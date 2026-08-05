using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FreischutzFocusVisualPool : MonoBehaviour
{
    private const string RuntimePoolName = "FreischutzFocusVisualPool";

    [SerializeField] private GameObject badgePrefab;
    [SerializeField] private Sprite[] stackSprites;
    [SerializeField, Min(0f)] private float badgeGap = 0.5f;
    [SerializeField] private string badgeSortingLayerName = "Actor";
    [SerializeField] private int badgeSortingOrder = 51;

    private readonly Dictionary<EnemyController, BadgeState> _activeBadges = new();
    private readonly List<BadgeState> _badgePool = new();
    private readonly List<EnemyController> _scratchInvalidEnemies = new();

    public static FreischutzFocusVisualPool Active { get; private set; }

    private sealed class BadgeState
    {
        public GameObject Instance;
        public SpriteRenderer Renderer;
        public EnemyHealthBar HealthBar;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Active != null)
            return;

        GameObject poolObject = new GameObject(RuntimePoolName, typeof(FreischutzFocusVisualPool));
        DontDestroyOnLoad(poolObject);
    }

    private void Awake()
    {
        if (Active != null && Active != this)
        {
            enabled = false;
            return;
        }

        Active = this;
    }

    private void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }

    private void LateUpdate()
    {
        if (_activeBadges.Count == 0)
            return;

        _scratchInvalidEnemies.Clear();
        foreach (KeyValuePair<EnemyController, BadgeState> pair in _activeBadges)
        {
            EnemyController enemy = pair.Key;
            BadgeState badge = pair.Value;
            if (enemy == null || !enemy.IsAlive ||
                badge == null || badge.Instance == null)
            {
                _scratchInvalidEnemies.Add(enemy);
                continue;
            }

            badge.Instance.transform.position = ResolveBadgePosition(enemy, badge);
        }

        for (int i = 0; i < _scratchInvalidEnemies.Count; i++)
            Hide(_scratchInvalidEnemies[i]);
        _scratchInvalidEnemies.Clear();
    }

    public void Show(EnemyController enemy, int stackCount)
    {
        if (enemy == null || !enemy.IsAlive || stackCount <= 0)
        {
            Hide(enemy);
            return;
        }

        Sprite sprite = ResolveStackSprite(stackCount);
        if (sprite == null)
        {
            Hide(enemy);
            return;
        }

        if (!_activeBadges.TryGetValue(enemy, out BadgeState badge) ||
            badge == null || badge.Instance == null || badge.Renderer == null)
        {
            _activeBadges.Remove(enemy);
            badge = GetBadge();
            if (badge == null)
                return;

            _activeBadges[enemy] = badge;
            EnsureScratchCapacity();
        }

        badge.HealthBar = enemy.GetComponent<EnemyHealthBar>();
        badge.Renderer.sprite = sprite;
        badge.Instance.transform.position = ResolveBadgePosition(enemy, badge);
        badge.Instance.SetActive(true);
    }

    public void Hide(EnemyController enemy)
    {
        if (ReferenceEquals(enemy, null))
            return;

        if (!_activeBadges.TryGetValue(enemy, out BadgeState badge))
            return;

        _activeBadges.Remove(enemy);
        if (badge == null)
            return;

        badge.HealthBar = null;
        if (badge.Renderer != null)
            badge.Renderer.sprite = null;
        if (badge.Instance != null)
            badge.Instance.SetActive(false);
    }

    private Sprite ResolveStackSprite(int stackCount)
    {
        if (stackSprites == null || stackSprites.Length == 0)
            return null;

        int index = Mathf.Clamp(stackCount - 1, 0, stackSprites.Length - 1);
        return stackSprites[index];
    }

    private Vector3 ResolveBadgePosition(EnemyController enemy, BadgeState badge)
    {
        if (badge.HealthBar == null)
            return enemy.MarkerAnchorWorld + Vector3.up * badgeGap;

        float scaleY = enemy.transform.lossyScale.y;
        float invScaleY = Mathf.Abs(scaleY) > 0.0001f ? 1f / scaleY : 1f;
        float localY = badge.HealthBar.TopAnchorY + badgeGap * invScaleY;
        return enemy.transform.TransformPoint(new Vector3(0f, localY, 0f));
    }

    private BadgeState GetBadge()
    {
        for (int i = 0; i < _badgePool.Count; i++)
        {
            BadgeState pooled = _badgePool[i];
            if (pooled != null && pooled.Instance != null && !pooled.Instance.activeSelf)
                return pooled;
        }

        if (badgePrefab == null)
            return null;

        GameObject created = Instantiate(badgePrefab, transform);
        SpriteRenderer spriteRenderer = created.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            created.SetActive(false);
            Destroy(created);
            return null;
        }

        spriteRenderer.sprite = null;
        spriteRenderer.sortingLayerName = badgeSortingLayerName;
        spriteRenderer.sortingOrder = badgeSortingOrder;
        created.SetActive(false);

        BadgeState badge = new BadgeState
        {
            Instance = created,
            Renderer = spriteRenderer
        };
        _badgePool.Add(badge);
        return badge;
    }

    private void EnsureScratchCapacity()
    {
        if (_scratchInvalidEnemies.Capacity < _activeBadges.Count)
            _scratchInvalidEnemies.Capacity = _activeBadges.Count;
    }
}
