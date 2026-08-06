using System;
using UnityEngine;

public sealed class RecoilMotion
{
    private readonly Func<Vector2, bool> _displace;
    private Vector2 _remaining;
    private float _remainingTime;

    public RecoilMotion(Func<Vector2, bool> displace)
    {
        _displace = displace;
    }

    public bool IsActive => _remaining.sqrMagnitude > 0f;

    public void Add(Vector2 delta, float duration)
    {
        if (duration <= 0f)
        {
            _displace?.Invoke(delta);
            return;
        }

        _remaining += delta;
        _remainingTime = Mathf.Max(_remainingTime, duration);
    }

    public void Tick(float deltaTime)
    {
        if (!IsActive || deltaTime <= 0f)
            return;

        Vector2 step = _remainingTime <= deltaTime
            ? _remaining
            : _remaining * (deltaTime / _remainingTime);
        _remainingTime -= deltaTime;

        // Keep this threshold aligned with PlayerController.TryApplyExternalDisplacement.
        if (step.sqrMagnitude > 0.000001f)
        {
            _remaining -= step;
            if (_displace == null || !_displace(step))
            {
                Clear();
                return;
            }
        }

        if (_remainingTime <= 0f)
            Clear();
    }

    public void Clear()
    {
        _remaining = Vector2.zero;
        _remainingTime = 0f;
    }
}
