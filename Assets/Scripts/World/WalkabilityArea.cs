using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 하나의 전투 가능 공간(Elite Arena, Boss Arena, 추후 다른 특수 공간 등)을 표현하는 컴포넌트입니다.
///
/// 한 Area는 walk/wall Tilemap 한 쌍으로 표현되며, OnEnable에서 <see cref="WalkabilityQuery"/>에
/// 자기 자신을 등록하고 OnDisable에서 해제합니다. 런타임 Find를 쓰지 않고, Scene 안에 Inspector로
/// 명시 연결한 Tilemap만 사용합니다.
///
/// walkable 판단 규칙:
///   - center: walkTilemap에 tile이 있고 wallTilemap에 tile이 없으면 walkable.
///   - wallTilemap이 비어 있으면 wall 없음으로 처리(보수적).
///   - walkTilemap에 tile이 없으면 Area "밖"(IsInsideWorld == false).
///
/// 모든 query는 **각 Tilemap의 자체 WorldToCell**을 사용합니다.
/// walk/wall Tilemap이 서로 다른 transform/cellSize를 가져도 정상 동작합니다.
///
/// Footprint 판정은 의도적으로 너그럽게 설계되어 있습니다:
///   - center 셀은 반드시 walkable (엄격).
///   - 4-corner 샘플은 wallTilemap에 tile이 명시적으로 있을 때만 차단 (empty cell은 허용).
///   - 이는 "WalkTile 위인데 footprint corner가 boundary empty cell에 살짝 닿아 막히는" 케이스를 방지합니다.
///   - Inspector의 <see cref="footprintInsetMultiplier"/>로 corner 샘플 거리를 추가 완화 가능.
/// </summary>
[DisallowMultipleComponent]
public sealed class WalkabilityArea : MonoBehaviour
{
    [Tooltip("이 Area의 식별자입니다. 예: \"elite_arena\", \"boss_arena\". 디버깅/로그용도.")]
    [SerializeField] private string id = "area";

    [Tooltip("이 Area의 walk 가능 셀을 표현하는 Tilemap.")]
    [SerializeField] private Tilemap walkTilemap;

    [Tooltip("이 Area의 wall(차단) 셀을 표현하는 Tilemap. 비어 있으면 wall 없음으로 처리합니다.")]
    [SerializeField] private Tilemap wallTilemap;

    [Header("Footprint Tuning")]
    [Tooltip("Footprint 4-corner 샘플을 실제 radius 대비 이 비율 거리에 둡니다. 1.0이면 strict, 0.85 정도면 약간 완화.")]
    [SerializeField, Range(0.1f, 1f)] private float footprintInsetMultiplier = 0.85f;

    [Header("Debug")]
    [Tooltip("Footprint 판정 실패 시 어떤 셀이 왜 막혔는지 로그를 남깁니다. 1초당 1회로 throttle.")]
    [SerializeField] private bool debugLogFootprintFailures = false;

    [Tooltip("Selected 상태에서 Gizmo로 walkTilemap cellBounds를 그립니다.")]
    [SerializeField] private bool drawCellBoundsGizmo = true;

    private bool _warnedMissingWalkTilemap;
    private float _lastDebugLogTime;

    public string Id => id;
    public Tilemap WalkTilemap => walkTilemap;
    public Tilemap WallTilemap => wallTilemap;
    public float FootprintInsetMultiplier => footprintInsetMultiplier;

    private void OnEnable()
    {
        WalkabilityQuery.Register(this);
    }

    private void OnDisable()
    {
        WalkabilityQuery.Unregister(this);
    }

    // ═════════════════════════════════════════════════════════════════
    // 기본 query — 모두 각 Tilemap의 WorldToCell 기준으로 동작합니다.
    // walk/wall Tilemap이 서로 다른 transform을 가져도 정상입니다.
    // ═════════════════════════════════════════════════════════════════

    /// <summary>world 좌표가 이 Area 안인지(walk tile이 그 위치에 존재하는지) 반환합니다.</summary>
    public bool IsInsideWorld(Vector3 worldPosition)
    {
        if (!EnsureWalkTilemap()) return false;
        return walkTilemap.HasTile(walkTilemap.WorldToCell(worldPosition));
    }

    /// <summary>world 좌표가 이 Area 안인지(Vector2 오버로드).</summary>
    public bool IsInsideWorld(Vector2 worldPosition) => IsInsideWorld((Vector3)worldPosition);

    /// <summary>world 좌표 셀이 walkable(walk tile 있음 + wall tile 없음)인지 반환합니다.</summary>
    public bool IsWalkableWorld(Vector3 worldPosition)
    {
        if (!EnsureWalkTilemap()) return false;
        Vector3Int walkCell = walkTilemap.WorldToCell(worldPosition);
        if (!walkTilemap.HasTile(walkCell)) return false;
        return !IsWallAtWorld(worldPosition);
    }

