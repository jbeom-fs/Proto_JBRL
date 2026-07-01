using UnityEngine;

public sealed class EngravingStationPlacer : MonoBehaviour
{
    [SerializeField] private DungeonEventChannel eventChannel;
    [SerializeField] private EngravingStation stationPrefab;
    [SerializeField] private EngravingLoadoutUIController loadoutUI;
    [SerializeField] private Transform stationParent;

    private EngravingStation _instance;

    private void OnEnable()
    {
        if (eventChannel == null)
            return;

        eventChannel.OnStairRoomEntered += HandleStairRoomEntered;
        eventChannel.OnFloorChanged += HandleFloorChanged;
    }

    private void OnDisable()
    {
        if (eventChannel != null)
        {
            eventChannel.OnStairRoomEntered -= HandleStairRoomEntered;
            eventChannel.OnFloorChanged -= HandleFloorChanged;
        }

        ClearRuntimeState();
    }

    private void HandleStairRoomEntered(RoomEnteredEventArgs args)
    {
        if (!args.IsFirstVisit)
            return;

        DungeonManager dungeonManager = DungeonManager.Instance;
        if (dungeonManager == null || dungeonManager.Data == null)
            return;

        if (!DungeonPlacementUtility.TryGetRoomCenterWalkablePosition(args.Room, dungeonManager, out Vector3 position))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[EngravingStationPlacer] Stair room has no walkable center position.", this);
#endif
            return;
        }

        EngravingStation station = GetOrCreateInstance();
        if (station == null)
            return;

        station.transform.position = position;
        station.Bind(loadoutUI);
        station.gameObject.SetActive(true);
    }

    private void HandleFloorChanged(int prevFloor, int nextFloor)
    {
        ClearRuntimeState();
    }

    public void ClearRuntimeState()
    {
        if (_instance != null)
            _instance.gameObject.SetActive(false);
    }

    private EngravingStation GetOrCreateInstance()
    {
        if (_instance != null)
            return _instance;

        if (stationPrefab == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[EngravingStationPlacer] stationPrefab is not assigned.", this);
#endif
            return null;
        }

        Transform parent = stationParent != null ? stationParent : transform;
        _instance = Instantiate(stationPrefab, parent);
        return _instance;
    }
}
