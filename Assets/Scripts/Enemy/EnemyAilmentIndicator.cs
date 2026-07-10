using UnityEngine;

public sealed class EnemyAilmentIndicator : MonoBehaviour
{
    private const int PoisonIndex = 0;
    private const int BleedIndex = 1;
    private const int SlowIndex = 2;
    private const int StunIndex = 3;
    private const int StatusCount = 4;
    private const float StartSlotOffset = -1.5f;

    [Header("Data")]
    [SerializeField] private StatusEffectIconTable iconTable;

    [Header("Layout")]
    [Tooltip("Fallback local Y used when EnemyHealthBar is missing.")]
    [SerializeField] private float yOffset = 0.8f;
    [SerializeField] private float iconGap = 0.08f;
    [SerializeField] private float iconWorldSize = 0.25f;
    [SerializeField] private float spacing = 0.28f;

    [Header("Rendering")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 12;

    private EnemyController _enemy;
    private EnemyHealthBar _healthBar;
    private GameObject _poisonObject;
    private GameObject _bleedObject;
    private GameObject _slowObject;
    private GameObject _stunObject;
    private Vector3 _invScale = Vector3.one;
    private Sprite _poisonIcon;
    private Sprite _bleedIcon;
    private Sprite _slowIcon;
    private Sprite _stunIcon;
    private bool _poisonVisible;
    private bool _bleedVisible;
    private bool _slowVisible;
    private bool _stunVisible;
    private bool _positioned;
    private bool _warnedMissingPoison;
    private bool _warnedMissingBleed;
    private bool _warnedMissingSlow;
    private bool _warnedMissingStun;
    private bool _warnedMissingTable;
    private readonly int[] _activeOrder = new int[StatusCount];
    private int _activeOrderCount;

    private void Awake()
    {
        _enemy = GetComponent<EnemyController>();
        _healthBar = GetComponent<EnemyHealthBar>();
        ResolveInverseScale();
        ResolveIcons();
        _poisonObject = CreateIconChild("Ailment_Poison", _poisonIcon);
        _bleedObject = CreateIconChild("Ailment_Bleed", _bleedIcon);
        _slowObject = CreateIconChild("Ailment_Slow", _slowIcon);
        _stunObject = CreateIconChild("Ailment_Stun", _stunIcon);
        ClearActiveStatuses();
    }

    private void LateUpdate()
    {
        PositionIconsIfNeeded();

        if (_enemy == null || _enemy.IsDead || !_enemy.IsAlive)
        {
            ClearActiveStatuses();
            return;
        }

        bool orderChanged = false;
        orderChanged |= UpdateStatusVisible(PoisonIndex, _poisonIcon != null && _enemy.GetAilmentStacks(AilmentType.Poison) > 0);
        orderChanged |= UpdateStatusVisible(BleedIndex, _bleedIcon != null && _enemy.GetAilmentStacks(AilmentType.Bleed) > 0);
        orderChanged |= UpdateStatusVisible(SlowIndex, _slowIcon != null && _enemy.IsSlowed);
        orderChanged |= UpdateStatusVisible(StunIndex, _stunIcon != null && _enemy.IsStunned);

        if (orderChanged)
            RefreshActiveIconPositions();
    }

    private void OnDisable()
    {
        ClearActiveStatuses();
        _positioned = false;
    }

    private GameObject CreateIconChild(string childName, Sprite icon)
    {
        GameObject child = new GameObject(childName);
        Transform childTransform = child.transform;
        childTransform.SetParent(transform, false);
        childTransform.localPosition = new Vector3(spacing * StartSlotOffset * _invScale.x, 0f, 0f);
        childTransform.localScale = ResolveIconScale(icon);

        SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = icon;
        renderer.sortingLayerName = sortingLayerName;
        renderer.sortingOrder = sortingOrder;
        child.SetActive(false);
        return child;
    }

    private void PositionIconsIfNeeded()
    {
        if (_positioned)
            return;

        _positioned = true;
        RefreshActiveIconPositions();
    }

    private void SetIconPosition(GameObject iconObject, float slotOffset, float y)
    {
        if (iconObject == null)
            return;

        iconObject.transform.localPosition = new Vector3(spacing * slotOffset * _invScale.x, y, 0f);
    }

    private Vector3 ResolveIconScale(Sprite icon)
    {
        if (icon == null || iconWorldSize <= 0f)
            return _invScale;

        Vector2 size = icon.bounds.size;
        float longest = Mathf.Max(size.x, size.y);
        float baseScale = longest > 0.0001f ? iconWorldSize / longest : 1f;
        return new Vector3(baseScale * _invScale.x, baseScale * _invScale.y, 1f);
    }

    private void ResolveInverseScale()
    {
        Vector3 scale = transform.lossyScale;
        _invScale = new Vector3(SafeInv(scale.x), SafeInv(scale.y), 1f);
    }

    private void SetPoisonVisible(bool visible)
    {
        if (_poisonVisible == visible)
            return;

        _poisonVisible = visible;
        if (_poisonObject != null)
            _poisonObject.SetActive(visible);
    }

    private void SetBleedVisible(bool visible)
    {
        if (_bleedVisible == visible)
            return;

        _bleedVisible = visible;
        if (_bleedObject != null)
            _bleedObject.SetActive(visible);
    }

    private void SetSlowVisible(bool visible)
    {
        if (_slowVisible == visible)
            return;

        _slowVisible = visible;
        if (_slowObject != null)
            _slowObject.SetActive(visible);
    }

    private void SetStunVisible(bool visible)
    {
        if (_stunVisible == visible)
            return;

        _stunVisible = visible;
        if (_stunObject != null)
            _stunObject.SetActive(visible);
    }

    private bool UpdateStatusVisible(int statusIndex, bool visible)
    {
        if (IsStatusVisible(statusIndex) == visible)
            return false;

        SetStatusVisible(statusIndex, visible);
        if (visible)
            AppendActiveStatus(statusIndex);
        else
            RemoveActiveStatus(statusIndex);

        return true;
    }

    private bool IsStatusVisible(int statusIndex)
    {
        switch (statusIndex)
        {
            case PoisonIndex:
                return _poisonVisible;
            case BleedIndex:
                return _bleedVisible;
            case SlowIndex:
                return _slowVisible;
            case StunIndex:
                return _stunVisible;
            default:
                return false;
        }
    }

    private void SetStatusVisible(int statusIndex, bool visible)
    {
        switch (statusIndex)
        {
            case PoisonIndex:
                SetPoisonVisible(visible);
                break;
            case BleedIndex:
                SetBleedVisible(visible);
                break;
            case SlowIndex:
                SetSlowVisible(visible);
                break;
            case StunIndex:
                SetStunVisible(visible);
                break;
        }
    }

    private void AppendActiveStatus(int statusIndex)
    {
        if (_activeOrderCount >= _activeOrder.Length)
            return;

        _activeOrder[_activeOrderCount] = statusIndex;
        _activeOrderCount++;
    }

    private void RemoveActiveStatus(int statusIndex)
    {
        for (int i = 0; i < _activeOrderCount; i++)
        {
            if (_activeOrder[i] != statusIndex)
                continue;

            for (int j = i; j < _activeOrderCount - 1; j++)
                _activeOrder[j] = _activeOrder[j + 1];

            _activeOrderCount--;
            return;
        }
    }

    private void RefreshActiveIconPositions()
    {
        if (!_positioned)
            return;

        float y = ResolveIconY();
        for (int i = 0; i < _activeOrderCount; i++)
            SetIconPosition(GetStatusObject(_activeOrder[i]), StartSlotOffset + i, y);
    }

    private float ResolveIconY()
    {
        return _healthBar != null
            ? _healthBar.TopAnchorY + (iconGap + iconWorldSize * 0.5f) * _invScale.y
            : yOffset * _invScale.y;
    }

    private GameObject GetStatusObject(int statusIndex)
    {
        switch (statusIndex)
        {
            case PoisonIndex:
                return _poisonObject;
            case BleedIndex:
                return _bleedObject;
            case SlowIndex:
                return _slowObject;
            case StunIndex:
                return _stunObject;
            default:
                return null;
        }
    }

    private void ClearActiveStatuses()
    {
        SetPoisonVisible(false);
        SetBleedVisible(false);
        SetSlowVisible(false);
        SetStunVisible(false);
        _activeOrderCount = 0;
    }

    private static float SafeInv(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
    }

    private void ResolveIcons()
    {
        StatusEffectIconTable table = StatusEffectIconTable.Resolve(iconTable);
        if (table == null)
        {
            WarnMissingTable();
            WarnMissingIcon(StatusEffectIconType.Poison);
            WarnMissingIcon(StatusEffectIconType.Bleed);
            WarnMissingIcon(StatusEffectIconType.Slow);
            WarnMissingIcon(StatusEffectIconType.Stun);
            return;
        }

        if (!table.TryGetIcon(StatusEffectIconType.Poison, out _poisonIcon))
            WarnMissingIcon(StatusEffectIconType.Poison);
        if (!table.TryGetIcon(StatusEffectIconType.Bleed, out _bleedIcon))
            WarnMissingIcon(StatusEffectIconType.Bleed);
        if (!table.TryGetIcon(StatusEffectIconType.Slow, out _slowIcon))
            WarnMissingIcon(StatusEffectIconType.Slow);
        if (!table.TryGetIcon(StatusEffectIconType.Stun, out _stunIcon))
            WarnMissingIcon(StatusEffectIconType.Stun);
    }

    private void WarnMissingTable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedMissingTable)
            return;

