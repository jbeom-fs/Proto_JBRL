using UnityEngine;

public class ProjectileController : MonoBehaviour
{
    [SerializeField] private float hitRadius = 0.08f;

    private Vector2 _direction = Vector2.right;
    private float _speed = 6f;
    private int _damage = 1;
    private float _lifetime = 3f;
    private ProjectileWallHitMode _wallHitMode = ProjectileWallHitMode.Destroy;
    private Collider2D _collider;

    private void Awake()
    {
        _collider = GetComponent<Collider2D>();
        if (_collider != null)
        {
            _collider.isTrigger = true;
            hitRadius = Mathf.Max(hitRadius, Mathf.Max(_collider.bounds.extents.x, _collider.bounds.extents.y));
        }
    }

    public void Initialize(
        Vector2 direction,
        int damage,
        float speed,
        float lifetime,
        ProjectileWallHitMode wallHitMode,
        Object owner)
    {
        _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        _damage = Mathf.Max(0, damage);
        _speed = Mathf.Max(0f, speed);
        _lifetime = Mathf.Max(0.01f, lifetime);
        _wallHitMode = wallHitMode;
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        transform.position += (Vector3)(_direction * (_speed * deltaTime));

        _lifetime -= deltaTime;
        if (_lifetime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        if (HitsWall())
        {
            if (!HandleWallHit())
                return;
        }

        TryHitPlayer();
    }

    private bool HitsWall()
    {
        var dungeon = DungeonManager.Instance;
        if (dungeon == null || dungeon.Data == null)
            return false;

        Vector2Int grid = dungeon.WorldToGrid(transform.position);
        return !dungeon.IsWalkable(grid.x, grid.y);
    }

    private bool HandleWallHit()
    {
        switch (_wallHitMode)
        {
            case ProjectileWallHitMode.Destroy:
                Destroy(gameObject);
                return false;

            case ProjectileWallHitMode.PassThrough:
                return true;

            case ProjectileWallHitMode.Bounce:
                // TODO: Reflect the projectile direction against wall normals when wall normals are available.
                Destroy(gameObject);
                return false;
        }

        return true;
    }

    private void TryHitPlayer()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, hitRadius);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null) continue;

            PlayerCombatController player = hit.GetComponent<PlayerCombatController>();
            if (player == null)
                player = hit.GetComponentInParent<PlayerCombatController>();
            if (player == null)
                player = hit.GetComponentInChildren<PlayerCombatController>();
            if (player == null || !player.IsAlive) continue;

            player.TakeDamage(_damage);
            Destroy(gameObject);
            return;
        }
    }
}
