using UnityEngine;

/// <summary>
/// Tracks a running combo count that increments on every landed attack action
/// and fully resets if no hit lands within a sliding time window. Used as the
/// stack source for the Sword <see cref="SoulStatType.ComboDamage"/> bonus.
/// </summary>
public sealed class ComboMeter
{
    private readonly float _window;
    private readonly int _maxStack;
    private int _count;
    private float _windowTimer;

    public ComboMeter(float window, int maxStack)
    {
        _window = Mathf.Max(0.01f, window);
        _maxStack = Mathf.Max(0, maxStack);
    }

    public int Count => _count;

    /// <summary>Registers a single landed attack action (one swing/projectile = one step).</summary>
    public void RegisterHit()
    {
        _count = Mathf.Min(_maxStack, _count + 1);
        _windowTimer = _window;
    }

    public void Tick(float deltaTime)
    {
        if (_count <= 0)
            return;

        _windowTimer -= deltaTime;
        if (_windowTimer <= 0f)
        {
            _count = 0;
            _windowTimer = 0f;
        }
    }

    public void Reset()
    {
        _count = 0;
        _windowTimer = 0f;
    }
}
