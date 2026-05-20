// NOTE: The drag handle GameObject must have a Graphic component (e.g., Image)
// with Raycast Target enabled so the EventSystem can deliver drag events here.
// Any child text that should not block clicks (e.g., TitleText) must set raycastTarget = false.
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class UIDraggableWindow : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private RectTransform targetWindow;
    [SerializeField] private RectTransform boundaryRoot;
    [SerializeField] private bool clampToBoundary = true;
    [SerializeField] private bool bringToFrontOnDrag = true;

    private RectTransform _cachedTarget;
    private RectTransform _cachedBoundary;
    private Vector2 _startPointerLocal;
    private Vector2 _startWindowAnchoredPos;
    private bool _dragging;

    // Call once after runtime UI creation to set target and boundary explicitly.
    public void Initialize(RectTransform target, RectTransform boundary)
    {
        targetWindow = target;
        boundaryRoot = boundary;
        _cachedTarget = target;
        _cachedBoundary = boundary;
    }

    private void Awake()
    {
        _cachedTarget = targetWindow;
        _cachedBoundary = boundaryRoot;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        EnsureCachedReferences();
        if (_cachedTarget == null)
            return;

        _startWindowAnchoredPos = _cachedTarget.anchoredPosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _cachedBoundary,
            eventData.position,
            eventData.pressEventCamera,
            out _startPointerLocal);

        _dragging = true;

        if (bringToFrontOnDrag)
            _cachedTarget.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || _cachedTarget == null)
            return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _cachedBoundary,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 currentLocal))
            return;

        Vector2 delta = currentLocal - _startPointerLocal;
        Vector2 desired = _startWindowAnchoredPos + delta;

        if (clampToBoundary && _cachedBoundary != null)
            desired = ClampAnchoredPositionToBoundary(desired);

        _cachedTarget.anchoredPosition = desired;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _dragging = false;
    }

    // Populates cache on first drag if fields were not pre-assigned via Initialize().
    private void EnsureCachedReferences()
    {
        if (_cachedTarget == null)
            _cachedTarget = targetWindow != null ? targetWindow : transform.parent as RectTransform;

        if (_cachedBoundary != null)
            return;

        if (boundaryRoot != null)
        {
            _cachedBoundary = boundaryRoot;
            return;
        }

        if (_cachedTarget != null)
        {
            Canvas canvas = _cachedTarget.GetComponentInParent<Canvas>();
            if (canvas != null)
                _cachedBoundary = (RectTransform)canvas.transform;
        }
    }

    // Clamps desiredAnchoredPosition so targetWindow stays fully inside boundaryRoot.
    // Requires targetWindow to be a direct child of boundaryRoot (or share the same
    // coordinate space) so anchoredPosition offsets map directly to boundary local space.
    // This holds when targetWindow uses a non-stretch anchor (anchorMin == anchorMax)
    // and is parented to a center-pivoted Canvas.
    private Vector2 ClampAnchoredPositionToBoundary(Vector2 desired)
    {
        if (_cachedTarget == null || _cachedBoundary == null)
            return desired;

        Rect boundary = _cachedBoundary.rect;
        Vector2 size   = _cachedTarget.rect.size;
        Vector2 pivot  = _cachedTarget.pivot;

        float left   = desired.x - pivot.x * size.x;
        float right  = desired.x + (1f - pivot.x) * size.x;
        float bottom = desired.y - pivot.y * size.y;
        float top    = desired.y + (1f - pivot.y) * size.y;

        float cx = desired.x;
        float cy = desired.y;

        if (left < boundary.xMin)
            cx += boundary.xMin - left;
        else if (right > boundary.xMax)
            cx += boundary.xMax - right;

        if (bottom < boundary.yMin)
            cy += boundary.yMin - bottom;
        else if (top > boundary.yMax)
            cy += boundary.yMax - top;

        return new Vector2(cx, cy);
    }
}
