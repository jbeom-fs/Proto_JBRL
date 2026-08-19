using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class BossEncounterController : ArenaEncounterBase
{
    public static BossEncounterController Active { get; private set; }

    protected override DropRank EncounterDropRank => DropRank.Boss;

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
    private ArenaSpace _activeSpace;
    private bool _hasEncounter;
    private bool _bossDefeated;
    private bool _proceedRequested;
    private int _phaseIndex;
    private int _phaseDamageFloor;
    private EnemyPatternRunner _bossPatternRunner;
    private bool _warnedMissingRunner;
    private bool _warnedMissingArenaSpace;
    private bool _warnedInvalidSpaceExitPortal;

    public event Action<BossEncounterEntry, PlayerController> ProceedRequested;

    public bool IsEncounterActive => _hasEncounter && !_bossDefeated;
    public bool IsBossDefeated => _hasEncounter && _bossDefeated;
    public BossEncounterEntry ActiveEntry => _activeEntry;

    private ArenaHealthBarPanel HealthBarPanel =>
        healthBarPanel != null ? healthBarPanel : ArenaHealthBarPanel.Active;

    protected override ArenaDoor ActiveArenaDoor =>
        _activeSpace != null && _activeSpace.ArenaDoor != null
            ? _activeSpace.ArenaDoor
            : base.ActiveArenaDoor;

    protected override Tilemap ActiveWalkTilemap =>
        _activeSpace != null && _activeSpace.WalkTilemap != null
            ? _activeSpace.WalkTilemap
            : base.ActiveWalkTilemap;

    protected override WalkabilityArea ActiveWalkabilityArea =>
        _activeSpace != null && _activeSpace.WalkabilityArea != null
            ? _activeSpace.WalkabilityArea
            : base.ActiveWalkabilityArea;

    protected override Transform ActiveEnemySpawnPoint =>
        _activeSpace != null && _activeSpace.EnemySpawnPoint != null
            ? _activeSpace.EnemySpawnPoint
            : base.ActiveEnemySpawnPoint;

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

    private void LateUpdate()
    {
        IReadOnlyList<BossPhase> phases = _activeEntry?.Phases;
        if (!_hasEncounter ||
            _bossDefeated ||
            _activeBoss == null ||
            !_activeBoss.IsAlive ||
            phases == null ||
            _phaseIndex >= phases.Count - 1)
        {
            return;
        }

        if (_activeBoss.CurrentHp <= _phaseDamageFloor)
            EnterPhase(_phaseIndex + 1);
    }

    public bool Begin(BossEncounterEntry entry, PlayerController player)
    {
        if (_hasEncounter || entry == null || player == null)
            return false;

        ArenaSpace resolvedSpace = ResolveArenaSpace(entry.BossAreaDestinationId);
        if (_activeSpace != resolvedSpace)
            _activeExitPortal = null;
        _activeSpace = resolvedSpace;

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
            _activeBoss.SetDamageFloor(0);
            _activeBoss.SetMaxHpOverride(0, false);
            _activeBoss = null;
        }

        HealthBarPanel?.DetachAll();

        _activeEntry = null;
        _hasEncounter = false;
        _bossDefeated = false;
        _proceedRequested = false;
        _phaseIndex = 0;
        _phaseDamageFloor = 0;
        _bossPatternRunner = null;
        _warnedMissingRunner = false;
        HideExitPortal();
        CloseArenaDoor();
    }

    public void ClearRuntimeState()
    {
        CancelEncounter();

        if (_activeExitPortal != null)
            _activeExitPortal.ResetRuntimeState();

        HideExitPortal();
        _activeExitPortal = null;
        _activeSpace = null;
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

        boss.MarkAsBossEncounterEnemy();
        _activeBoss = boss;
        _bossPatternRunner = boss.GetComponent<EnemyPatternRunner>();

        IReadOnlyList<BossPhase> phases = _activeEntry?.Phases;
        if (phases != null && phases.Count > 0)
        {
            if (_bossPatternRunner == null && !_warnedMissingRunner)
            {
                _warnedMissingRunner = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning(
                    "[BossEncounterController] EnemyPatternRunner is missing; phase HP rules will continue without pattern swaps.",
                    boss);
#endif
            }

            EnterPhase(0);
        }

        HealthBarPanel?.Attach(boss, true);
        return true;
    }

    private void EnterPhase(int index)
    {
        IReadOnlyList<BossPhase> phases = _activeEntry?.Phases;
        if (_activeBoss == null || phases == null || index < 0 || index >= phases.Count)
            return;

        BossPhase phase = phases[index];
        if (phase == null)
            return;

        bool refill = index > 0 &&
            phases[index - 1] != null &&
            phases[index - 1].Exit == BossPhaseExit.Depletion;

        _bossPatternRunner?.SetPatternSet(phase.PatternSet);
        _activeBoss.SetMaxHpOverride(
            phase.MaxHpOverride,
            refill || (index == 0 && phase.MaxHpOverride > 0));

        if (refill)
        {
            _activeBoss.ResetStatusEffects();
            DaggerMarkerRegistry.Instance.Clear(_activeBoss);
        }

        if (index >= phases.Count - 1)
        {
            _phaseDamageFloor = 0;
        }
        else if (phase.Exit == BossPhaseExit.HpRatio)
        {
            _phaseDamageFloor = Mathf.RoundToInt(_activeBoss.MaxHp * phase.ExitHpRatio);
        }
        else
        {
            _phaseDamageFloor = 1;
        }

        _activeBoss.SetDamageFloor(_phaseDamageFloor);
        _phaseIndex = index;
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

        if (_activeSpace != null && _activeSpace.ClearedPortal != null)
        {
            if (_activeSpace.ClearedPortal is BossExitPortal spacePortal)
            {
                _activeExitPortal = spacePortal;
                return _activeExitPortal;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_warnedInvalidSpaceExitPortal)
            {
                _warnedInvalidSpaceExitPortal = true;
                Debug.LogWarning(
                    "[BossEncounterController] ArenaSpace cleared portal is not a BossExitPortal; using fallback.",
                    _activeSpace);
            }
#endif
        }

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
        Transform spawnPoint = _activeSpace != null && _activeSpace.ClearedPortalSpawnPoint != null
            ? _activeSpace.ClearedPortalSpawnPoint
            : exitPortalSpawnPoint;

        if (spawnPoint != null)
        {
            position = spawnPoint.position;
            return true;
        }

        if (TryGetCenterTileWorldPosition(ActiveWalkTilemap, out position))
            return true;

        if (ActiveWalkabilityArea != null &&
            ActiveWalkabilityArea.TryGetNearestWalkableWorldPosition(transform.position, out position))
        {
            return true;
        }

        position = transform.position;
        return false;
    }

    private ArenaSpace ResolveArenaSpace(string destinationId)
    {
        transitionManager = transitionManager != null
            ? transitionManager
            : LocationTransitionManager.Active;

        if (transitionManager != null &&
            transitionManager.TryResolveLocationRoot(destinationId, out LocationRoot root))
        {
            ArenaSpace space = root.GetComponent<ArenaSpace>();
            if (space != null)
                return space;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_warnedMissingArenaSpace)
        {
            _warnedMissingArenaSpace = true;
            Debug.LogWarning(
                "[BossEncounterController] ArenaSpace could not be resolved for destination '" +
                destinationId + "'; using serialized fallback references.",
                this);
        }
#endif
        return null;
    }

    private void HandleFinalBossDefeated()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[BossEncounterController] Final boss defeated. TODO: ending sequence/screen is undecided.", this);
#endif
        // TODO(stage4+): connect final boss clear to ending flow.
    }
}
