using System;

/// <summary>
/// Tracks tiered combo progress for the Sword <see cref="SoulStatType.ComboDamage"/> bonus.
/// </summary>
public sealed class ComboMeter
{
    private readonly int _stacksPerTier;
    private readonly int _maxTier;
    private readonly float _window;
    private readonly int _gainPerHit;
    private int _tier;
    private int _progress;
    private float _windowTimer;

    public ComboMeter(int stacksPerTier, int maxTier, float window, int gainPerHit)
    {
        _stacksPerTier = Math.Max(1, stacksPerTier);
        _maxTier = Math.Max(0, maxTier);
        _window = Math.Max(0.01f, window);
        _gainPerHit = Math.Max(0, gainPerHit);
    }

    public int TotalStacks => _tier * _stacksPerTier + _progress;
    public int Count => TotalStacks;
    public int Tier => _tier;
    public int Progress => _progress;
    public float WindowRemaining => TotalStacks > 0 ? Math.Max(0f, _windowTimer) : 0f;
    public float WindowRemainingNormalized => TotalStacks > 0
        ? Math.Min(1f, Math.Max(0f, _windowTimer / _window))
        : 0f;

    /// <summary>Registers one landed attack action and applies configured stack gain.</summary>
    public void RegisterHit()
    {
        if (_gainPerHit <= 0)
        {
            _windowTimer = _window;
            return;
        }

        AddStacks(_gainPerHit);
    }

    /// <summary>Adds an exact number of stacks. Used by hit gain and debug commands.</summary>
    public void AddStacks(int amount)
    {
        if (amount <= 0)
            return;

        _windowTimer = _window;
        if (_tier >= _maxTier)
            return;

        long nextTotal = Math.Min(
            (long)_maxTier * _stacksPerTier,
            (long)TotalStacks + amount);
        _tier = (int)(nextTotal / _stacksPerTier);
        _progress = _tier >= _maxTier ? 0 : (int)(nextTotal % _stacksPerTier);
    }

    public bool Spend(int amount)
    {
        if (amount <= 0)
            return true;

        if (TotalStacks < amount)
            return false;

        int nextTotal = TotalStacks - amount;
        _tier = nextTotal / _stacksPerTier;
        _progress = _tier >= _maxTier ? 0 : nextTotal % _stacksPerTier;
        return true;
    }

    public void Tick(float deltaTime, bool enemiesPresent)
    {
        if (TotalStacks <= 0)
            return;

        if (!enemiesPresent)
        {
            _windowTimer = _window;
            return;
        }

        if (deltaTime <= 0f)
            return;

        _windowTimer -= deltaTime;
        while (_windowTimer <= 0f && TotalStacks > 0)
        {
            if (_tier > 0)
                _tier--;

            _progress = 0;
            _windowTimer = TotalStacks > 0 ? _windowTimer + _window : 0f;
        }
    }

    public void Reset()
    {
        _tier = 0;
        _progress = 0;
        _windowTimer = 0f;
    }
}
