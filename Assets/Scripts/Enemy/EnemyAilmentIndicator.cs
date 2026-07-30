using System;
using UnityEngine;

public sealed class EnemyAilmentIndicator : MonoBehaviour
{
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
    private readonly AilmentStatusSlotView[] _slots = new AilmentStatusSlotView[s_IconTypes.Length];
    private readonly bool[] _hasIcon = new bool[s_IconTypes.Length];
    private readonly bool[] _warnedMissingIcons = new bool[s_IconTypes.Length];
    private readonly AilmentStatusTracker _statusTracker = new AilmentStatusTracker();
    private bool _initialized;
    private bool _positioned;
    private bool _suppressed;
    private bool _warnedMissingTable;
    private bool _warnedMissingSlotPrefab;

    private void Awake()
    {
        _enemy = GetComponent<EnemyController>();
        _healthBar = GetComponent<EnemyHealthBar>();
        ResolveInverseScale();
        CreateSlots();
    }

    private void LateUpdate()
    {
        if (_suppressed || !_initialized)
            return;

        if (_enemy == null || _enemy.IsDead || !_enemy.IsAlive)
        {
            ClearRuntimeState(true);
            return;
        }

        PositionSlotsIfNeeded();

        int poisonStacks = HasIcon(StatusEffectIconType.Poison)
            ? _enemy.GetAilmentStacks(AilmentType.Poison)
            : 0;
        int bleedStacks = HasIcon(StatusEffectIconType.Bleed)
            ? _enemy.GetAilmentStacks(AilmentType.Bleed)
            : 0;

        SetSlotStackCount(StatusEffectIconType.Poison, poisonStacks);
        SetSlotStackCount(StatusEffectIconType.Bleed, bleedStacks);

        bool orderChanged = _statusTracker.Update(
            poisonStacks,
            bleedStacks,
            HasIcon(StatusEffectIconType.Slow) && _enemy.IsSlowed,
            HasIcon(StatusEffectIconType.Stun) && _enemy.IsStunned);

        if (orderChanged)
        {
            RefreshSlotVisibility();
            RefreshActiveSlotPositions();
        }
    }

    private void OnDisable()
    {
        ClearRuntimeState(true);
    }

    public void SetSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        if (suppressed)
            ClearRuntimeState(true);
    }

    private void CreateSlots()
    {
        StatusEffectIconTable table = iconTable;
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

        for (int i = 0; i < s_IconTypes.Length; i++)
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

    private void RefreshSlotVisibility()
    {
        for (int i = 0; i < s_IconTypes.Length; i++)
        {
            if (_slots[i] != null)
                _slots[i].SetVisible(_statusTracker.IsActive(s_IconTypes[i]));
        }
    }

    private void RefreshActiveSlotPositions()
    {
        if (!_positioned)
            return;

        float y = ResolveIconY();
        var activeStatuses = _statusTracker.ActiveStatuses;
        for (int i = 0; i < activeStatuses.Count; i++)
        {
            int statusIndex = GetStatusIndex(activeStatuses[i].Type);
            if (statusIndex < 0)
                continue;

            AilmentStatusSlotView slot = _slots[statusIndex];
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

    private void SetSlotStackCount(StatusEffectIconType type, int stackCount)
    {
        int statusIndex = GetStatusIndex(type);
        if (statusIndex < 0)
            return;

        AilmentStatusSlotView slot = _slots[statusIndex];
        if (slot != null)
            slot.SetStackCount(stackCount);
    }

    private void ClearRuntimeState(bool resetPositioned)
    {
        for (int i = 0; i < s_IconTypes.Length; i++)
        {
            if (_slots[i] == null)
                continue;

            _slots[i].SetVisible(false);
            StatusEffectIconType type = s_IconTypes[i];
            if (type == StatusEffectIconType.Poison || type == StatusEffectIconType.Bleed)
                _slots[i].SetStackCount(0);
        }

        _statusTracker.Reset();
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

    private bool HasIcon(StatusEffectIconType type)
    {
        int statusIndex = GetStatusIndex(type);
        return statusIndex >= 0 && _hasIcon[statusIndex];
    }

    private static int GetStatusIndex(StatusEffectIconType type)
    {
        return Array.IndexOf(s_IconTypes, type);
    }

    private void WarnMissingTable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedMissingTable)
            return;

        _warnedMissingTable = true;
        Debug.LogWarning(
            "[EnemyAilmentIndicator] StatusEffectIconTable is missing. Inspector assignment is required.",
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
        int statusIndex = GetStatusIndex(type);
        if (statusIndex < 0 || _warnedMissingIcons[statusIndex])
            return;

        _warnedMissingIcons[statusIndex] = true;
        Debug.LogWarning(
            "[EnemyAilmentIndicator] " + type + " icon missing; " +
            type.ToString().ToLowerInvariant() + " slot disabled.",
            this);
#endif
    }
}
