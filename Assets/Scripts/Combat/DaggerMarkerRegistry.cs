using System.Collections.Generic;
using UnityEngine;

public sealed class DaggerMarkerRegistry
{
    private const float DefaultMarkerDuration = 5f;

    private static readonly DaggerMarkerRegistry s_instance = new();
    private readonly Dictionary<EnemyController, MarkerState> _markers = new();
    private readonly List<EnemyController> _scratchExpired = new();

    public static DaggerMarkerRegistry Instance => s_instance;

    private struct MarkerState
    {
        public float ExpiresAt;
    }

    private DaggerMarkerRegistry()
    {
    }

    public void Tick(float deltaTime)
    {
        if (_markers.Count == 0)
            return;

        _scratchExpired.Clear();
        foreach (KeyValuePair<EnemyController, MarkerState> pair in _markers)
        {
            EnemyController enemy = pair.Key;
            if (enemy == null || !enemy.IsAlive)
            {
                _scratchExpired.Add(enemy);
                continue;
            }

            if (Time.time >= pair.Value.ExpiresAt)
            {
                _scratchExpired.Add(enemy);
            }
        }

        for (int i = 0; i < _scratchExpired.Count; i++)
            Clear(_scratchExpired[i]);
        _scratchExpired.Clear();
    }

    public void Apply(EnemyController enemy, float duration)
    {
        if (enemy == null || !enemy.IsAlive)
            return;

        bool isNew = !_markers.ContainsKey(enemy);
        _markers[enemy] = new MarkerState
        {
            ExpiresAt = Time.time + ResolveDuration(duration)
        };

        if (isNew)
            enemy.OnDied += HandleEnemyDied;

        DaggerMarkerVisualPool.Active?.Show(enemy);
    }

    public bool Has(EnemyController enemy)
    {
        if (enemy == null || !enemy.IsAlive)
            return false;

        return _markers.ContainsKey(enemy);
    }

    public bool Detonate(EnemyController enemy)
    {
        if (!Has(enemy))
            return false;

        DaggerMarkerVisualPool.Active?.Burst(enemy);
        Clear(enemy);
        return true;
    }

    public void Clear(EnemyController enemy)
    {
        if (ReferenceEquals(enemy, null))
            return;

        if (!_markers.Remove(enemy))
            return;

        enemy.OnDied -= HandleEnemyDied;
        DaggerMarkerVisualPool.Active?.Hide(enemy);
    }

    private void HandleEnemyDied(EnemyController enemy)
    {
        Clear(enemy);
    }

    private static float ResolveDuration(float duration)
    {
        return duration > 0f ? duration : DefaultMarkerDuration;
    }
}
