using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class EliteArenaPortal : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private Sprite portalSprite;
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 20;
    [SerializeField] private Color portalColor = new Color(0.25f, 0.9f, 1f, 1f);
    [SerializeField] private bool hideVisualWhenCompleted = true;

    private EliteArenaEncounterController _controller;
    private RoomInfo _room;
    private Collider2D _collider;
    private SpriteRenderer _spriteRenderer;
    private bool _bound;
    private bool _locked;
    private bool _completed;
    private int _completedRoomKey;
    private bool _warnedMissingVisual;
    private bool _warnedFogVisibility;

    private void Awake()
    {
        _collider = GetComponent<Collider2D>();
        _collider.isTrigger = true;
        EnsureVisual();
    }

    public void Bind(EliteArenaEncounterController controller, RoomInfo room)
    {
        _controller = controller;
        _room = room;
        _bound = true;
        _locked = false;

        if (_collider == null)
            _collider = GetComponent<Collider2D>();

        if (_collider != null)
            _collider.enabled = !IsCompletedForRoom(room);

        EnsureVisual();
        SetVisualVisible(!IsCompletedForRoom(room) || !hideVisualWhenCompleted);
    }

    public void SetLocked(bool locked)
    {
        _locked = locked;
    }

    public bool IsCompletedForRoom(RoomInfo room)
    {
        return _completed && _completedRoomKey == room.StableRoomKey;
    }

    public void MarkCompletedAndDisable(RoomInfo room)
    {
        _completed = true;
        _completedRoomKey = room.StableRoomKey;
        _locked = true;

        if (_collider == null)
            _collider = GetComponent<Collider2D>();

        if (_collider != null)
            _collider.enabled = false;

        if (hideVisualWhenCompleted)
            SetVisualVisible(false);
    }

    public void ResetRuntimeState()
    {
        _bound = false;
        _locked = false;
        _completed = false;
        _completedRoomKey = 0;
        _controller = null;

        if (_collider == null)
            _collider = GetComponent<Collider2D>();

        if (_collider != null)
            _collider.enabled = false;

        SetVisualVisible(false);
    }

    public void EnsureVisual()
    {
        if (_spriteRenderer == null)
            _spriteRenderer = GetComponent<SpriteRenderer>();

        if (_spriteRenderer == null)
            return;

        if (_spriteRenderer.sprite == null && portalSprite != null)
            _spriteRenderer.sprite = portalSprite;

        _spriteRenderer.color = portalColor;
        _spriteRenderer.sortingLayerName = sortingLayerName;
        _spriteRenderer.sortingOrder = sortingOrder;
        DisableFogVisibilityRendererIfPresent();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_warnedMissingVisual && _spriteRenderer.sprite == null)
        {
            _warnedMissingVisual = true;
            Debug.LogWarning("[EliteArenaPortal] SpriteRenderer.sprite is missing. Assign Elite_portal sprite on the prefab.", this);
        }
#endif
    }

    private void SetVisualVisible(bool visible)
    {
        EnsureVisual();
        if (_spriteRenderer != null)
            _spriteRenderer.enabled = visible;
    }

    private void DisableFogVisibilityRendererIfPresent()
    {
        if (!TryGetComponent(out FogVisibilityRenderer fogVisibility))
            return;

        if (!fogVisibility.enabled)
            return;

        fogVisibility.enabled = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_warnedFogVisibility)
        {
            _warnedFogVisibility = true;
            Debug.LogWarning("[EliteArenaPortal] FogVisibilityRenderer was disabled so portal remains visible in the current room.", this);
        }
#endif
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!_bound || _locked || IsCompletedForRoom(_room))
            return;

        if (!other.TryGetComponent(out PlayerController player))
            return;

        DungeonManager dungeonManager = DungeonManager.Instance;
        RoomSpawner roomSpawner = RoomSpawner.Active;
        _controller?.BeginEncounter(this, _room, player, dungeonManager, roomSpawner);
    }
}
