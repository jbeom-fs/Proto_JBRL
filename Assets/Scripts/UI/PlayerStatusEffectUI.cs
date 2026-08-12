using UnityEngine;

public sealed class PlayerStatusEffectUI : MonoBehaviour
{
    [SerializeField] private StatusEffectIconTable iconTable;
    [SerializeField] private StatusEffectIconView slowIconView;
    [SerializeField] private StatusEffectIconView stunIconView;
    [SerializeField] private StatusEffectIconView[] buffSlotViews;

    private PlayerCombatController _combat;
    private Sprite _attackBuffFallbackIcon;
    private bool _iconsResolved;
    private bool _warnedMissingTable;
    private bool _warnedMissingSlowIcon;
    private bool _warnedMissingStunIcon;
    private bool _warnedMissingAttackBuffIcon;
    private bool _warnedBuffSlotOverflow;

    private void OnEnable()
    {
        SetIconsHidden();
        ResolveIconsOnce();
        TryBindCombat();
    }

    private void OnDisable()
    {
        UnbindCombat();
    }

    private void Update()
    {
        if (_combat == null)
        {
            TryBindCombat();
            return;
        }

        RefreshActiveIcons();
    }

    private void TryBindCombat()
    {
        if (_combat != null)
            return;

        _combat = PlayerCombatController.Active;
        if (_combat == null)
            return;

        _combat.OnStatusEffectApplied += HandleStatusEffectApplied;
        _combat.OnStatusEffectEnded += HandleStatusEffectEnded;
        SyncInitialState();
    }

    private void UnbindCombat()
    {
        if (_combat == null)
            return;

        _combat.OnStatusEffectApplied -= HandleStatusEffectApplied;
        _combat.OnStatusEffectEnded -= HandleStatusEffectEnded;
        _combat = null;
    }

    private void SetIconsHidden()
    {
        if (slowIconView != null)
            slowIconView.SetVisible(false);
        if (stunIconView != null)
            stunIconView.SetVisible(false);
        if (buffSlotViews == null)
            return;

        for (int i = 0; i < buffSlotViews.Length; i++)
        {
            StatusEffectIconView view = buffSlotViews[i];
            if (view != null)
                view.SetVisible(false);
        }
    }

    private void ResolveIconsOnce()
    {
        if (_iconsResolved)
            return;

        _iconsResolved = true;
        StatusEffectIconTable table = iconTable;
        if (table == null)
        {
            WarnMissingTable();
            return;
        }

        if (slowIconView != null)
        {
            if (table.TryGetIcon(StatusEffectIconType.Slow, out Sprite slowIcon))
                slowIconView.SetIcon(slowIcon);
            else
                WarnMissingIcon(StatusEffectIconType.Slow);
        }

        if (stunIconView != null)
        {
            if (table.TryGetIcon(StatusEffectIconType.Stun, out Sprite stunIcon))
                stunIconView.SetIcon(stunIcon);
            else
                WarnMissingIcon(StatusEffectIconType.Stun);
        }

        if (table.TryGetIcon(
                StatusEffectIconType.AttackBuff,
                out Sprite attackBuffIcon))
        {
            _attackBuffFallbackIcon = attackBuffIcon;
        }
        else
        {
            WarnMissingIcon(StatusEffectIconType.AttackBuff);
        }
    }

    private void SyncInitialState()
    {
        if (_combat == null)
            return;

        SyncIcon(PlayerStatusEffectType.Slow, _combat.IsSlowed);
        SyncIcon(PlayerStatusEffectType.Stun, _combat.IsStunned);
        RefreshActiveIcons();
    }

    private void SyncIcon(PlayerStatusEffectType type, bool visible)
    {
        StatusEffectIconView view = GetView(type);
        if (view == null)
            return;

        view.SetVisible(visible);
        if (visible)
            view.MoveToLast();
    }

    private void HandleStatusEffectApplied(PlayerStatusEffectType type)
    {
        StatusEffectIconView view = GetView(type);
        if (view == null)
            return;

        view.SetVisible(true);
        view.MoveToLast();
        RefreshIcon(type, view);
    }

