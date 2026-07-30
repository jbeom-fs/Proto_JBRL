using UnityEngine;

public sealed class PlayerShield
{
    private int _amount;
    private float _remaining;
    private bool _infinite;

    public int CurrentAmount => _amount;
    public bool IsActive => _amount > 0;
    public event System.Action OnChanged;

    public void Grant(int amount, float duration)
    {
        if (amount <= 0)
            return;

        _amount = Mathf.Max(_amount, amount);
        _infinite = duration <= 0f;
        _remaining = _infinite ? 0f : duration;
        OnChanged?.Invoke();
    }

    public void Add(int amount, float duration)
    {
        if (amount <= 0)
            return;

        _amount += amount;
        _infinite = duration <= 0f;
        _remaining = _infinite ? 0f : duration;
        OnChanged?.Invoke();
    }

    public int Absorb(int damage)
    {
        if (damage <= 0 || _amount <= 0)
            return damage;

        int absorbed = Mathf.Min(_amount, damage);
        _amount -= absorbed;
        if (_amount <= 0)
        {
            _amount = 0;
            _remaining = 0f;
            _infinite = false;
        }

        OnChanged?.Invoke();
        return damage - absorbed;
    }

    public void Tick(float dt)
    {
        if (_amount <= 0 || _infinite)
            return;

        _remaining -= dt;
        if (_remaining <= 0f)
        {
            _amount = 0;
            _remaining = 0f;
            OnChanged?.Invoke();
        }
    }

    public void Clear()
    {
        if (_amount == 0 && _remaining == 0f)
            return;

        _amount = 0;
        _remaining = 0f;
        _infinite = false;
        OnChanged?.Invoke();
    }
}