    public bool IsWalkableWorld(Vector2 worldPosition) => IsWalkableWorld((Vector3)worldPosition);

    /// <summary>해당 world 좌표에 wall tile이 명시적으로 존재하는지 반환합니다. wall tilemap이 null이면 false.</summary>
    public bool IsWallAtWorld(Vector3 worldPosition)
    {
        if (wallTilemap == null) return false;
        Vector3Int wallCell = wallTilemap.WorldToCell(worldPosition);
        return wallTilemap.HasTile(wallCell);
    }

    /// <summary>해당 world 좌표가 wall로 취급되어 LOS/이동이 차단되어야 하는지 반환합니다. walk 영역 밖도 wall로 봅니다.</summary>
    public bool IsWallWorld(Vector3 worldPosition)
    {
        return !IsWalkableWorld(worldPosition);
    }

    /// <summary>
    /// Player/Enemy footprint(반경 radius)가 이 Area에서 차지 가능한지 판정합니다.
    ///
    /// 규칙:
    ///   - center 셀은 반드시 walkable.
    ///   - 4-corner는 radius * footprintInsetMultiplier 거리에서 평가하며,
    ///     wallTilemap에 tile이 명시적으로 있을 때만 차단합니다. empty cell은 허용.
    /// </summary>
    public bool IsFootprintWalkableWorld(Vector3 worldPosition, float radius)
    {
        if (!EnsureWalkTilemap()) return false;

        Vector3Int centerCell = walkTilemap.WorldToCell(worldPosition);
        if (!walkTilemap.HasTile(centerCell))
        {
            LogFootprintFailure(worldPosition, centerCell, "center cell has no walk tile");
            return false;
        }
        if (IsWallAtWorld(worldPosition))
        {
            LogFootprintFailure(worldPosition, centerCell, "center cell is wall");
            return false;
        }

        float effective = Mathf.Max(0f, radius) * Mathf.Clamp(footprintInsetMultiplier, 0.1f, 1f);
        if (effective <= 0f)
            return true;

        if (CornerHitsWall(worldPosition, -effective, -effective, out Vector3Int cBL))
        {
            LogFootprintFailure(worldPosition, cBL, "corner BL hits wall");
            return false;
        }
        if (CornerHitsWall(worldPosition, +effective, -effective, out Vector3Int cBR))
        {
            LogFootprintFailure(worldPosition, cBR, "corner BR hits wall");
            return false;
        }
        if (CornerHitsWall(worldPosition, -effective, +effective, out Vector3Int cTL))
        {
            LogFootprintFailure(worldPosition, cTL, "corner TL hits wall");
            return false;
        }
        if (CornerHitsWall(worldPosition, +effective, +effective, out Vector3Int cTR))
        {
            LogFootprintFailure(worldPosition, cTR, "corner TR hits wall");
            return false;
        }

        return true;
    }

    public bool IsFootprintWalkableWorld(Vector2 worldPosition, float radius)
        => IsFootprintWalkableWorld((Vector3)worldPosition, radius);