        _warnedMissingTable = true;
        Debug.LogWarning(
            "[EnemyAilmentIndicator] StatusEffectIconTable is missing. Expected Resources path: " +
            StatusEffectIconTable.ResourcePath,
            this);
#endif
    }

    private void WarnMissingIcon(StatusEffectIconType type)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (type == StatusEffectIconType.Poison && !_warnedMissingPoison)
        {
            _warnedMissingPoison = true;
            Debug.LogWarning("[EnemyAilmentIndicator] Poison icon missing; poison slot disabled.", this);
            return;
        }

        if (type == StatusEffectIconType.Bleed && !_warnedMissingBleed)
        {
            _warnedMissingBleed = true;
            Debug.LogWarning("[EnemyAilmentIndicator] Bleed icon missing; bleed slot disabled.", this);
            return;
        }

        if (type == StatusEffectIconType.Slow && !_warnedMissingSlow)
        {
            _warnedMissingSlow = true;
            Debug.LogWarning("[EnemyAilmentIndicator] Slow icon missing; slow slot disabled.", this);
            return;
        }

        if (type == StatusEffectIconType.Stun && !_warnedMissingStun)
        {
            _warnedMissingStun = true;
            Debug.LogWarning("[EnemyAilmentIndicator] Stun icon missing; stun slot disabled.", this);
        }
#endif
    }
}
