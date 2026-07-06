using UnityEngine;

public sealed class PlayerStatusEffectUI : MonoBehaviour
{
    [SerializeField] private StatusEffectIconTable iconTable;
    [SerializeField] private StatusEffectIconView slowIconView;
    [SerializeField] private StatusEffectIconView stunIconView;

    private PlayerCombatController _combat;
    private bool _iconsResolved;
    private bool _warnedMissingTable;
    private bool _warnedMissingSlowIcon;
    private bool _warnedMissingStunIcon;

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
    }

    private void ResolveIconsOnce()
    {
        if (_iconsResolved)
            return;

        _iconsResolved = true;
        StatusEffectIconTable table = StatusEffectIconTable.Resolve(iconTable);
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
            "[PlayerStatusEffectUI] StatusEffectIconTable is missing. Existing scene icon sprites remain in use. Expected Resources path: " +
            StatusEffectIconTable.ResourcePath,
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
        }
#endif
    }
}
