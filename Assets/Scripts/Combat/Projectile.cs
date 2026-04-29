// ═══════════════════════════════════════════════════════════════════
//  Projectile.cs
//  책임: 직선 이동 발사체 — 벽·유닛 충돌 처리
//
//  스폰 방법:
//    Instantiate(projectilePrefab, spawnPos, Quaternion.identity)
//        .GetComponent<Projectile>()
//        .Init(direction, damage, skill.canPenetrateWalls, wallLayer, unitLayer, this);
//
//  필요한 Unity 설정:
//    - Rigidbody2D (Kinematic), Collider2D (Is Trigger = true)
//    - 발사체 GameObject의 Layer = "Projectile" (벽·유닛 레이어와 충돌 매트릭스 설정)
//    - Project Settings > Physics 2D > Layer Collision Matrix:
//        Projectile ↔ Wall  : 충돌 켬
//        Projectile ↔ Unit  : 충돌 켬
//
//  canPenetrateWalls 동작:
//    false — 벽 충돌 시 즉시 소멸 / true — 벽 무시, 유닛에만 반응
// ═══════════════════════════════════════════════════════════════════

using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("이동")]
    [Tooltip("월드 단위/초")]
    [SerializeField] private float speed = 8f;

    [Header("최대 비행 거리 (0 = 무제한)")]
    [SerializeField] private float maxRange = 0f;

    // ── 런타임 상태 (Init()으로 주입) ───────────────────────────────

    private int       _damage;
    private bool      _canPenetrateWalls;
    private int       _wallLayerMask;   // 1 << layerIndex 형태
    private int       _unitLayerMask;
    private object    _owner;           // 자기 자신 피해 방지용

    private Rigidbody2D _rb;
    private Vector3     _spawnPos;

    // ══════════════════════════════════════════════════════════════
    //  초기화
    // ══════════════════════════════════════════════════════════════

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.bodyType     = RigidbodyType2D.Kinematic;
        _rb.gravityScale = 0f;
    }

    /// <summary>
    /// 발사 방향·전투 데이터를 주입하고 이동을 시작합니다.
    /// 스폰 직후 반드시 호출하세요.
    /// </summary>
    /// <param name="direction">이동 방향 (자동 정규화)</param>
    /// <param name="damage">적중 시 피해량</param>
    /// <param name="canPenetrateWalls">벽 관통 여부</param>
    /// <param name="wallLayer">벽 레이어 마스크 (Inspector의 LayerMask 값)</param>
    /// <param name="unitLayer">유닛 레이어 마스크</param>
    /// <param name="owner">발사한 주체 (자기 자신 피해 방지)</param>
    public void Init(Vector2 direction, int damage, bool canPenetrateWalls,
                     LayerMask wallLayer, LayerMask unitLayer, object owner)
    {
        _damage            = damage;
        _canPenetrateWalls = canPenetrateWalls;
        _wallLayerMask     = (int)wallLayer;
        _unitLayerMask     = (int)unitLayer;
        _owner             = owner;
        _spawnPos          = transform.position;

        _rb.linearVelocity = direction.normalized * speed;
    }

    // ══════════════════════════════════════════════════════════════
    //  이동 및 최대 사거리 감시
    // ══════════════════════════════════════════════════════════════

    private void Update()
    {
        // 최대 사거리 초과 시 소멸
        if (maxRange > 0f && Vector3.Distance(transform.position, _spawnPos) >= maxRange)
            Destroy(gameObject);
    }

    // ══════════════════════════════════════════════════════════════
    //  충돌 처리 (Collider2D IsTrigger = true 필수)
    // ══════════════════════════════════════════════════════════════

    private void OnTriggerEnter2D(Collider2D other)
    {
        int hitLayer = 1 << other.gameObject.layer;

        // ── 벽 충돌 ──────────────────────────────────────────────────
        if ((hitLayer & _wallLayerMask) != 0)
        {
            // 벽 관통 불가 → 즉시 소멸
            if (!_canPenetrateWalls)
                Destroy(gameObject);

            // 벽 관통 가능 → 통과 (아무것도 하지 않음)
            return;
        }

        // ── 유닛(적/플레이어) 충돌 ──────────────────────────────────
        if ((hitLayer & _unitLayerMask) != 0)
        {
            // 오너 자신 제외, IDamageable 구현체에만 피해
            if (other.TryGetComponent<IDamageable>(out var target) &&
                !ReferenceEquals(target, _owner))
            {
                target.TakeDamage(_damage);
            }

            // 유닛 명중 시 항상 소멸 (벽 관통 여부와 무관)
            Destroy(gameObject);
        }
    }
}
