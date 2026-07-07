using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class BossEntryPortal : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private Sprite portalSprite;
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 20;
    [SerializeField] private Color portalColor = new Color(0.85f, 0.25f, 0.25f, 1f);

    private RestAreaController _controller;
    private Collider2D _collider;
    private SpriteRenderer _spriteRenderer;
    private bool _locked = true;
    private bool _isProceeding;
    private bool _warnedMissingVisual;
    private bool _warnedFogVisibility;

    private void Awake()
    {
        _collider = GetComponent<Collider2D>();
        _collider.isTrigger = true;
        EnsureVisual();
        SetColliderEnabled(false);
    }

    public void Bind(RestAreaController controller)
    {
        _controller = controller;
        _isProceeding = false;
    }

    public void SetLocked(bool locked)
    {
        _locked = locked;
    }

    public void SetColliderEnabled(bool enabledState)
    {
        if (_collider == null)
            _collider = GetComponent<Collider2D>();

        if (_collider != null)
            _collider.enabled = enabledState;

        SetVisualVisible(enabledState);
    }

    public void ResetRuntimeState()
    {
        _controller = null;
        _locked = true;
        _isProceeding = false;
        SetColliderEnabled(false);
        gameObject.SetActive(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_locked || _isProceeding || _controller == null)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        _isProceeding = true;
        if (_controller.RequestEnterBoss(player))
        {
            _locked = true;
            return;
        }

        _isProceeding = false;
    }

    private void EnsureVisual()
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
            Debug.LogWarning("[BossEntryPortal] SpriteRenderer.sprite is missing. Assign a portal sprite on the prefab/scene object.", this);
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
            Debug.LogWarning("[BossEntryPortal] FogVisibilityRenderer was disabled so portal remains visible.", this);
        }
#endif
    }
}
