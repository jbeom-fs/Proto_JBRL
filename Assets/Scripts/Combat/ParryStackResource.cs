using UnityEngine;

public sealed class ParryStackResource
{
    private readonly int _maxStack;
    private readonly float _graceDuration;
    private readonly float _decayInterval;
    private readonly int _decayAmount;
    private int _current;
    private float _graceTimer;
    private float _decayTimer;

    public ParryStackResource(int maxStack, float graceDuration, float decayInterval, int decayAmount)
    {
        _maxStack = Mathf.Max(0, maxStack);
        _graceDuration = Mathf.Max(0f, graceDuration);
        _decayInterval = Mathf.Max(0.01f, decayInterval);
        _decayAmount = Mathf.Max(1, decayAmount);
    }

    public int Current => _current;
    public int Max => _maxStack;

    public bool Has(int amount)
    {
        return _current >= Mathf.Max(0, amount);
    }

    public void Add(int amount)
    {
        if (amount <= 0)
            return;

        _current = Mathf.Min(_maxStack, _current + amount);
        if (_current > 0)
        {
            _graceTimer = Mathf.Max(0f, _graceDuration);
            _decayTimer = Mathf.Max(0.01f, _decayInterval);
        }
    }

    public bool Spend(int amount)
    {
        if (amount <= 0)
            return true;

        if (!Has(amount))
            return false;

        _current = Mathf.Max(0, _current - amount);
        if (_current == 0)
            ResetTimers();
        return true;
    }

    public void Restore(int amount)
    {
        Add(amount);
    }

    public void Reset()
    {
        _current = 0;
        ResetTimers();
    }

    public void Tick(float deltaTime)
    {
        if (_current <= 0)
            return;

        if (_graceTimer > 0f)
        {
            _graceTimer = Mathf.Max(0f, _graceTimer - deltaTime);
            if (_graceTimer > 0f)
                return;
        }

        _decayTimer -= deltaTime;
        if (_decayTimer > 0f)
            return;

        int decay = Mathf.Max(1, _decayAmount);
        _current = Mathf.Max(0, _current - decay);
        _decayTimer = Mathf.Max(0.01f, _decayInterval);
        if (_current == 0)
            ResetTimers();
    }

    private void ResetTimers()
    {
        _graceTimer = 0f;
        _decayTimer = 0f;
    }
}
