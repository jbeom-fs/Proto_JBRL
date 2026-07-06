using UnityEngine;

public sealed class EnemyAilmentIndicator : MonoBehaviour
{
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
    private Vector3 _invScale = Vector3.one;
    private Sprite _poisonIcon;
    private Sprite _bleedIcon;
    private bool _poisonVisible;
    private bool _bleedVisible;
    private bool _positioned;
    private bool _warnedMissingPoison;
    private bool _warnedMissingBleed;
    private bool _warnedMissingTable;

    private void Awake()
    {
        _enemy = GetComponent<EnemyController>();
        _healthBar = GetComponent<EnemyHealthBar>();
        ResolveInverseScale();
        ResolveIcons();
        _poisonObject = CreateIconChild("Ailment_Poison", _poisonIcon, -0.5f);
        _bleedObject = CreateIconChild("Ailment_Bleed", _bleedIcon, 0.5f);
        SetPoisonVisible(false);
        SetBleedVisible(false);
    }

    private void LateUpdate()
    {
        PositionIconsIfNeeded();

        if (_enemy == null || _enemy.IsDead || !_enemy.IsAlive)
        {
            SetPoisonVisible(false);
            SetBleedVisible(false);
            return;
        }

        SetPoisonVisible(_poisonIcon != null && _enemy.GetAilmentStacks(AilmentType.Poison) > 0);
        SetBleedVisible(_bleedIcon != null && _enemy.GetAilmentStacks(AilmentType.Bleed) > 0);
    }

    private void OnDisable()
    {
        SetPoisonVisible(false);
        SetBleedVisible(false);
    }

    private GameObject CreateIconChild(string childName, Sprite icon, float slotOffset)
    {
        GameObject child = new GameObject(childName);
        Transform childTransform = child.transform;
        childTransform.SetParent(transform, false);
        childTransform.localPosition = new Vector3(spacing * slotOffset * _invScale.x, 0f, 0f);
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
        float y = _healthBar != null
            ? _healthBar.TopAnchorY + (iconGap + iconWorldSize * 0.5f) * _invScale.y
            : yOffset * _invScale.y;

        SetIconPosition(_poisonObject, -0.5f, y);
        SetIconPosition(_bleedObject, 0.5f, y);
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
            return;
        }

        if (!table.TryGetIcon(StatusEffectIconType.Poison, out _poisonIcon))
            WarnMissingIcon(StatusEffectIconType.Poison);
        if (!table.TryGetIcon(StatusEffectIconType.Bleed, out _bleedIcon))
            WarnMissingIcon(StatusEffectIconType.Bleed);
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
        }
#endif
    }
}