    /// <summary>이 Area 안에서 from→to 사이 LOS가 wall로 막혀 있지 않은지 반환합니다.</summary>
    public bool HasLineOfSightWorld(Vector3 fromWorld, Vector3 toWorld)
    {
        if (!EnsureWalkTilemap()) return false;

        Vector3Int fromCell = walkTilemap.WorldToCell(fromWorld);
        Vector3Int toCell = walkTilemap.WorldToCell(toWorld);

        int dx = toCell.x - fromCell.x;
        int dy = toCell.y - fromCell.y;
        int steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        if (steps == 0)
            return IsWalkableCellSampledAsWorld(fromCell);

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            int x = Mathf.RoundToInt(fromCell.x + dx * t);
            int y = Mathf.RoundToInt(fromCell.y + dy * t);
            Vector3Int walkCell = new Vector3Int(x, y, fromCell.z);
            if (!IsWalkableCellSampledAsWorld(walkCell))
                return false;
        }
        return true;
    }

    public bool HasLineOfSightWorld(Vector2 fromWorld, Vector2 toWorld)
        => HasLineOfSightWorld((Vector3)fromWorld, (Vector3)toWorld);

    /// <summary>
    /// preferred 좌표에서 가장 가까운 walkable world 좌표를 spiral 검색해 반환합니다.
    /// 찾지 못하면 result는 preferred 그대로 두고 false 반환.
    /// </summary>
    public bool TryGetNearestWalkableWorldPosition(Vector3 preferred, out Vector3 result)
    {
        result = preferred;
        if (!EnsureWalkTilemap()) return false;

        Vector3Int center = walkTilemap.WorldToCell(preferred);
        const int MaxRadius = 8;
        for (int r = 0; r <= MaxRadius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (r > 0 && Mathf.Abs(dx) != r && Mathf.Abs(dy) != r)
                        continue;

                    Vector3Int cell = new Vector3Int(center.x + dx, center.y + dy, center.z);
                    if (!IsWalkableCellSampledAsWorld(cell))
                        continue;

                    result = walkTilemap.GetCellCenterWorld(cell);
                    return true;
                }
            }
        }
        return false;
    }

    public bool TryGetNearestWalkableWorldPosition(Vector2 preferred, out Vector2 result)
    {
        if (TryGetNearestWalkableWorldPosition((Vector3)preferred, out Vector3 v3))
        {
            result = v3;
            return true;
        }
        result = preferred;
        return false;
    }

    public Vector3Int WorldToCell(Vector3 worldPosition)
    {
        return walkTilemap != null
            ? walkTilemap.WorldToCell(worldPosition)
            : new Vector3Int(Mathf.FloorToInt(worldPosition.x), Mathf.FloorToInt(worldPosition.y), 0);
    }

    public Vector3 CellToWorldCenter(Vector3Int cell)
    {
        return walkTilemap != null
            ? walkTilemap.GetCellCenterWorld(cell)
            : new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
    }

    /// <summary>
    /// 셀 단위 walkable 검사. walk Tilemap의 cell index를 받아 cell 중심을 world로 변환한 뒤
    /// wallTilemap 측은 자신의 WorldToCell로 다시 변환해 검사하므로, 두 Tilemap의 transform이 달라도 안전합니다.
    /// </summary>
    public bool IsWalkableCell(Vector3Int walkCell)
    {
        return IsWalkableCellSampledAsWorld(walkCell);
    }

    private bool IsWalkableCellSampledAsWorld(Vector3Int walkCell)
    {
        if (walkTilemap == null || !walkTilemap.HasTile(walkCell)) return false;
        if (wallTilemap == null) return true;
        Vector3 cellWorld = walkTilemap.GetCellCenterWorld(walkCell);
        Vector3Int wallCell = wallTilemap.WorldToCell(cellWorld);
        return !wallTilemap.HasTile(wallCell);
    }

    private bool CornerHitsWall(Vector3 center, float dx, float dy, out Vector3Int wallCell)
    {
        Vector3 sampled = new Vector3(center.x + dx, center.y + dy, 0f);
        if (wallTilemap == null)
        {
            wallCell = walkTilemap != null ? walkTilemap.WorldToCell(sampled) : Vector3Int.zero;
            return false;
        }
        wallCell = wallTilemap.WorldToCell(sampled);
        return wallTilemap.HasTile(wallCell);
    }

    private bool EnsureWalkTilemap()
    {
        if (walkTilemap != null) return true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_warnedMissingWalkTilemap)
        {
            Debug.LogWarning($"[WalkabilityArea] '{id}' walkTilemap이 연결되지 않았습니다.", this);
            _warnedMissingWalkTilemap = true;
        }
#endif
        return false;
    }

    private void LogFootprintFailure(Vector3 world, Vector3Int cell, string reason)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!debugLogFootprintFailures) return;
        if (Time.unscaledTime - _lastDebugLogTime < 1f) return;
        _lastDebugLogTime = Time.unscaledTime;
        Debug.LogWarning(
            $"[WalkabilityArea] '{id}' footprint blocked at world={world} walkCell={cell} reason='{reason}'",
            this);
#endif
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (walkTilemap == null)
            Debug.LogWarning($"[WalkabilityArea] '{id}' walkTilemap이 비어 있습니다. Inspector에서 연결하세요.", this);
        if (string.IsNullOrWhiteSpace(id))
            Debug.LogWarning("[WalkabilityArea] id가 비어 있습니다. 디버깅 편의를 위해 이름을 지정하세요.", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawCellBoundsGizmo || walkTilemap == null) return;
        BoundsInt b = walkTilemap.cellBounds;
        if (b.size.x <= 0 || b.size.y <= 0) return;

        Vector3 min = walkTilemap.CellToWorld(b.min);
        Vector3 max = walkTilemap.CellToWorld(b.max);
        Vector3 center = (min + max) * 0.5f;
        Vector3 size = max - min;

        Color prev = Gizmos.color;
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.85f);
        Gizmos.DrawWireCube(center, size);
        Gizmos.color = prev;
    }
#endif
}
