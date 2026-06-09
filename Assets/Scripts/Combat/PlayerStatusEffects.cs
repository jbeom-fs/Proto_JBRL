using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerStatusEffects
{
    private readonly Action<Vector2> _applyDisplacement;
    private readonly List<PlayerSlowEffect> _enemySlows = new();
    private float _enemySlowMultiplier = 1f;
    private float _stunTimer;
    private float _slowTotalDurationForUi;
    private float _stunTotalDurationForUi;
    private Vector2 _knockbackDir;
    private float _knockbackSpeed;
    private float _knockbackRemaining;

    private struct PlayerSlowEffect
    {
        public float Multiplier;
        public float Timer;
    }

    public PlayerStatusEffects(Action<Vector2> applyDisplacement)
    {
        _applyDisplacement = applyDisplacement;
    }

    public bool IsStunned => _stunTimer > 0f;
    public bool IsSlowed => _enemySlows.Count > 0;
    public float StunRemainingTime => _stunTimer;
    public float SlowRemainingTime => GetSlowRemainingTime();
    public float StunTotalDurationForUi => _stunTotalDurationForUi;
    public float SlowTotalDurationForUi => _slowTotalDurationForUi;
    public float StunRemainingRatio =>
        _stunTotalDurationForUi > 0f ? Mathf.Clamp01(_stunTimer / _stunTotalDurationForUi) : 0f;
    public float SlowRemainingRatio =>
        _slowTotalDurationForUi > 0f ? Mathf.Clamp01(SlowRemainingTime / _slowTotalDurationForUi) : 0f;
    public float MoveSpeedMultiplier => IsStunned ? 0f : _enemySlowMultiplier;

    public event Action<PlayerStatusEffectType> OnApplied;
    public event Action<PlayerStatusEffectType> OnEnded;

    public void ApplyKnockback(Vector2 direction, float distance, float duration)
    {
        if (duration <= 0f || distance <= 0f)
            return;

        float clampedDuration = Mathf.Max(0.01f, duration);
        _knockbackDir = direction;
        _knockbackSpeed = Mathf.Max(0f, distance) / clampedDuration;
        _knockbackRemaining = clampedDuration;
    }

    public void ApplySlow(float multiplier, float duration)
    {
        if (duration <= 0f || multiplier <= 0f || multiplier >= 1f)
            return;

        _slowTotalDurationForUi = Mathf.Max(_slowTotalDurationForUi, duration);
        _enemySlows.Add(new PlayerSlowEffect
        {
            Multiplier = Mathf.Clamp01(multiplier),
            Timer = duration
        });
        RecalculateEnemySlowMultiplier();
        OnApplied?.Invoke(PlayerStatusEffectType.Slow);
    }

    public void ApplyStun(float duration)
    {
        if (duration <= 0f)
            return;

        _stunTimer = Mathf.Max(_stunTimer, duration);
        _stunTotalDurationForUi = Mathf.Max(_stunTotalDurationForUi, duration);
        OnApplied?.Invoke(PlayerStatusEffectType.Stun);
    }

    public void Tick(float deltaTime)
    {
        TickStun(deltaTime);
        TickSlowEffects(deltaTime);
        TickKnockback(deltaTime);
    }

    public void ClearAll()
    {
        bool wasSlowed = IsSlowed;
        bool wasStunned = IsStunned;

        _knockbackRemaining = 0f;
        _knockbackSpeed = 0f;
        _knockbackDir = Vector2.zero;
        _enemySlows.Clear();
        _enemySlowMultiplier = 1f;
        _slowTotalDurationForUi = 0f;
        _stunTimer = 0f;
        _stunTotalDurationForUi = 0f;

        if (wasSlowed)
            OnEnded?.Invoke(PlayerStatusEffectType.Slow);
        if (wasStunned)
            OnEnded?.Invoke(PlayerStatusEffectType.Stun);
    }

    private void TickKnockback(float deltaTime)
    {
        if (_knockbackRemaining <= 0f)
            return;

        _knockbackRemaining -= deltaTime;
        _applyDisplacement?.Invoke(_knockbackDir * (_knockbackSpeed * deltaTime));
    }

    private void TickStun(float deltaTime)
    {
        if (_stunTimer <= 0f)
            return;

        _stunTimer = Mathf.Max(0f, _stunTimer - deltaTime);
        if (_stunTimer <= 0f)
        {
            _stunTotalDurationForUi = 0f;
            OnEnded?.Invoke(PlayerStatusEffectType.Stun);
        }
    }

    private void TickSlowEffects(float deltaTime)
    {
        if (_enemySlows.Count == 0)
            return;

        bool changed = false;
        bool wasSlowed = _enemySlows.Count > 0;
        for (int i = _enemySlows.Count - 1; i >= 0; i--)
        {
            PlayerSlowEffect effect = _enemySlows[i];
            effect.Timer -= deltaTime;
            if (effect.Timer <= 0f)
            {
                _enemySlows.RemoveAt(i);
                changed = true;
            }
            else
            {
                _enemySlows[i] = effect;
            }
        }

        if (changed)
            RecalculateEnemySlowMultiplier();

        if (wasSlowed && _enemySlows.Count == 0)
        {
            _slowTotalDurationForUi = 0f;
            OnEnded?.Invoke(PlayerStatusEffectType.Slow);
        }
    }

    private void RecalculateEnemySlowMultiplier()
    {
        float multiplier = 1f;
        for (int i = 0; i < _enemySlows.Count; i++)
            multiplier = Mathf.Min(multiplier, _enemySlows[i].Multiplier);

        _enemySlowMultiplier = multiplier;
    }

    private float GetSlowRemainingTime()
    {
        float remaining = 0f;
        for (int i = 0; i < _enemySlows.Count; i++)
            remaining = Mathf.Max(remaining, _enemySlows[i].Timer);

        return remaining;
    }
}
