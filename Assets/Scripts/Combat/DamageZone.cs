using UnityEngine;

public sealed class DamageZone : MonoBehaviour
{
    private const int EnemyHitBufferSize = 64;
    private const float MinTickInterval = 0.01f;
    private static readonly int s_ZoneStateHash = Animator.StringToHash("Base Layer.Zone");
    private static bool s_ReportedMissingAnimationScaleSprite;

    private readonly Collider2D[] _enemyHitBuffer = new Collider2D[EnemyHitBufferSize];
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;
    private DamageZoneSpawner _owner;
    private ZonePayload _payload;
    private float _remainingDuration;
    private float _radius;
    private float _tickInterval;
    private float _tickTimer;

    internal void SetPoolOwner(DamageZoneSpawner owner)
    {
        _owner = owner;
    }

    public void Initialize(
        Sprite sprite,
        AnimatorOverrideController animation,
        in ZonePayload payload)
    {
        _payload = payload;
        _remainingDuration = Mathf.Max(0f, payload.Duration);
        _radius = Mathf.Max(0f, payload.Radius);
        _tickInterval = Mathf.Max(MinTickInterval, payload.TickInterval);
        _tickTimer = _tickInterval;
        UpdateVisual(sprite, animation);
    }

    private void UpdateVisual(Sprite sprite, AnimatorOverrideController animation)
    {
        if (animation != null)
        {
            if (spriteRenderer != null)
                spriteRenderer.enabled = true;

            if (animator != null)
            {
                animator.enabled = true;
                if (animator.runtimeAnimatorController != animation)
                    animator.runtimeAnimatorController = animation;
                animator.Rebind();
                animator.Play(s_ZoneStateHash, 0, 0f);
            }
        }
        else
        {
            if (animator != null)
                animator.enabled = false;

            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = sprite;
                spriteRenderer.enabled = sprite != null;
            }
        }

        if (sprite == null)
        {
            if (animation != null)
            {
                ReportMissingAnimationScaleSpriteOnce();
                if (spriteRenderer != null)
                    spriteRenderer.transform.localScale = Vector3.one;
            }
            return;
        }

        if (spriteRenderer == null)
            return;

        Vector2 size = sprite.bounds.size;
        float longest = Mathf.Max(size.x, size.y);
        float diameter = _radius * 2f;
        float scale = longest > 0.0001f ? diameter / longest : 1f;
        spriteRenderer.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void ReportMissingAnimationScaleSpriteOnce()
    {
        if (s_ReportedMissingAnimationScaleSprite)
            return;

        s_ReportedMissingAnimationScaleSprite = true;
        Debug.LogWarning(
            "[DamageZone] zoneAnimation is assigned without zoneSprite. " +
            "Animation scale falls back to 1 because no reference sprite is available.",
            this);
    }

    private void Update()
    {
        float activeDelta = Mathf.Min(Time.deltaTime, _remainingDuration);
        _remainingDuration -= Time.deltaTime;
        _tickTimer -= activeDelta;

        while (_tickTimer <= 0f)
        {
            ApplyTick();
            _tickTimer += _tickInterval;
        }

        if (_remainingDuration <= 0f)
            ReturnToPool();
    }

    private void ApplyTick()
    {
        int count = Physics2D.OverlapCircle(
            transform.position,
            _radius,
            CombatLayers.EnemyFilter,
            _enemyHitBuffer);

        for (int i = 0; i < count; i++)
        {
            Collider2D hit = _enemyHitBuffer[i];
            if (hit == null || !hit.TryGetComponent(out EnemyController enemy) || !enemy.IsAlive)
                continue;

            if (_payload.TickDamage > 0)
            {
                enemy.ApplyCombatImpact(
                    _payload.TickDamage,
                    transform.position,
                    0f,
                    0f,
                    _payload.SlowPercentage,
                    _payload.SlowDuration,
                    _payload.Ailments,
                    _payload.EffectContext);
                continue;
            }

            // damage == 0 uses status-only APIs to avoid ApplyCombatImpact's minimum damage.
            enemy.ApplySlowEffect(_payload.SlowPercentage, _payload.SlowDuration);
            CombatEffectContext effectContext =
                _payload.EffectContext;
            enemy.ApplyAilments(_payload.Ailments, in effectContext);
        }
    }

    private void ReturnToPool()
    {
        if (_owner != null)
            _owner.Release(this);
        else
            gameObject.SetActive(false);
    }
}
