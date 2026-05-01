// ═══════════════════════════════════════════════════════════════════
//  SkillRangePreviewer.cs
//  책임: Q/W/E/R 키를 누르는 동안 스킬 범위를 LineRenderer 로 시각화
//
//  형상 매핑 (모든 패턴이 patternRange 사용):
//    Circle   → 원  (반경: range*√2+0.5 타일, 코너 타일까지 포함)
//    Cone     → 부채꼴 (반경: range*√2+0.5 타일, coneHalfAngle 각도)
//    Line     → 직사각형 (range칸 길이, 1칸 너비)
//    Single   → range칸 거리의 1×1 정사각형
//    Cross    → 십자 12점 다각형 (팔 길이 = range칸)
//    Diagonal → 대각 16점 다각형 (range칸, 십자 영역 미포함)
//
//  재계산 조건 (성능 최적화):
//    - 슬롯 변경 시        → 즉시 재계산
//    - 방향 의존 패턴이고 FacingDirection 이 바뀔 때만 재계산
//    - 매 프레임 재계산 안 함
//
//  벽 인식:
//    enableWallAwareness = true 이면 각 꼭짓점을 공격자 → 해당 점 방향으로
//    DungeonData 그리드를 따라 샘플링해 벽 경계에서 클리핑합니다.
//    wallLayer LayerMask 가 설정된 경우 Physics2D.Raycast 를 우선 사용합니다.
// ═══════════════════════════════════════════════════════════════════

