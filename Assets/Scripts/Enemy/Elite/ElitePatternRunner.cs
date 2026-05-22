using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ElitePatternRunner : MonoBehaviour
{
    private readonly ProjectileFireService _projectileFireService = new ProjectileFireService();
    private readonly ElitePatternContext _context = new ElitePatternContext();
    private readonly List<ElitePatternData> _patterns = new List<ElitePatternData>(8);
    private readonly Dictionary<ElitePatternData, float> _cooldowns = new Dictionary<ElitePatternData, float>(8);

    private EnemyBrain _brain;
    private EnemyAnimationController _animation;
    private Collider2D _collider;
    private ElitePatternRuntime _currentRuntime;
    private ElitePatternData _currentPattern;
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
        _brain = brain;
        CacheComponents();
        RebuildPatterns();
        _initialized = true;
    }

    public void ResetRuntimeState()
    {
        CancelCurrent();
        for (int i = 0; i < _patterns.Count; i++)
            _cooldowns[_patterns[i]] = 0f;
        _active = false;
    }

    public void Tick(float deltaTime)
    {
        if (!_initialized || _brain == null || _brain.Data == null)
            return;

        _context.RefreshRuntimeRefs();
        bool shouldRun = _brain.Data.IsElite && _brain.Data.ElitePatternSet != null;
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
        _cooldowns.Clear();

        EnemyData data = _brain != null ? _brain.Data : null;
        ElitePatternSet set = data != null && data.IsElite ? data.ElitePatternSet : null;
        if (set == null || set.Patterns == null)
            return;

        IReadOnlyList<ElitePatternData> source = set.Patterns;
        for (int i = 0; i < source.Count; i++)
        {
            ElitePatternData pattern = source[i];
            if (pattern == null || pattern.Weight <= 0)
                continue;

            _patterns.Add(pattern);
            _cooldowns[pattern] = 0f;
        }
    }

    private void TickCooldowns(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        for (int i = 0; i < _patterns.Count; i++)
        {
            ElitePatternData pattern = _patterns[i];
            float cooldown = _cooldowns[pattern];
            if (cooldown > 0f)
                _cooldowns[pattern] = Mathf.Max(0f, cooldown - deltaTime);
        }
    }

    private void TryStartNextPattern()
    {
        if (_patterns.Count == 0 || _brain.Target == null || !_brain.Target.HasTarget)
            return;

        float distance = Mathf.Sqrt(_brain.Target.SqrDistanceToTarget);
        for (int i = 0; i < _patterns.Count; i++)
        {
            ElitePatternData pattern = _patterns[i];
            if (_cooldowns[pattern] > 0f || !pattern.IsInRange(distance))
                continue;

            ElitePatternRuntime runtime = pattern.CreateRuntime();
            if (runtime == null)
            {
                _cooldowns[pattern] = pattern.Cooldown;
                return;
            }

            _context.Initialize(_brain, _animation, _collider, _projectileFireService, this);
            _currentPattern = pattern;
            _currentRuntime = runtime;
            _currentRuntime.Start(_context);

            if (_currentRuntime.IsFinished)
                FinishCurrent();

            return;
        }
    }

    private void FinishCurrent()
    {
        if (_currentPattern != null)
            _cooldowns[_currentPattern] = _currentPattern.Cooldown;

        _currentRuntime = null;
        _currentPattern = null;
    }

    private void CancelCurrent()
    {
        if (_currentRuntime != null)
            _currentRuntime.Cancel();

        _currentRuntime = null;
        _currentPattern = null;
    }
}
