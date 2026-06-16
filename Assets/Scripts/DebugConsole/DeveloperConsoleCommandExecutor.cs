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
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerFormController playerFormController;
    [SerializeField] private TeleportDestinationDatabase teleportDestinationDatabase;

    private readonly List<string> _itemCodeFilterBuffer = new List<string>(32);

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

        switch (playerFormController.TrySwitchForm(id))
        {
            case FormSwitchResult.Switched:
                return DeveloperConsoleCommandResult.Success("Form switched to " + id);
            case FormSwitchResult.AlreadyActive:
                return DeveloperConsoleCommandResult.Success("Already in form " + id);
            case FormSwitchResult.NoDatabase:
                return DeveloperConsoleCommandResult.Error("Form database is not assigned on PlayerFormController.");
            case FormSwitchResult.UnknownForm:
                return DeveloperConsoleCommandResult.Error("Form not registered in database: " + id);
            case FormSwitchResult.NotOwned:
                return DeveloperConsoleCommandResult.Error("Form locked: soul not owned for " + id);
            case FormSwitchResult.Busy:
                return DeveloperConsoleCommandResult.Error("Form switch blocked: player is busy/dashing/stunned.");
            default:
                return DeveloperConsoleCommandResult.Error("Form switch failed.");
        }
    }

    public DeveloperConsoleCommandResult ExecuteItemGive(string itemCode, int count)
    {
        PlayerInventory inventory = ResolvePlayerInventory();
        if (inventory == null)
        {
            WarnMissing(nameof(PlayerInventory));
            return DeveloperConsoleCommandResult.Error("PlayerInventory is not active.");
        }

        if (string.IsNullOrWhiteSpace(itemCode))
            return DeveloperConsoleCommandResult.Error("Usage: /give <category> <code> [count]");

        if (count <= 0)
            return DeveloperConsoleCommandResult.Error("Usage: /give <category> <code> [positiveCount]");

        if (!inventory.TryGetDatabaseItem(itemCode, out ItemData item))
            return DeveloperConsoleCommandResult.Error("Unknown itemCode: " + itemCode);

        if (!inventory.AddItem(item, count))
            return DeveloperConsoleCommandResult.Error("Failed to add item: " + itemCode);

        return DeveloperConsoleCommandResult.Success("Gave " + count + " x " + item.ItemCode + ".");
    }

    public void GetFormIds(List<string> output)
    {
        if (output == null)
            return;

        foreach (string name in System.Enum.GetNames(typeof(PlayerFormId)))
            output.Add(name);
    }

    public void GetItemCodes(List<string> output)
    {
        if (output == null)
            return;

        ResolvePlayerInventory()?.GetDatabaseItemCodes(output);
    }

    public void GetItemCodes(ItemType type, List<string> output)
    {
        if (output == null)
            return;

        PlayerInventory inventory = ResolvePlayerInventory();
        if (inventory == null)
            return;

        _itemCodeFilterBuffer.Clear();
        inventory.GetDatabaseItemCodes(_itemCodeFilterBuffer);
        for (int i = 0; i < _itemCodeFilterBuffer.Count; i++)
        {
            string itemCode = _itemCodeFilterBuffer[i];
            if (inventory.TryGetDatabaseItem(itemCode, out ItemData item) &&
                item != null &&
                item.ItemType == type)
            {
                output.Add(itemCode);
            }
        }

        _itemCodeFilterBuffer.Clear();
    }

    public bool TryGetItemType(string itemCode, out ItemType type)
    {
        type = default;

        PlayerInventory inventory = ResolvePlayerInventory();
        if (inventory == null || string.IsNullOrWhiteSpace(itemCode))
            return false;

        if (!inventory.TryGetDatabaseItem(itemCode, out ItemData item) || item == null)
            return false;

        type = item.ItemType;
        return true;
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

    private PlayerInventory ResolvePlayerInventory()
    {
        if (playerInventory != null)
            return playerInventory;

        if (player != null && player.Inventory != null)
            return player.Inventory;

        PlayerController activePlayer = PlayerController.Active;
        if (activePlayer != null && activePlayer.Inventory != null)
            return activePlayer.Inventory;

        playerInventory = UnityEngine.Object.FindAnyObjectByType<PlayerInventory>();
        return playerInventory;
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
