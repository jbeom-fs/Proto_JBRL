using UnityEngine;

public sealed class RestAreaController : MonoBehaviour
{
    public static RestAreaController Active { get; private set; }

    [SerializeField] private LocationTransitionManager transitionManager;
    [SerializeField] private string restAreaDestinationId = "rest_area";
    [SerializeField] private BossEntryPortal entryPortal;

    private BossEncounterEntry _pendingEntry;

    public bool HasPendingEncounter => _pendingEntry != null;

    private void Awake()
    {
        if (Active != null && Active != this)
        {
            Destroy(gameObject);
            return;
        }

        Active = this;
        HideEntryPortal();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Active, this))
            Active = null;
    }

    public bool Begin(BossEncounterEntry entry, PlayerController player)
    {
        if (_pendingEntry != null || entry == null || player == null)
            return false;

        if (entryPortal == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[RestAreaController] entryPortal is not assigned.", this);
#endif
            return false;
        }

        LocationTransitionManager manager =
            transitionManager != null ? transitionManager : LocationTransitionManager.Active;
        if (manager == null || !manager.TryTeleportPlayer(player, restAreaDestinationId))
            return false;

        _pendingEntry = entry;
        ShowEntryPortal();
        return true;
    }

    public bool RequestEnterBoss(PlayerController player)
    {
        if (_pendingEntry == null || player == null)
            return false;

        BossEncounterController boss = BossEncounterController.Active;
        if (boss == null)
            return false;

        if (!boss.Begin(_pendingEntry, player))
            return false;

        _pendingEntry = null;
        HideEntryPortal();
        return true;
    }

    public void ClearRuntimeState()
    {
        _pendingEntry = null;
        HideEntryPortal();
    }

    private void ShowEntryPortal()
    {
        entryPortal.Bind(this);
        entryPortal.gameObject.SetActive(true);
        entryPortal.SetColliderEnabled(true);
        entryPortal.SetLocked(false);
    }

    private void HideEntryPortal()
    {
        if (entryPortal == null)
            return;

        entryPortal.SetLocked(true);
        entryPortal.SetColliderEnabled(false);
        entryPortal.gameObject.SetActive(false);
    }
}