    private void HandleStatusEffectEnded(PlayerStatusEffectType type)
    {
        StatusEffectIconView view = GetView(type);
        if (view == null)
            return;

        view.SetVisible(false);
    }

    private void RefreshActiveIcons()
    {
        if (_combat == null)
            return;

        if (slowIconView != null && _combat.IsSlowed)
            RefreshIcon(PlayerStatusEffectType.Slow, slowIconView);
        if (stunIconView != null && _combat.IsStunned)
            RefreshIcon(PlayerStatusEffectType.Stun, stunIconView);

        RefreshBuffSlots();
    }

    private void RefreshBuffSlots()
    {
        if (_combat == null || buffSlotViews == null)
            return;

        int entryCount = _combat.BuffEntryCount;
        if (entryCount > buffSlotViews.Length)
            WarnBuffSlotOverflow(entryCount);

        for (int i = 0; i < buffSlotViews.Length; i++)
        {
            StatusEffectIconView view = buffSlotViews[i];
            if (view == null)
                continue;

            if (i >= entryCount ||
                !_combat.TryGetBuffEntry(i, out BuffEntrySnapshot snapshot))
            {
                if (view.gameObject.activeSelf)
                    view.SetVisible(false);

                continue;
            }

            if (!view.gameObject.activeSelf)
                view.SetVisible(true);

            Sprite icon = snapshot.Icon != null
                ? snapshot.Icon
                : _attackBuffFallbackIcon;
            if (icon != null)
                view.SetIcon(icon);

            if (snapshot.Infinite)
            {
                view.SetTime(0f, 1f);
                continue;
            }

            float ratio = snapshot.Duration > 0f
                ? snapshot.Remaining / snapshot.Duration
                : 0f;
            view.SetTime(snapshot.Remaining, ratio);
        }
    }

    private void RefreshIcon(PlayerStatusEffectType type, StatusEffectIconView view)
    {
        if (_combat == null || view == null)
            return;

        switch (type)
        {
            case PlayerStatusEffectType.Slow:
                view.SetTime(_combat.SlowRemainingTime, _combat.SlowRemainingRatio);
                break;

            case PlayerStatusEffectType.Stun:
                view.SetTime(_combat.StunRemainingTime, _combat.StunRemainingRatio);
                break;
        }
    }

    private StatusEffectIconView GetView(PlayerStatusEffectType type)
    {
        switch (type)
        {
            case PlayerStatusEffectType.Slow:
                return slowIconView;

            case PlayerStatusEffectType.Stun:
                return stunIconView;

            default:
                return null;
        }
    }

    private void WarnMissingTable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedMissingTable)
            return;

        _warnedMissingTable = true;
        Debug.LogWarning(
            "[PlayerStatusEffectUI] StatusEffectIconTable is missing. Inspector assignment is required; existing scene icon sprites remain in use.",
            this);
#endif
    }

    private void WarnMissingIcon(StatusEffectIconType type)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (type == StatusEffectIconType.Slow && !_warnedMissingSlowIcon)
        {
            _warnedMissingSlowIcon = true;
            Debug.LogWarning("[PlayerStatusEffectUI] Slow icon missing. Existing scene icon sprite remains in use.", this);
            return;
        }

        if (type == StatusEffectIconType.Stun && !_warnedMissingStunIcon)
        {
            _warnedMissingStunIcon = true;
            Debug.LogWarning("[PlayerStatusEffectUI] Stun icon missing. Existing scene icon sprite remains in use.", this);
            return;
        }

        if (type == StatusEffectIconType.AttackBuff &&
            !_warnedMissingAttackBuffIcon)
        {
            _warnedMissingAttackBuffIcon = true;
            Debug.LogWarning(
                "[PlayerStatusEffectUI] AttackBuff icon missing. Existing scene icon sprite remains in use.",
                this);
        }
#endif
    }

    private void WarnBuffSlotOverflow(int entryCount)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_warnedBuffSlotOverflow)
            return;

        _warnedBuffSlotOverflow = true;
        Debug.LogWarning(
            "[PlayerStatusEffectUI] Active buff count exceeds configured slots: " +
            entryCount + " active, " + buffSlotViews.Length + " visible.",
            this);
#endif
    }
}
