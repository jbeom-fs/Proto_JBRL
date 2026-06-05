using System.Collections.Generic;
using UnityEngine;

// Developer Console 명령의 실제 실행을 담당하는 facade.
// DeveloperConsoleService는 파싱/등록/자동완성만 담당하고,
// 게임 상태를 바꾸는 호출은 모두 이 컴포넌트로 위임됩니다.
public sealed class DeveloperConsoleCommandExecutor : MonoBehaviour
{
    [Header("Gameplay Dependencies")]
    [SerializeField] private RoomSpawner roomSpawner;
    [SerializeField] private DungeonManager dungeonManager;
    [SerializeField] private LocationTransitionManager transitionManager;
    [SerializeField] private EliteArenaEncounterController eliteArenaEncounterController;
    [SerializeField] private PlayerController player;
    [SerializeField] private PlayerFormController playerFormController;
    [SerializeField] private TeleportDestinationDatabase teleportDestinationDatabase;

    public bool HasTeleportDestinationDatabase => teleportDestinationDatabase != null;

    public void GetTeleportDestinationIds(List<string> output)
    {
        if (output == null || teleportDestinationDatabase == null)
            return;

        teleportDestinationDatabase.GetDestinationIds(output);
    }

    public DeveloperConsoleCommandResult ExecuteKill()
    {
        RoomSpawner spawner = ResolveRoomSpawner();
        if (spawner == null)
        {
            WarnMissing(nameof(RoomSpawner));
            return DeveloperConsoleCommandResult.Error("RoomSpawner is not active.");
        }

        int killedCount = spawner.ForceKillCurrentEncounterEnemiesForDebug();
        if (killedCount <= 0)
            return DeveloperConsoleCommandResult.Success("No enemies to kill in current room or elite arena.");

        return DeveloperConsoleCommandResult.Success("Killed " + killedCount + " enemy(s) in current room or elite arena.");
    }

    public DeveloperConsoleCommandResult ExecuteOpenNormalDoors()
    {
        if (!TryGetDungeonManager(out DungeonManager manager))
            return DeveloperConsoleCommandResult.Error("DungeonManager is not assigned.");

        int openedCount = manager.OpenDebugNormalDoors();
        if (openedCount <= 0)
            return DeveloperConsoleCommandResult.Success("No normal doors to open.");

        return DeveloperConsoleCommandResult.Success("Opened " + openedCount + " normal door(s).");
    }

    public DeveloperConsoleCommandResult ExecuteOpenEliteDoors()
    {
        if (!TryGetDungeonManager(out DungeonManager manager))
            return DeveloperConsoleCommandResult.Error("DungeonManager is not assigned.");

        int openedCount = manager.OpenDebugEliteDoors();
        if (openedCount <= 0)
            return DeveloperConsoleCommandResult.Success("No elite doors to open.");

        return DeveloperConsoleCommandResult.Success("Opened " + openedCount + " elite door(s).");
    }

    public DeveloperConsoleCommandResult ExecuteTeleport(string destinationId)
    {
        if (transitionManager == null)
        {
            WarnMissing(nameof(LocationTransitionManager));
            return DeveloperConsoleCommandResult.Error("Teleport manager is not assigned.");
        }

        if (teleportDestinationDatabase == null)
        {
            WarnMissing(nameof(TeleportDestinationDatabase));
            return DeveloperConsoleCommandResult.Error("Teleport destination database is not assigned.");
        }

        if (player == null)
        {
            WarnMissing(nameof(PlayerController));
            return DeveloperConsoleCommandResult.Error("Player is not assigned.");
        }

        if (!teleportDestinationDatabase.TryResolveLocationId(destinationId, out string resolvedId))
            return DeveloperConsoleCommandResult.Error("Unknown destinationId: " + destinationId);

        transitionManager.TeleportPlayer(player, resolvedId);
        return DeveloperConsoleCommandResult.Success("Teleported to " + resolvedId);
    }

    public DeveloperConsoleCommandResult ExecuteFloorAdd(int count)
    {
        if (!TryGetDungeonManager(out DungeonManager manager))
            return DeveloperConsoleCommandResult.Error("DungeonManager is not assigned.");

        if (count > manager.MaxFloor - manager.CurrentFloor)
            return DeveloperConsoleCommandResult.Error("Invalid floor: target floor exceeds max floor.");

        return ExecuteFloorTransition(manager, manager.CurrentFloor + count);
    }

    public DeveloperConsoleCommandResult ExecuteFloorSub(int count)
    {
        if (!TryGetDungeonManager(out DungeonManager manager))
            return DeveloperConsoleCommandResult.Error("DungeonManager is not assigned.");

        if (count > manager.CurrentFloor - manager.MinFloor)
            return DeveloperConsoleCommandResult.Error("Invalid floor: target floor must be 1 or higher.");

        return ExecuteFloorTransition(manager, manager.CurrentFloor - count);
    }

    public DeveloperConsoleCommandResult ExecuteFloorSet(int targetFloor)
    {
        if (!TryGetDungeonManager(out DungeonManager manager))
            return DeveloperConsoleCommandResult.Error("DungeonManager is not assigned.");

        if (targetFloor < manager.MinFloor || targetFloor > manager.MaxFloor)
        {
            return DeveloperConsoleCommandResult.Error(
                "Invalid floor: floor must be between " + manager.MinFloor + " and " + manager.MaxFloor + ".");
        }

        return ExecuteFloorTransition(manager, targetFloor);
    }

    public DeveloperConsoleCommandResult ExecuteSetForm(string formName)
    {
        if (playerFormController == null)
        {
            WarnMissing(nameof(PlayerFormController));
            return DeveloperConsoleCommandResult.Error("PlayerFormController is not assigned.");
        }

        if (!System.Enum.TryParse(formName, true, out PlayerFormId id))
            return DeveloperConsoleCommandResult.Error("Unknown form: " + formName);

        return playerFormController.TrySwitchForm(id)
            ? DeveloperConsoleCommandResult.Success("Form switched to " + id)
            : DeveloperConsoleCommandResult.Error("Form switch rejected (busy/stunned/unknown).");
    }

    public void GetFormIds(List<string> output)
    {
        if (output == null)
            return;

        foreach (string name in System.Enum.GetNames(typeof(PlayerFormId)))
            output.Add(name);
    }

    private RoomSpawner ResolveRoomSpawner()
    {
        if (roomSpawner != null)
            return roomSpawner;
        return RoomSpawner.Active;
    }

    private bool TryGetDungeonManager(out DungeonManager manager)
    {
        manager = dungeonManager;
        if (manager != null)
            return true;

        WarnMissing(nameof(DungeonManager));
        return false;
    }

    private static DeveloperConsoleCommandResult ExecuteFloorTransition(DungeonManager manager, int targetFloor)
    {
        if (targetFloor == manager.CurrentFloor)
            return DeveloperConsoleCommandResult.Success("Already on floor " + manager.CurrentFloor + ".");

        if (!manager.TryTransitionToFloor(targetFloor, out string message))
            return DeveloperConsoleCommandResult.Error(message);

        return DeveloperConsoleCommandResult.Success(message);
    }

    private void WarnMissing(string dependencyName)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning("[DeveloperConsoleCommandExecutor] Missing dependency: " + dependencyName, this);
#endif
    }
}
