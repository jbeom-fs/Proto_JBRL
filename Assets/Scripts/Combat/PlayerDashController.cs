using System.Collections;
using UnityEngine;

/// <summary>
/// Handles player dash movement for skill execution.
/// It owns only movement timing/path safety; damage, invulnerability, VFX, and camera effects can be layered later.
/// </summary>
public sealed class PlayerDashController : MonoBehaviour
{
    private const float MinDashMoveDistance = 0.05f;
    private const float MinSampleStep = 0.05f;

    private readonly Vector3[] _corners = new Vector3[4];
    private Coroutine _dashRoutine;
    private CircleCollider2D _circleCollider;
    private DungeonManager _dungeonManager;
    private PlayerCombatController _activeInvincibilityOwner;

    public bool IsDashing => _dashRoutine != null;

    private void Awake()
    {
        _circleCollider = GetComponent<CircleCollider2D>();
        _dungeonManager = DungeonManager.Instance;
    }

    private void OnDisable()
    {
        if (_dashRoutine != null)
        {
            StopCoroutine(_dashRoutine);
            _dashRoutine = null;
        }

        ClearDashInvincibility();
    }

    public bool TryStartDash(
        PlayerCombatController caster,
        Vector2 direction,
        float distance,
        float duration,
        bool stopOnWall,
        bool invincibleDuringDash)
    {
        if (_dashRoutine != null) return false;
        if (caster == null || caster.IsDead || !caster.isActiveAndEnabled) return false;
        if (!gameObject.activeInHierarchy || !enabled) return false;

        direction = ResolveDirection(direction);
        distance = Mathf.Max(0f, distance);
        if (distance <= MinDashMoveDistance) return false;

        _dungeonManager = DungeonManager.Instance;
        if (_dungeonManager == null || _dungeonManager.Data == null) return false;

        Vector3 start = transform.position;
        if (!TryResolveDestination(start, direction, distance, stopOnWall, out Vector3 destination))
            return false;

        if ((destination - start).sqrMagnitude < MinDashMoveDistance * MinDashMoveDistance)
            return false;

        if (invincibleDuringDash)
            BeginDashInvincibility(caster, Mathf.Max(0f, duration));

        _dashRoutine = StartCoroutine(DashRoutine(caster, start, destination, Mathf.Max(0f, duration)));
        return true;
    }

    private IEnumerator DashRoutine(PlayerCombatController caster, Vector3 start, Vector3 destination, float duration)
    {
        try
        {
            if (duration <= 0f)
            {
                transform.position = destination;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (ShouldCancel(caster))
                    break;

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transform.position = Vector3.Lerp(start, destination, t);
                yield return null;
            }

            if (!ShouldCancel(caster))
                transform.position = destination;
        }
        finally
        {
            _dashRoutine = null;
            ClearDashInvincibility();
        }
    }

    private bool TryResolveDestination(
        Vector3 start,
        Vector2 direction,
        float distance,
        bool stopOnWall,
        out Vector3 destination)
    {
        destination = start;

        float tileSize = ResolveTileSize();
        float sampleStep = Mathf.Max(MinSampleStep, tileSize * 0.25f);
        Vector3 worldDirection = new Vector3(direction.x, direction.y, 0f);
        Vector3 lastWalkable = start;

        for (float moved = sampleStep; moved < distance + sampleStep; moved += sampleStep)
        {
            float clampedDistance = Mathf.Min(moved, distance);
            Vector3 candidate = start + worldDirection * clampedDistance;
            if (!IsFootprintWalkable(candidate))
            {
                if (!stopOnWall)
                    return false;

                destination = lastWalkable;
                return true;
            }

            lastWalkable = candidate;
        }

        destination = lastWalkable;
        return true;
    }

    private bool IsFootprintWalkable(Vector3 position)
    {
        float radius = ResolveFootprintRadius();
        _corners[0] = new Vector3(position.x - radius, position.y - radius, 0f);
        _corners[1] = new Vector3(position.x + radius, position.y - radius, 0f);
        _corners[2] = new Vector3(position.x - radius, position.y + radius, 0f);
        _corners[3] = new Vector3(position.x + radius, position.y + radius, 0f);

        for (int i = 0; i < _corners.Length; i++)
        {
            Vector2Int grid = _dungeonManager.WorldToGrid(_corners[i]);
            if (!_dungeonManager.IsWalkable(grid.x, grid.y))
                return false;
        }

        return true;
    }

    private float ResolveTileSize()
    {
        if (_dungeonManager != null &&
            _dungeonManager.dungeonRenderer != null &&
            _dungeonManager.dungeonRenderer.tilemap != null)
        {
            return Mathf.Max(0.01f, _dungeonManager.dungeonRenderer.tilemap.cellSize.x);
        }

        return 1f;
    }

    private float ResolveFootprintRadius()
    {
        if (_circleCollider == null)
            _circleCollider = GetComponent<CircleCollider2D>();

        if (_circleCollider == null)
            return Mathf.Max(0.01f, ResolveTileSize() * 0.2f);

        float maxScale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y));

        return Mathf.Max(0.01f, _circleCollider.radius * maxScale);
    }

    private static Vector2 ResolveDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector2.down;

        return direction.normalized;
    }

    private static bool ShouldCancel(PlayerCombatController caster)
    {
        return caster == null ||
               caster.IsDead ||
               !caster.isActiveAndEnabled ||
               !caster.gameObject.activeInHierarchy;
    }

    private void BeginDashInvincibility(PlayerCombatController caster, float visualDuration)
    {
        ClearDashInvincibility();
        _activeInvincibilityOwner = caster;
        _activeInvincibilityOwner.BeginExternalInvincibility(visualDuration);
    }

    private void ClearDashInvincibility()
    {
        if (_activeInvincibilityOwner == null)
            return;

        _activeInvincibilityOwner.EndExternalInvincibility();
        _activeInvincibilityOwner = null;
    }
}
