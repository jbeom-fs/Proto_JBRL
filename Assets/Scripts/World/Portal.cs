using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public abstract class Portal : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] protected Sprite portalSprite;
    [SerializeField] protected string sortingLayerName = "FloorFX";
    [SerializeField] protected int sortingOrder = 0;
    [SerializeField] protected Color portalColor = Color.white;

    private Collider2D _collider;
    private SpriteRenderer _spriteRenderer;
    private bool _isProceeding;
    private bool _warnedMissingVisual;
    private bool _warnedFogVisibility;

    public bool IsLocked { get; private set; }

    protected virtual string MissingVisualWarningMessage =>
        "SpriteRenderer.sprite is missing. Assign a portal sprite on the prefab/scene object.";

    protected virtual string FogVisibilityWarningMessage =>
        "FogVisibilityRenderer was disabled so portal remains visible.";

    protected virtual void Awake()
    {
        _collider = GetComponent<Collider2D>();
        if (_collider != null)
            _collider.isTrigger = true;

        EnsureVisual();
    }

    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        if (!locked)
            _isProceeding = false;
    }

    public void SetColliderEnabled(bool enabledState)
    {
        SetColliderOnly(enabledState);
        SetVisualVisible(enabledState);
    }

    protected void SetColliderOnly(bool enabledState)
    {
        if (_collider == null)
            _collider = GetComponent<Collider2D>();

        if (_collider != null)
            _collider.enabled = enabledState;
    }

    protected void SetVisualVisible(bool visible)
    {
        EnsureVisual();
        if (_spriteRenderer != null)
            _spriteRenderer.enabled = visible;
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
            Debug.LogWarning(
                "[" + GetType().Name + "] " + MissingVisualWarningMessage,
                this);
        }
#endif
    }

    public virtual void ResetRuntimeState()
    {
        SetLocked(false);
        SetColliderEnabled(false);
    }

    protected virtual bool CanTrigger()
    {
        return true;
    }

    protected abstract bool OnPlayerEntered(PlayerController player);

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsLocked || _isProceeding)
            return;

        if (!CanTrigger())
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        _isProceeding = true;
        if (OnPlayerEntered(player))
        {
            SetLocked(true);
            return;
        }

        _isProceeding = false;
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
            Debug.LogWarning(
                "[" + GetType().Name + "] " + FogVisibilityWarningMessage,
                this);
        }
#endif
    }
}