using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class SkillRangePreviewer : MonoBehaviour
{
    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("의존성")]
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private PlayerController       movement;
    [SerializeField] private DungeonManager         dungeonManager;
    [SerializeField] private PlayerInputReader      inputReader;

    [Header("곡선 품질")]
    [Tooltip("원·부채꼴 호의 분절 수. 50이 기본값 (충분히 매끄럽고 저비용).")]
    [Range(8, 128)]
    [SerializeField] private int circleSegments = 50;

    [Header("LineRenderer 시각")]
    [SerializeField] private float lineWidth    = 0.06f;
    [SerializeField] private Color previewColor = new Color(1f, 0.9f, 0.1f, 0.75f);

    [Header("타일 크기 (월드 단위)")]
    [Tooltip("Unity Tilemap 기본값 1. Tilemap의 Cell Size 와 일치시키세요.")]
    [SerializeField] private float tileSize = 1f;

    [Header("벽 인식")]
    [Tooltip("true면 꼭짓점을 벽 경계에서 잘라냅니다.")]
    [SerializeField] private bool      enableWallAwareness = true;
    [Tooltip("Physics2D 벽 레이어 (선택). 0이면 DungeonData 그리드 방식 사용.")]
    [SerializeField] private LayerMask wallLayer;

    // ── 정적 꼭짓점 버퍼 (GC 방지, 최대 256점) ─────────────────────
    private static readonly Vector3[] s_Buf = new Vector3[256];

    // ── 런타임 상태 ─────────────────────────────────────────────────
    private LineRenderer _lr;
    private int          _activeSlot           = -1;   // -1 = 스킬 미표시
    private SkillData    _currentSkill;
    private bool         _isBasicAttackPreview  = false;
    private Vector2Int   _lastFacing;

    // ══════════════════════════════════════════════════════════════
    //  초기화
    // ══════════════════════════════════════════════════════════════

    private void Awake()
    {
        _lr = GetComponent<LineRenderer>();
        SetupLineRenderer();
        _lr.enabled = false;

        if (combat == null)
            Debug.LogWarning("[SkillRangePreviewer] PlayerCombatController가 연결되지 않아 스킬 미리보기를 표시할 수 없습니다.");
        if (movement == null)
            Debug.LogWarning("[SkillRangePreviewer] PlayerController가 연결되지 않아 방향성 미리보기가 기본 방향을 사용합니다.");
        if (dungeonManager == null)
            Debug.LogWarning("[SkillRangePreviewer] DungeonManager가 연결되지 않아 벽 인식 미리보기가 비활성화됩니다.");

        if (inputReader == null && movement != null)
            inputReader = movement.GetComponent<PlayerInputReader>();
        if (inputReader == null)
            inputReader = FindAnyObjectByType<PlayerInputReader>();
        if (inputReader == null)
            Debug.LogWarning("[SkillRangePreviewer] PlayerInputReader를 찾을 수 없습니다.");
    }

    private void SetupLineRenderer()
    {
        _lr.useWorldSpace = false;  // 플레이어 로컬 좌표 (플레이어를 따라 자동 이동)
        _lr.loop          = true;   // 닫힌 다각형 — 마지막 꼭짓점 → 첫 꼭짓점 자동 연결
        _lr.startWidth    = lineWidth;
        _lr.endWidth      = lineWidth;
        _lr.startColor    = previewColor;
        _lr.endColor      = previewColor;

        // 반투명 Unlit 머티리얼 (런타임 생성)
        var shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            var mat   = new Material(shader);
            mat.color = previewColor;
            _lr.sharedMaterial = mat;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  매 프레임 처리
    // ══════════════════════════════════════════════════════════════

    private void Update()
    {
        HandleInput();

        if (_activeSlot >= 0 && _currentSkill != null)
        {
            // 방향 의존 패턴(Line·Cone·Single)은 FacingDirection 이 바뀔 때만 재계산
            if (IsDirectional(_currentSkill.attackPattern))
            {
                Vector2Int facing = movement != null ? movement.FacingDirection : Vector2Int.down;
                if (facing != _lastFacing)
                {
                    _lastFacing = facing;
                    BuildPreview(_currentSkill);
                }
            }
        }
        else if (_isBasicAttackPreview)
        {
            var weapon = combat?.currentWeapon;
            if (weapon != null && IsDirectional(weapon.attackPattern))
            {
                Vector2Int facing = movement != null ? movement.FacingDirection : Vector2Int.down;
                if (facing != _lastFacing)
                {
                    _lastFacing = facing;
                    BuildBasicAttackPreview(weapon);
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  입력 처리
    // ══════════════════════════════════════════════════════════════

    private void HandleInput()
    {
        if (inputReader == null) return;

        // 스킬 키 최초 입력 → 스킬 미리보기 시작 (기본 공격 미리보기보다 우선)
        if      (inputReader.WasSkillPressed(0)) { HideBasicAttackPreview(); TryShowPreview(0); }
        else if (inputReader.WasSkillPressed(1)) { HideBasicAttackPreview(); TryShowPreview(1); }
        else if (inputReader.WasSkillPressed(2)) { HideBasicAttackPreview(); TryShowPreview(2); }
        else if (inputReader.WasSkillPressed(3)) { HideBasicAttackPreview(); TryShowPreview(3); }

        // 현재 표시 중인 스킬 키가 릴리즈되면 → 스킬 미리보기 숨김
        if (_activeSlot >= 0 && !inputReader.IsSkillHeld(_activeSlot))
            HidePreview();

        // 기본 공격 미리보기: 스킬 미리보기가 없을 때만 Space hold 감지
        if (_activeSlot < 0)
        {
            if (inputReader.IsBasicAttackHeld)
            {
                if (!_isBasicAttackPreview) TryShowBasicAttackPreview();
            }
            else if (_isBasicAttackPreview)
            {
                HideBasicAttackPreview();
            }
        }
        else if (_isBasicAttackPreview)
        {
            HideBasicAttackPreview();
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  미리보기 ON / OFF
    // ══════════════════════════════════════════════════════════════

    private void TryShowPreview(int slot)
    {
        if (combat == null || combat.currentWeapon == null) return;

        SkillData[] skills = combat.currentWeapon.skills;
        if (skills == null) return;
        if ((uint)slot >= (uint)skills.Length || skills[slot] == null) return;

        _activeSlot   = slot;
        _currentSkill = skills[slot];
        _lastFacing   = movement != null ? movement.FacingDirection : Vector2Int.down;

        BuildPreview(_currentSkill);
        _lr.enabled = true;
    }

    private void HidePreview()
    {
        _activeSlot   = -1;
        _currentSkill = null;
        _lr.enabled   = false;
    }

    private void TryShowBasicAttackPreview()
    {
        var weapon = combat?.currentWeapon;
        if (weapon == null) return;

        _isBasicAttackPreview = true;
        _lastFacing = movement != null ? movement.FacingDirection : Vector2Int.down;
        BuildBasicAttackPreview(weapon);
        _lr.enabled = true;
    }

    private void HideBasicAttackPreview()
    {
        _isBasicAttackPreview = false;
        _lr.enabled = false;
    }

    // ══════════════════════════════════════════════════════════════
    //  형상 생성 — 패턴별 분기
    // ══════════════════════════════════════════════════════════════

    private void BuildPreview(SkillData skill)
    {
        Vector2Int facing = movement != null ? movement.FacingDirection : Vector2Int.down;

        switch (skill.attackPattern)
        {
            case AttackPatternType.Circle:
                // 체비쇼프 range 이내 — 코너 타일(±range,±range)까지 반경
                BuildCircle((skill.patternRange * Mathf.Sqrt(2f) + 0.5f) * tileSize);
                break;

            case AttackPatternType.Cone:
                // 정면+좌우45° 각 range칸 — 대각 방향이 가장 멀리 뻗음
                BuildCone(facing, (skill.patternRange * Mathf.Sqrt(2f) + 0.5f) * tileSize, skill.coneHalfAngle);
                break;

            case AttackPatternType.Line:
                // 정면 직선 range칸 직사각형
                BuildRectangle(facing, tileSize * 0.5f, skill.patternRange * tileSize, tileSize);
                break;

            case AttackPatternType.Single:
                // range칸 거리의 1×1 정사각형
                BuildRectangle(facing, (skill.patternRange - 0.5f) * tileSize, tileSize, tileSize);
                break;

            case AttackPatternType.Cross:
                // 상하좌우 각 range칸 십자 (12점)
                BuildCross(skill.patternRange);
                break;

            case AttackPatternType.Diagonal:
                // 대각 4방향 각 range칸 (16점, range>1은 직사각형 근사)
                BuildDiagonal(skill.patternRange);
                break;
        }
    }

    private void BuildBasicAttackPreview(WeaponData weapon)
    {
        Vector2Int facing = movement != null ? movement.FacingDirection : Vector2Int.down;

        switch (weapon.attackPattern)
        {
            case AttackPatternType.Circle:
                BuildCircle((weapon.patternRange * Mathf.Sqrt(2f) + 0.5f) * tileSize);
                break;
            case AttackPatternType.Cone:
                BuildCone(facing, (weapon.patternRange * Mathf.Sqrt(2f) + 0.5f) * tileSize, 45f);
                break;
            case AttackPatternType.Line:
                BuildRectangle(facing, tileSize * 0.5f, weapon.patternRange * tileSize, tileSize);
                break;
            case AttackPatternType.Single:
                BuildRectangle(facing, (weapon.patternRange - 0.5f) * tileSize, tileSize, tileSize);
                break;
            case AttackPatternType.Cross:
                BuildCross(weapon.patternRange);
                break;
            case AttackPatternType.Diagonal:
                BuildDiagonal(weapon.patternRange);
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  형상별 꼭짓점 계산
    // ══════════════════════════════════════════════════════════════

    // ── 원 ────────────────────────────────────────────────────────
    private void BuildCircle(float radius)
    {
        int n = circleSegments;
        for (int i = 0; i < n; i++)
        {
            float angle = i * Mathf.PI * 2f / n;
            var   pt    = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            s_Buf[i] = ClipToWall(Vector3.zero, pt);
        }
        Apply(n);
    }

    // ── 부채꼴 (Cone) ─────────────────────────────────────────────
    // 구조: [중심, 호점0, 호점1, ..., 호점N-1]
    // loop = true 로 마지막 호점 → 중심이 자동 연결되어 부채꼴 완성
    private void BuildCone(Vector2Int facing, float radius, float halfAngleDeg)
    {
        float baseAngle = Mathf.Atan2(facing.y, facing.x);
        float halfRad   = halfAngleDeg * Mathf.Deg2Rad;
        // 호 분절 수: 전체 원 대비 차지하는 각도 비율로 결정
        int arcN    = Mathf.Max(3, Mathf.RoundToInt(circleSegments * (halfAngleDeg * 2f) / 360f));
        int totalN  = arcN + 1;  // 중심 1점 + 호점 N개

        s_Buf[0] = Vector3.zero;  // 인덱스 0 = 중심 (플레이어 위치)

        for (int i = 0; i < arcN; i++)
        {
            float t     = (float)i / (arcN - 1);
            float angle = baseAngle - halfRad + halfRad * 2f * t;
            var   pt    = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            s_Buf[i + 1] = ClipToWall(Vector3.zero, pt);
        }
        Apply(totalN);
    }

    // ── 직사각형 (Line·Single 패턴) ─────────────────────────────
    // startOffset: 플레이어 중심에서 직사각형 시작까지 거리
    // length: 직사각형 길이 (startOffset 기준)
    private void BuildRectangle(Vector2Int facing, float startOffset, float length, float width)
    {
        var   fwd   = new Vector2(facing.x, facing.y);
        var   right = new Vector2(-fwd.y, fwd.x);
        float hw    = width * 0.5f;

        s_Buf[0] = fwd * startOffset             - right * hw;
        s_Buf[1] = fwd * (startOffset + length)  - right * hw;
        s_Buf[2] = fwd * (startOffset + length)  + right * hw;
        s_Buf[3] = fwd * startOffset             + right * hw;
        Apply(4);
    }

    // ── 십자 (Cross 패턴) — 12점 다각형 ────────────────────────
    // 상·하·좌·우 각 range칸 팔이 연결된 십자 윤곽
    private void BuildCross(int range)
    {
        float h = tileSize * 0.5f;
        float e = range * tileSize + h;   // 팔 끝 = (range + 0.5) * tileSize
        // 시계 방향으로 12점 배치
        s_Buf[0]  = new Vector3(-h,  e);   // 위쪽 팔 왼상단
        s_Buf[1]  = new Vector3( h,  e);   // 위쪽 팔 오른상단
        s_Buf[2]  = new Vector3( h,  h);   // 내부 오른쪽 상단 모서리
        s_Buf[3]  = new Vector3( e,  h);   // 오른쪽 팔 오른상단
        s_Buf[4]  = new Vector3( e, -h);   // 오른쪽 팔 오른하단
        s_Buf[5]  = new Vector3( h, -h);   // 내부 오른쪽 하단 모서리
        s_Buf[6]  = new Vector3( h, -e);   // 아래쪽 팔 오른하단
        s_Buf[7]  = new Vector3(-h, -e);   // 아래쪽 팔 왼하단
        s_Buf[8]  = new Vector3(-h, -h);   // 내부 왼쪽 하단 모서리
        s_Buf[9]  = new Vector3(-e, -h);   // 왼쪽 팔 왼하단
        s_Buf[10] = new Vector3(-e,  h);   // 왼쪽 팔 왼상단
        s_Buf[11] = new Vector3(-h,  h);   // 내부 왼쪽 상단 모서리
        Apply(12);
    }

    // ── 대각 (Diagonal 패턴) — 16점 다각형 ──────────────────────
    // 우상 → 우하 → 좌하 → 좌상 순서로 각 방향 range칸을 직사각형으로 표현
    // range=1이면 타일 단위 정확, range>1이면 직사각형 근사 (십자 영역 미포함)
    private void BuildDiagonal(int range)
    {
        float h = tileSize * 0.5f;
        float e = range * tileSize + h;   // 외곽 = (range + 0.5) * tileSize

        // 우상 방향 (3면: 내→위→오른)
        s_Buf[0]  = new Vector3( h,  h);
        s_Buf[1]  = new Vector3( h,  e);
        s_Buf[2]  = new Vector3( e,  e);
        s_Buf[3]  = new Vector3( e,  h);
        // 우하 방향 (오른 외곽 경유 → 3면: 오른→아래→내)
        s_Buf[4]  = new Vector3( e, -h);
        s_Buf[5]  = new Vector3( e, -e);
        s_Buf[6]  = new Vector3( h, -e);
        s_Buf[7]  = new Vector3( h, -h);
        // 좌하 방향 (내부 경유 → 3면: 내→아래→왼)
        s_Buf[8]  = new Vector3(-h, -h);
        s_Buf[9]  = new Vector3(-h, -e);
        s_Buf[10] = new Vector3(-e, -e);
        s_Buf[11] = new Vector3(-e, -h);
        // 좌상 방향 (왼 외곽 경유 → 3면: 왼→위→내)
        s_Buf[12] = new Vector3(-e,  h);
        s_Buf[13] = new Vector3(-e,  e);
        s_Buf[14] = new Vector3(-h,  e);
        s_Buf[15] = new Vector3(-h,  h);
        // loop = true → [15] → [0] 자동 연결
        Apply(16);
    }

    // ══════════════════════════════════════════════════════════════
    //  벽 클리핑 — 꼭짓점이 벽을 뚫지 않도록 경계에서 자름
    // ══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// 로컬 좌표 fromLocal → toLocal 방향으로 벽이 있으면
    /// 벽 직전 위치를 로컬 좌표로 반환합니다.
    /// </summary>
    private Vector3 ClipToWall(Vector3 fromLocal, Vector3 toLocal)
    {
        if (!enableWallAwareness) return toLocal;

        // 로컬 → 월드 변환
        Vector3 fromWorld = transform.TransformPoint(fromLocal);
        Vector3 toWorld   = transform.TransformPoint(toLocal);
        float   dist      = Vector3.Distance(fromWorld, toWorld);
        if (dist < 0.01f) return toLocal;

        Vector3 dir = (toWorld - fromWorld) / dist;

        // ① Physics2D Raycast (wallLayer 가 설정된 경우 우선 사용)
        if (wallLayer.value != 0)
        {
            RaycastHit2D hit = Physics2D.Raycast(fromWorld, dir, dist, wallLayer);
            if (hit.collider != null)
                return transform.InverseTransformPoint(hit.point);
            return toLocal;
        }

        // ② DungeonData 그리드 샘플링 (wallLayer 미설정 시 폴백)
        if (dungeonManager == null || dungeonManager.Data == null) return toLocal;

        float   step     = tileSize * 0.5f;   // 0.5칸 간격으로 샘플링
        Vector3 lastSafe = fromWorld;

        for (float d = step; d < dist + step; d += step)
        {
            Vector3    cur  = fromWorld + dir * Mathf.Min(d, dist);
            Vector2Int grid = dungeonManager.WorldToGrid(cur);

            if (!dungeonManager.Data.IsWalkable(grid.x, grid.y))
                return transform.InverseTransformPoint(lastSafe);  // 벽 직전 반환

            lastSafe = cur;
        }
        return toLocal;
    }

    // ══════════════════════════════════════════════════════════════
    //  헬퍼
    // ══════════════════════════════════════════════════════════════

    // positionCount 설정 후 s_Buf 를 LineRenderer 에 일괄 적용
    private void Apply(int count)
    {
        _lr.positionCount = count;
        _lr.SetPositions(s_Buf);
    }

    // 방향이 바뀔 때 재계산이 필요한 패턴 여부
    private static bool IsDirectional(AttackPatternType p) =>
        p == AttackPatternType.Line   ||
        p == AttackPatternType.Cone   ||
        p == AttackPatternType.Single;
}
