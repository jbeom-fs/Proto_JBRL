using System;
using UnityEngine;

public sealed class BossEncounterController : ArenaEncounterBase
{
    public static BossEncounterController Active { get; private set; }

    [Header("Boss Exit")]
    [SerializeField] private BossExitPortal exitPortal;
    [SerializeField] private BossExitPortal exitPortalPrefab;
    [SerializeField] private Transform exitPortalSpawnPoint;
    [SerializeField] private Transform exitPortalParent;

    [Header("Screen Health Bar")]
    [SerializeField] private ArenaHealthBarPanel healthBarPanel;

    private BossEncounterEntry _activeEntry;
    private EnemyController _activeBoss;
    private BossExitPortal _activeExitPortal;
    private bool _hasEncounter;
    private bool _bossDefeated;
    private bool _proceedRequested;

    public event Action<BossEncounterEntry, PlayerController> ProceedRequested;

    public bool IsEncounterActive => _hasEncounter && !_bossDefeated;
    public bool IsBossDefeated => _hasEncounter && _bossDefeated;
    public BossEncounterEntry ActiveEntry => _activeEntry;

    private ArenaHealthBarPanel HealthBarPanel =>
        healthBarPanel != null ? healthBarPanel : ArenaHealthBarPanel.Active;

    private void Awake()
    {
        if (Active != null && Active != this)
        {
            Destroy(gameObject);
            return;
        }

        Active = this;
        HideExitPortal();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Active, this))
            Active = null;
    }

    public bool Begin(BossEncounterEntry entry, PlayerController player)
    {
        if (_hasEncounter || entry == null || player == null)
            return false;

        CloseArenaDoor();
        _activeEntry = entry;
        _hasEncounter = true;
        _bossDefeated = false;
        _proceedRequested = false;
        HideExitPortal();

        if (!TryTeleportPlayerToArena(player, entry.BossAreaDestinationId))
        {
            CancelEncounter();
            return false;
        }

        if (!TrySpawnBoss(entry.Boss))
        {
            CancelEncounter();
            return false;
        }

        return true;
    }

    public bool RequestProceed(PlayerController player)
    {
        if (!_hasEncounter || !_bossDefeated || _proceedRequested || player == null)
            return false;

        _proceedRequested = true;
        HideExitPortal();

        if (_activeEntry != null && _activeEntry.IsFinal)
        {
            HandleFinalBossDefeated();
            return true;
        }

        ProceedRequested?.Invoke(_activeEntry, player);
        return true;
    }

    public void CompleteProceedToNextFloor()
    {
        RestoreDungeonMinimapSource();
        CancelEncounter();
    }

    public void ResetProceedRequest()
    {
        if (!_hasEncounter || !_bossDefeated)
            return;

        _proceedRequested = false;
        ShowExitPortal();
    }

    public void CancelEncounter()
    {
        if (_activeBoss != null)
        {
            _activeBoss.OnDied -= OnBossDied;
            _activeBoss = null;
        }

        HealthBarPanel?.DetachAll();

        _activeEntry = null;
        _hasEncounter = false;
        _bossDefeated = false;
        _proceedRequested = false;
        HideExitPortal();
        CloseArenaDoor();
    }

    public void ClearRuntimeState()
    {
        CancelEncounter();

        if (_activeExitPortal != null)
            _activeExitPortal.ResetRuntimeState();

        HideExitPortal();
    }

    private bool TrySpawnBoss(EnemyData bossData)
    {
        if (bossData == null || EnemyPoolManager.Instance == null)
            return false;

        if (!TryResolveArenaEnemySpawnPosition(out Vector3 spawnPosition))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[BossEncounterController] Boss area has no valid spawn position.", this);
#endif
            return false;
        }

        System.Random dropRng = CreateBossDropRng();
        EnemyController boss = SpawnArenaEnemyAtPosition(bossData, spawnPosition, OnBossDied, dropRng);
        if (boss == null)
            return false;

        _activeBoss = boss;
        HealthBarPanel?.Attach(boss, true);
        return true;
    }

    private System.Random CreateBossDropRng()
    {
        DungeonManager dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null || dungeonManager.Data == null)
            return null;

        int dropSeed = DeterministicSeedUtility.CreateSeed(
            dungeonManager.seed,
            (int)dungeonManager.Data.currentStageRegion,
            dungeonManager.floor,
            0,
            DeterministicSeedUtility.EnemyDropDomain);
        return new System.Random(dropSeed);
    }

    private void OnBossDied(EnemyController boss)
    {
        if (boss != null)
            boss.OnDied -= OnBossDied;

        HealthBarPanel?.Detach(boss);

        if (_activeBoss == boss)
            _activeBoss = null;

        _bossDefeated = true;
        OpenArenaDoor();
        ShowExitPortal();
    }

    private void ShowExitPortal()
    {
        BossExitPortal portal = GetExitPortal();
        if (portal == null)
            return;

        if (TryResolveExitPortalPosition(out Vector3 position))
            portal.transform.position = position;

        portal.Bind(this);
        portal.gameObject.SetActive(true);
        portal.SetColliderEnabled(true);
        portal.SetLocked(false);
    }

    private void HideExitPortal()
    {
        BossExitPortal portal = _activeExitPortal != null ? _activeExitPortal : exitPortal;
        if (portal == null)
            return;

        portal.SetLocked(true);
        portal.SetColliderEnabled(false);
        portal.gameObject.SetActive(false);
    }

    private BossExitPortal GetExitPortal()
    {
        if (_activeExitPortal != null)
            return _activeExitPortal;

        if (exitPortal != null)
        {
            _activeExitPortal = exitPortal;
            return _activeExitPortal;
        }

        if (exitPortalPrefab == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[BossEncounterController] Boss exit portal prefab/scene reference is missing.", this);
#endif
            return null;
        }

        Transform parent = exitPortalParent != null ? exitPortalParent : transform;
        _activeExitPortal = Instantiate(exitPortalPrefab, parent);
        _activeExitPortal.gameObject.SetActive(false);
        _activeExitPortal.SetColliderEnabled(false);
        _activeExitPortal.SetLocked(true);
        return _activeExitPortal;
    }

    private bool TryResolveExitPortalPosition(out Vector3 position)
    {
        if (exitPortalSpawnPoint != null)
        {
            position = exitPortalSpawnPoint.position;
            return true;
        }

        if (TryGetCenterTileWorldPosition(arenaWalkTilemap, out position))
            return true;

        if (walkabilityArea != null &&
            walkabilityArea.TryGetNearestWalkableWorldPosition(transform.position, out position))
        {
            return true;
        }

        position = transform.position;
        return false;
    }

    private void HandleFinalBossDefeated()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[BossEncounterController] Final boss defeated. TODO: ending sequence/screen is undecided.", this);
#endif
        // TODO(stage4+): connect final boss clear to ending flow.
    }
}
