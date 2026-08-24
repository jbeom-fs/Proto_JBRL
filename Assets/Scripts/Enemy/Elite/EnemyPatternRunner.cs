using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyPatternRunner : MonoBehaviour
{
    private readonly ProjectileFireService _projectileFireService = new ProjectileFireService();
    private readonly EnemyPatternContext _context = new EnemyPatternContext();
    private readonly List<EnemyPatternData> _patterns = new List<EnemyPatternData>(8);
    private readonly List<EnemyPatternRuntime> _runtimes = new List<EnemyPatternRuntime>(8);
    private readonly List<float> _cooldowns = new List<float>(8);
    private readonly List<int> _eligiblePatternIndices = new List<int>(8);

    private EnemyBrain _brain;
    private EnemyAnimationController _animation;
    private Collider2D _collider;
    private EnemyPatternSet _overridePatternSet;
    private EnemyPatternRuntime _currentRuntime;
    private EnemyPatternData _currentPattern;
    private int _currentPatternIndex = -1;
    private bool _initialized;
    private bool _active;

    public bool IsRunning => _currentRuntime != null && !_currentRuntime.IsFinished;

    private void Awake()
    {
        CacheComponents();
    }

    private void OnDisable()
    {
        ResetRuntimeState();
    }

    public void Initialize(EnemyBrain brain)
    {
        CancelCurrent();
        _brain = brain;
        CacheComponents();
        RebuildPatterns();
        _initialized = true;
        _active = ResolvePatternSet() != null;
    }

    public void SetPatternSet(EnemyPatternSet set)
    {
        CancelCurrent();
        _overridePatternSet = set;
        RebuildPatterns();
        _active = _initialized && ResolvePatternSet() != null;
    }

    public void ResetRuntimeState()
    {
        CancelCurrent();
        _overridePatternSet = null;
        for (int i = 0; i < _cooldowns.Count; i++)
            _cooldowns[i] = 0f;
        _eligiblePatternIndices.Clear();
        _active = false;
    }

    public void Tick(float deltaTime)
    {
        if (!_initialized || _brain == null || _brain.Data == null)
            return;

        bool shouldRun = ResolvePatternSet() != null;
        if (!shouldRun || _brain.Enemy == null || !_brain.Enemy.IsAlive || _brain.Enemy.IsDead)
        {
            CancelCurrent();
            _active = false;
            return;
        }

        if (!_active)
        {
            RebuildPatterns();
            _active = true;
        }

        TickCooldowns(deltaTime);

        if (_currentRuntime != null)
        {
            _currentRuntime.Tick(deltaTime);
            if (!_currentRuntime.IsFinished)
                return;

            FinishCurrent();
        }

        TryStartNextPattern();
    }

    private void CacheComponents()
    {
        if (_brain == null)
            _brain = GetComponent<EnemyBrain>();
        if (_animation == null)
            _animation = GetComponentInChildren<EnemyAnimationController>(true);
        if (_collider == null)
            _collider = GetComponent<Collider2D>();
    }

    private void RebuildPatterns()
    {
        _patterns.Clear();
        _runtimes.Clear();
        _cooldowns.Clear();
        _eligiblePatternIndices.Clear();

        EnemyPatternSet set = ResolvePatternSet();
        if (set == null || set.Patterns == null)
            return;

        IReadOnlyList<EnemyPatternData> source = set.Patterns;
        for (int i = 0; i < source.Count; i++)
        {
            EnemyPatternData pattern = source[i];
            if (pattern == null || pattern.Weight <= 0)
                continue;

            _patterns.Add(pattern);
            _runtimes.Add(pattern.CreateRuntime());
            _cooldowns.Add(0f);
        }
    }

    private EnemyPatternSet ResolvePatternSet()
    {
        EnemyData data = _brain != null ? _brain.Data : null;
        return _overridePatternSet != null
            ? _overridePatternSet
            : data != null ? data.PatternSet : null;
    }

    private void TickCooldowns(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        for (int i = 0; i < _cooldowns.Count; i++)
        {
            float cooldown = _cooldowns[i];
            if (cooldown > 0f)
                _cooldowns[i] = Mathf.Max(0f, cooldown - deltaTime);
        }
    }

    private void TryStartNextPattern()
    {
        if (_patterns.Count == 0 || _brain.Target == null || !_brain.Target.HasTarget)
            return;

        _eligiblePatternIndices.Clear();
        float distance = Mathf.Sqrt(_brain.Target.SqrDistanceToTarget);
        for (int i = 0; i < _patterns.Count; i++)
        {
            EnemyPatternData pattern = _patterns[i];
            if (_cooldowns[i] > 0f || !pattern.IsInRange(distance))
                continue;

            _eligiblePatternIndices.Add(i);
        }

        if (_eligiblePatternIndices.Count == 0)
            return;

        _context.Initialize(_brain, _animation, _collider, _projectileFireService, this);
        while (_eligiblePatternIndices.Count > 0)
        {
            float totalWeight = 0f;
            for (int i = 0; i < _eligiblePatternIndices.Count; i++)
                totalWeight += _patterns[_eligiblePatternIndices[i]].Weight;

            if (totalWeight <= 0f)
                return;

            float roll = UnityEngine.Random.value * totalWeight;
            float accumulated = 0f;
            int selectedListIndex = _eligiblePatternIndices.Count - 1;
            int selectedIndex = _eligiblePatternIndices[selectedListIndex];
            for (int i = 0; i < _eligiblePatternIndices.Count; i++)
            {
                int candidateIndex = _eligiblePatternIndices[i];
                accumulated += _patterns[candidateIndex].Weight;
                if (roll < accumulated)
                {
                    selectedListIndex = i;
                    selectedIndex = candidateIndex;
                    break;
                }
            }

            int lastListIndex = _eligiblePatternIndices.Count - 1;
            _eligiblePatternIndices[selectedListIndex] = _eligiblePatternIndices[lastListIndex];
            _eligiblePatternIndices.RemoveAt(lastListIndex);

            EnemyPatternData selectedPattern = _patterns[selectedIndex];
            EnemyPatternRuntime runtime = _runtimes[selectedIndex];
            if (runtime == null)
            {
                _cooldowns[selectedIndex] = selectedPattern.Cooldown;
                continue;
            }

            _currentPattern = selectedPattern;
            _currentPatternIndex = selectedIndex;
            _currentRuntime = runtime;
            if (_currentRuntime.Start(_context))
            {
                if (_currentRuntime.IsFinished)
                    FinishCurrent();
                return;
            }

            _currentPattern = null;
            _currentPatternIndex = -1;
            _currentRuntime = null;
        }
    }

    private void FinishCurrent()
    {
        if (_currentPattern != null &&
            _currentPatternIndex >= 0 &&
            _currentPatternIndex < _cooldowns.Count)
        {
            _cooldowns[_currentPatternIndex] = _currentPattern.Cooldown;
        }

        _currentRuntime = null;
        _currentPattern = null;
        _currentPatternIndex = -1;
    }

    private void CancelCurrent()
    {
        if (_currentRuntime != null)
            _currentRuntime.Cancel();

        _currentRuntime = null;
        _currentPattern = null;
        _currentPatternIndex = -1;
    }
}
