using System;
using UnityEngine;

public class ProjectileController : MonoBehaviour
{
    [SerializeField] private float hitRadius = 0.08f;
    [SerializeField] private float bounceExitOffset = 0.05f;

    private Vector2 _direction = Vector2.right;
    private float _speed = 6f;
    private int _damage = 1;
    private float _lifetime = 3f;
    private ProjectileWallHitMode _wallHitMode = ProjectileWallHitMode.Destroy;
    private int _maxBounceCount;
    private int _currentBounceCount;
    private Collider2D _collider;
    private Action<ProjectileController> _releaseAction;
    private bool _released;

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
        int maxBounceCount,
        UnityEngine.Object owner)
    {
        _released = false;
        _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        _damage = Mathf.Max(0, damage);
        _speed = Mathf.Max(0f, speed);
        _lifetime = Mathf.Max(0.01f, lifetime);
        _wallHitMode = wallHitMode;
        _maxBounceCount = Mathf.Max(0, maxBounceCount);
        _currentBounceCount = 0;
    }

    public void SetReleaseAction(Action<ProjectileController> releaseAction)
    {
        _releaseAction = releaseAction;
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        _lifetime -= deltaTime;
        if (_lifetime <= 0f)
        {
            Release();
            return;
        }

        Vector2 currentPosition = transform.position;
        Vector2 nextPosition = currentPosition + _direction * (_speed * deltaTime);
        if (IsWallPosition(nextPosition))
        {
            if (!HandleWallHit(currentPosition, nextPosition))
                return;
        }
        else
        {
            transform.position = nextPosition;
        }

        TryHitPlayer();
    }

    private bool IsWallPosition(Vector2 position)
    {
        var dungeon = DungeonManager.Instance;
        if (dungeon == null || dungeon.Data == null)
            return false;

        Vector2Int grid = dungeon.WorldToGrid(position);
        return !dungeon.IsWalkable(grid.x, grid.y);
    }

    private bool HandleWallHit(Vector2 currentPosition, Vector2 nextPosition)
    {
        switch (_wallHitMode)
        {
            case ProjectileWallHitMode.Destroy:
                Release();
                return false;

            case ProjectileWallHitMode.PassThrough:
                transform.position = nextPosition;
                return true;

            case ProjectileWallHitMode.Bounce:
                return TryBounce(currentPosition, nextPosition);
        }

        return true;
    }

    private bool TryBounce(Vector2 currentPosition, Vector2 nextPosition)
    {
        if (_currentBounceCount >= _maxBounceCount)
        {
            Release();
            return false;
        }

        Vector2 xOnlyPosition = new Vector2(nextPosition.x, currentPosition.y);
        Vector2 yOnlyPosition = new Vector2(currentPosition.x, nextPosition.y);
        bool blockedX = IsWallPosition(xOnlyPosition);
        bool blockedY = IsWallPosition(yOnlyPosition);

        if (blockedX)
            _direction.x *= -1f;
        if (blockedY)
            _direction.y *= -1f;
        if (!blockedX && !blockedY)
            _direction = -_direction;

        if (_direction.sqrMagnitude <= 0.0001f)
            _direction = Vector2.right;
        else
            _direction.Normalize();

        _currentBounceCount++;

        Vector2 correctedPosition = currentPosition + _direction * Mathf.Max(0.01f, bounceExitOffset);
        transform.position = IsWallPosition(correctedPosition)
            ? currentPosition
            : correctedPosition;

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
            Release();
            return;
        }
    }

    private void Release()
    {
        if (_released)
            return;

        _released = true;
        if (_releaseAction != null)
        {
            _releaseAction(this);
            return;
        }

        Destroy(gameObject);
    }
}
