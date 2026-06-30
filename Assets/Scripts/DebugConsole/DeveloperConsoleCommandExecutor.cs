using System.Collections.Generic;
using System.Text;
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
    [SerializeField] private PlayerSoulEnhancements playerSoulEnhancements;
    [SerializeField] private EngravingLoadout engravingLoadout;
    [SerializeField] private PlayerCombatController playerCombatController;
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

    public DeveloperConsoleCommandResult ExecuteEnhance(PlayerFormId form, SoulStatType stat, int count)
    {
        PlayerSoulEnhancements enhancements = ResolvePlayerSoulEnhancements();
        if (enhancements == null)
        {
            WarnMissing(nameof(PlayerSoulEnhancements));
            return DeveloperConsoleCommandResult.Error("PlayerSoulEnhancements is not active.");
        }

        if (count <= 0)
            return DeveloperConsoleCommandResult.Error("Usage: /enhance <form> <stat> [positiveCount]");

        enhancements.AddLevel(form, stat, count);
        int level = enhancements.GetLevel(form, stat);
        return DeveloperConsoleCommandResult.Success(
            "Enhanced " + form + "/" + stat + " by " + count + " (now level " + level + ").");
    }

    public DeveloperConsoleCommandResult ExecuteEngravingGive(string formToken, string itemCode)
    {
        if (!System.Enum.TryParse(formToken, true, out PlayerFormId form))
            return DeveloperConsoleCommandResult.Error("Unknown form: " + formToken);

        PlayerInventory inventory = ResolvePlayerInventory();
        if (inventory == null)
        {
            WarnMissing(nameof(PlayerInventory));
            return DeveloperConsoleCommandResult.Error("PlayerInventory is not active.");
        }

        if (!inventory.TryGetDatabaseItem(itemCode, out ItemData item))
            return DeveloperConsoleCommandResult.Error("Unknown itemCode: " + itemCode);

        EngravingData engraving = item.Engraving;
        if (engraving == null)
            return DeveloperConsoleCommandResult.Error("Item is not an engraving: " + itemCode);

        EngravingLoadout loadout = ResolveEngravingLoadout();
        if (loadout == null)
            return DeveloperConsoleCommandResult.Error("EngravingLoadout is not active.");

        if (!loadout.AddToPool(form, engraving))
            return DeveloperConsoleCommandResult.Error(
                "Engraving is locked to another form (cannot add to " + form + " pool).");

        return DeveloperConsoleCommandResult.Success(
            "Gave " + GetSkillName(engraving) + " to " + form + " pool (size " + loadout.PoolCount(form) + ").");
    }

    public DeveloperConsoleCommandResult ExecuteEngravingEquip(int slot, int poolIndex)
    {
        if (!IsValidEngravingSlot(slot))
            return DeveloperConsoleCommandResult.Error("Invalid slot: must be 0-" + (EngravingLoadout.SlotCount - 1) + ".");

        if (!TryResolveEngravingContext(out EngravingLoadout loadout, out PlayerCombatController combat))
            return DeveloperConsoleCommandResult.Error("EngravingLoadout or PlayerCombatController is not active.");

        PlayerFormId form = combat.CurrentFormId;
        SkillData skill = loadout.GetPoolAt(form, poolIndex);
        if (skill == null || !loadout.Equip(form, slot, poolIndex))
            return DeveloperConsoleCommandResult.Error("Invalid slot/pool index.");

        return DeveloperConsoleCommandResult.Success(
            "Equipped " + GetSkillName(skill) + " to " + form + " slot " + slot + ".");
    }

    public DeveloperConsoleCommandResult ExecuteEngravingUnequip(int slot)
    {
        if (!IsValidEngravingSlot(slot))
            return DeveloperConsoleCommandResult.Error("Invalid slot: must be 0-" + (EngravingLoadout.SlotCount - 1) + ".");

        if (!TryResolveEngravingContext(out EngravingLoadout loadout, out PlayerCombatController combat))
            return DeveloperConsoleCommandResult.Error("EngravingLoadout or PlayerCombatController is not active.");

        PlayerFormId form = combat.CurrentFormId;
        SkillData skill = loadout.GetSlot(form, slot);
        if (!loadout.Unequip(form, slot))
            return DeveloperConsoleCommandResult.Error("Slot empty or invalid.");

        return DeveloperConsoleCommandResult.Success(
            "Unequipped " + GetSkillName(skill) + " from " + form + " slot " + slot + ".");
    }

    public DeveloperConsoleCommandResult ExecuteEngravingShow()
    {
        if (!TryResolveEngravingContext(out EngravingLoadout loadout, out PlayerCombatController combat))
            return DeveloperConsoleCommandResult.Error("EngravingLoadout or PlayerCombatController is not active.");

        PlayerFormId form = combat.CurrentFormId;

        StringBuilder builder = new StringBuilder();
        builder.Append("Engraving ");
        builder.Append(form);
        builder.Append(" slots: ");
        for (int i = 0; i < EngravingLoadout.SlotCount; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append('[');
            builder.Append(i);
            builder.Append("] ");
            builder.Append(GetSkillName(loadout.GetSlot(form, i)));
        }

        builder.Append(" | pool: ");
        int poolCount = loadout.PoolCount(form);
        if (poolCount == 0)
        {
            builder.Append("(empty)");
        }
        else
        {
            for (int i = 0; i < poolCount; i++)
            {
                if (i > 0)
                    builder.Append(", ");

                builder.Append(i);
                builder.Append(':');
                builder.Append(GetSkillName(loadout.GetPoolAt(form, i)));
            }
        }

        return DeveloperConsoleCommandResult.Success(builder.ToString());
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

    private PlayerSoulEnhancements ResolvePlayerSoulEnhancements()
    {
        if (playerSoulEnhancements != null)
            return playerSoulEnhancements;

        if (player != null && player.TryGetComponent(out playerSoulEnhancements))
            return playerSoulEnhancements;

        PlayerController activePlayer = PlayerController.Active;
        if (activePlayer != null && activePlayer.TryGetComponent(out playerSoulEnhancements))
            return playerSoulEnhancements;

        playerSoulEnhancements = UnityEngine.Object.FindAnyObjectByType<PlayerSoulEnhancements>();
        return playerSoulEnhancements;
    }

    private PlayerCombatController ResolvePlayerCombatController()
    {
        if (playerCombatController != null)
            return playerCombatController;

        if (player != null && player.TryGetComponent(out playerCombatController))
            return playerCombatController;

        PlayerController activePlayer = PlayerController.Active;
        if (activePlayer != null && activePlayer.TryGetComponent(out playerCombatController))
            return playerCombatController;

        if (PlayerCombatController.Active != null)
        {
            playerCombatController = PlayerCombatController.Active;
            return playerCombatController;
        }

        playerCombatController = UnityEngine.Object.FindAnyObjectByType<PlayerCombatController>();
        return playerCombatController;
    }

    private EngravingLoadout ResolveEngravingLoadout()
    {
        if (engravingLoadout != null)
            return engravingLoadout;

        if (EngravingLoadout.Active != null)
        {
            engravingLoadout = EngravingLoadout.Active;
            return engravingLoadout;
        }

        if (player != null && player.TryGetComponent(out engravingLoadout))
            return engravingLoadout;

        PlayerController activePlayer = PlayerController.Active;
        if (activePlayer != null && activePlayer.TryGetComponent(out engravingLoadout))
            return engravingLoadout;

        engravingLoadout = UnityEngine.Object.FindAnyObjectByType<EngravingLoadout>();
        return engravingLoadout;
    }

    private bool TryResolveEngravingContext(out EngravingLoadout loadout, out PlayerCombatController combat)
    {
        combat = ResolvePlayerCombatController();
        loadout = ResolveEngravingLoadout();

        if (loadout == null && combat != null)
            loadout = combat.GetComponent<EngravingLoadout>();

        if (loadout == null && player != null)
            loadout = player.GetComponent<EngravingLoadout>();

        if (combat == null && loadout != null)
            combat = loadout.GetComponent<PlayerCombatController>();

        engravingLoadout = loadout;
        playerCombatController = combat;
        return loadout != null && combat != null;
    }

    private static bool IsValidEngravingSlot(int slot)
    {
        return (uint)slot < (uint)EngravingLoadout.SlotCount;
    }

    private static string GetSkillName(SkillData skill)
    {
        if (skill == null)
            return "(empty)";

        string skillName = string.IsNullOrWhiteSpace(skill.skillName) ? skill.name : skill.skillName;
        return skill is EngravingData engraving ? skillName + " [" + engraving.grade + "]" : skillName;
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
