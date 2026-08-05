using System.Collections.Generic;
using UnityEngine;

public sealed class FreischutzFocusRegistry
{
    private const float DefaultDuration = 4f;

    private static readonly FreischutzFocusRegistry s_instance = new();
    private readonly Dictionary<EnemyController, FocusState> _focusStacks = new();
    private readonly List<EnemyController> _scratchExpired = new();

    public static FreischutzFocusRegistry Instance => s_instance;

    private struct FocusState
    {
        public int Count;
        public float ExpiresAt;
    }

    private FreischutzFocusRegistry()
    {
    }

    public void Tick(float deltaTime)
    {
        if (_focusStacks.Count == 0)
            return;

        _scratchExpired.Clear();
        foreach (KeyValuePair<EnemyController, FocusState> pair in _focusStacks)
        {
            EnemyController enemy = pair.Key;
            if (enemy == null || !enemy.IsAlive || Time.time >= pair.Value.ExpiresAt)
                _scratchExpired.Add(enemy);
        }

        for (int i = 0; i < _scratchExpired.Count; i++)
            Clear(_scratchExpired[i]);
        _scratchExpired.Clear();
    }

    public int AddStack(EnemyController enemy, float duration)
    {
        if (enemy == null || !enemy.IsAlive)
            return 0;

        bool isNew = !_focusStacks.TryGetValue(enemy, out FocusState state);
        state.Count = state.Count < int.MaxValue ? state.Count + 1 : int.MaxValue;
        state.ExpiresAt = Time.time + ResolveDuration(duration);
        _focusStacks[enemy] = state;

        if (isNew)
            enemy.OnDied += HandleEnemyDied;

        return state.Count;
    }

    public int GetStack(EnemyController enemy)
    {
        if (enemy == null || !enemy.IsAlive)
            return 0;

        return _focusStacks.TryGetValue(enemy, out FocusState state)
            ? state.Count
            : 0;
    }

    public void Clear(EnemyController enemy)
    {
        if (ReferenceEquals(enemy, null))
            return;

        if (!_focusStacks.Remove(enemy))
            return;

        enemy.OnDied -= HandleEnemyDied;
    }

    public void ClearAll()
    {
        if (_focusStacks.Count == 0)
            return;

        _scratchExpired.Clear();
        foreach (EnemyController enemy in _focusStacks.Keys)
            _scratchExpired.Add(enemy);

        for (int i = 0; i < _scratchExpired.Count; i++)
            Clear(_scratchExpired[i]);
        _scratchExpired.Clear();
    }

    private void HandleEnemyDied(EnemyController enemy)
    {
        Clear(enemy);
    }

    private static float ResolveDuration(float duration)
    {
        return duration > 0f ? duration : DefaultDuration;
    }
}
