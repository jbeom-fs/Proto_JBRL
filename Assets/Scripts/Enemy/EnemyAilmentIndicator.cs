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
    // 프리팹 아이콘 실제 크기와 수동 동기: HP바 위 Y 앵커 계산에만 사용.
    [SerializeField] private float iconWorldSize = 0.25f;
    [SerializeField] private float spacing = 0.28f;

    private static readonly StatusEffectIconType[] s_IconTypes =
    {
        StatusEffectIconType.Poison,
        StatusEffectIconType.Bleed,
        StatusEffectIconType.Slow,
        StatusEffectIconType.Stun
    };

    private EnemyController _enemy;
    private EnemyHealthBar _healthBar;
    private Vector3 _invScale = Vector3.one;
    private readonly AilmentStatusSlotView[] _slots = new AilmentStatusSlotView[StatusCount];
    private readonly bool[] _hasIcon = new bool[StatusCount];
    private readonly bool[] _visible = new bool[StatusCount];
    private readonly int[] _activeOrder = new int[StatusCount];
    private int _activeOrderCount;
    private bool _initialized;
    private bool _positioned;
    private bool _warnedMissingTable;
    private bool _warnedMissingSlotPrefab;
    private bool _warnedMissingPoison;
    private bool _warnedMissingBleed;
    private bool _warnedMissingSlow;
    private bool _warnedMissingStun;

    private void Awake()
    {
        _enemy = GetComponent<EnemyController>();
        _healthBar = GetComponent<EnemyHealthBar>();
        ResolveInverseScale();
        CreateSlots();
    }

    private void LateUpdate()
    {
        if (!_initialized)
            return;

        if (_enemy == null || _enemy.IsDead || !_enemy.IsAlive)
        {
            ClearRuntimeState(true);
            return;
        }

        PositionSlotsIfNeeded();

        int poisonStacks = _hasIcon[PoisonIndex] ? _enemy.GetAilmentStacks(AilmentType.Poison) : 0;
        int bleedStacks = _hasIcon[BleedIndex] ? _enemy.GetAilmentStacks(AilmentType.Bleed) : 0;

        SetSlotStackCount(PoisonIndex, poisonStacks);
        SetSlotStackCount(BleedIndex, bleedStacks);

        bool orderChanged = false;
        orderChanged |= UpdateStatusVisible(PoisonIndex, poisonStacks > 0);
        orderChanged |= UpdateStatusVisible(BleedIndex, bleedStacks > 0);
        orderChanged |= UpdateStatusVisible(SlowIndex, _hasIcon[SlowIndex] && _enemy.IsSlowed);
        orderChanged |= UpdateStatusVisible(StunIndex, _hasIcon[StunIndex] && _enemy.IsStunned);

        if (orderChanged)
            RefreshActiveSlotPositions();
    }

    private void OnDisable()
    {
        ClearRuntimeState(true);
    }

    private void CreateSlots()
    {
        StatusEffectIconTable table = StatusEffectIconTable.Resolve(iconTable);
        if (table == null)
        {
            WarnMissingTable();
            return;
        }

        AilmentStatusSlotView prefab = table.SlotPrefab;
        if (prefab == null)
        {
            WarnMissingSlotPrefab();
            return;
        }

        for (int i = 0; i < StatusCount; i++)
        {
            AilmentStatusSlotView slot = Instantiate(prefab, transform, false);
            Transform slotTransform = slot.transform;
            slotTransform.localScale = Vector3.Scale(slotTransform.localScale, _invScale);
            slotTransform.localPosition = new Vector3(spacing * StartSlotOffset * _invScale.x, 0f, 0f);
            slot.name = "Ailment_" + s_IconTypes[i];

            if (table.TryGetIcon(s_IconTypes[i], out Sprite icon))
            {
                _hasIcon[i] = true;
                slot.SetIcon(icon);
            }
            else
            {
                WarnMissingIcon(s_IconTypes[i]);
                slot.SetIcon(null);
            }

            slot.SetStackCount(0);
            slot.SetVisible(false);
            _slots[i] = slot;
        }

        _initialized = true;
    }

    private void PositionSlotsIfNeeded()
    {
        if (_positioned)
            return;

        _positioned = true;
        RefreshActiveSlotPositions();
    }

    private bool UpdateStatusVisible(int statusIndex, bool visible)
    {
        if (_visible[statusIndex] == visible)
            return false;

        _visible[statusIndex] = visible;
        if (_slots[statusIndex] != null)
            _slots[statusIndex].SetVisible(visible);

        if (visible)
            AppendActiveStatus(statusIndex);
        else
            RemoveActiveStatus(statusIndex);

        return true;
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

    private void RefreshActiveSlotPositions()
    {
        if (!_positioned)
            return;

        float y = ResolveIconY();
        for (int i = 0; i < _activeOrderCount; i++)
        {
            AilmentStatusSlotView slot = _slots[_activeOrder[i]];
            if (slot == null)
                continue;

            float slotOffset = StartSlotOffset + i;
            slot.transform.localPosition = new Vector3(spacing * slotOffset * _invScale.x, y, 0f);
        }
    }

    private float ResolveIconY()
    {
        return _healthBar != null
            ? _healthBar.TopAnchorY + (iconGap + iconWorldSize * 0.5f) * _invScale.y
            : yOffset * _invScale.y;
    }

    private void SetSlotStackCount(int statusIndex, int stackCount)
    {
        AilmentStatusSlotView slot = _slots[statusIndex];
        if (slot != null)
            slot.SetStackCount(stackCount);
    }

    private void ClearRuntimeState(bool resetPositioned)
    {
        for (int i = 0; i < StatusCount; i++)
        {
            _visible[i] = false;
            if (_slots[i] == null)
                continue;

            _slots[i].SetVisible(false);
            if (i == PoisonIndex || i == BleedIndex)
                _slots[i].SetStackCount(0);
        }

        _activeOrderCount = 0;
        if (resetPositioned)
            _positioned = false;
    }

    private void ResolveInverseScale()
    {
        Vector3 scale = transform.lossyScale;
        _invScale = new Vector3(SafeInv(scale.x), SafeInv(scale.y), 1f);
    }

    private static float SafeInv(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
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

    private void WarnMissingSlotPrefab()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedMissingSlotPrefab)
            return;

        _warnedMissingSlotPrefab = true;
        Debug.LogWarning("[EnemyAilmentIndicator] AilmentStatusSlotView prefab is missing; indicator disabled.", this);
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
