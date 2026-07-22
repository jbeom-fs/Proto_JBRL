using System.Collections.Generic;
using System.Globalization;
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
    private SoulEnhancementTable _soulEnhancementTable;
    private ItemDatabase _itemDatabase;

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

    public DeveloperConsoleCommandResult ExecuteEnhanceCommon(
        SoulStatType stat,
        IReadOnlyList<SoulCommonEnhancement.ShardAllocation> allocations)
    {
        PlayerSoulEnhancements enhancements = ResolvePlayerSoulEnhancements();
        PlayerInventory inventory = ResolvePlayerInventory();
        SoulEnhancementTable table = ResolveSoulEnhancementTable();
        ItemDatabase itemDatabase = ResolveItemDatabase();

        if (enhancements == null)
            return DeveloperConsoleCommandResult.Error("PlayerSoulEnhancements is not active.");
        if (inventory == null)
            return DeveloperConsoleCommandResult.Error("PlayerInventory is not active.");
        if (table == null)
            return DeveloperConsoleCommandResult.Error("SoulEnhancementTable is not loaded.");

        int currentLevel = enhancements.GetLevel(PlayerFormId.Normal, stat);
        int cost = table.TryGetGrowth(PlayerFormId.Normal, stat, out SoulStatGrowth growth)
            ? SoulEnhancementCost.GetMaterialCost(growth, currentLevel)
            : 0;
        SoulCommonEnhancement.Result result = SoulCommonEnhancement.TryEnhance(
            table, enhancements, inventory, itemDatabase, stat, allocations);

        if (result == SoulCommonEnhancement.Result.Success)
        {
            int level = enhancements.GetLevel(PlayerFormId.Normal, stat);
            return DeveloperConsoleCommandResult.Success(
                "Enhanced common/" + stat + " (now level " + level + ", cost=" + cost + ").");
        }

        return DeveloperConsoleCommandResult.Error(
            "Common enhancement failed: " + result + " (cost=" + cost + ").");
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

    public DeveloperConsoleCommandResult ExecutePassiveGive(int catalogIndex)
    {
        if (!TryResolveEngravingContext(out EngravingLoadout loadout, out PlayerCombatController combat))
            return DeveloperConsoleCommandResult.Error("EngravingLoadout or PlayerCombatController is not active.");

        WeaponData weapon = combat.currentWeapon;
        if (weapon == null ||
            weapon.passiveEngravings == null ||
            (uint)catalogIndex >= (uint)weapon.passiveEngravings.Count)
        {
            return DeveloperConsoleCommandResult.Error("Invalid passive catalog index.");
        }

        PlayerFormId form = combat.CurrentFormId;
        PassiveEngravingData passive = weapon.passiveEngravings[catalogIndex];
        if (!loadout.AddPassiveToPool(form, passive))
        {
            return DeveloperConsoleCommandResult.Error(
                "Passive engraving is missing or locked to another form (cannot add to " + form + " pool).");
        }

        return DeveloperConsoleCommandResult.Success(
            "Gave " + GetPassiveName(passive) + " to " + form +
            " passive pool (size " + loadout.PassivePoolCount(form) + ").");
    }

    public DeveloperConsoleCommandResult ExecutePassiveEquip(int slot, int poolIndex)
    {
        if (!IsValidPassiveSlot(slot))
        {
            return DeveloperConsoleCommandResult.Error(
                "Invalid slot: must be 0-" + (EngravingLoadout.PassiveSlotCount - 1) + ".");
        }

        if (!TryResolveEngravingContext(out EngravingLoadout loadout, out PlayerCombatController combat))
            return DeveloperConsoleCommandResult.Error("EngravingLoadout or PlayerCombatController is not active.");

        PlayerFormId form = combat.CurrentFormId;
        PassiveEngravingData passive = loadout.GetPassivePoolAt(form, poolIndex);
        if (passive == null || !loadout.EquipPassive(form, slot, poolIndex))
            return DeveloperConsoleCommandResult.Error("Invalid slot/pool index.");

        return DeveloperConsoleCommandResult.Success(
            "Equipped " + GetPassiveName(passive) + " to " + form + " passive slot " + slot + ".");
    }

    public DeveloperConsoleCommandResult ExecutePassiveUnequip(int slot)
    {
        if (!IsValidPassiveSlot(slot))
        {
            return DeveloperConsoleCommandResult.Error(
                "Invalid slot: must be 0-" + (EngravingLoadout.PassiveSlotCount - 1) + ".");
        }

        if (!TryResolveEngravingContext(out EngravingLoadout loadout, out PlayerCombatController combat))
            return DeveloperConsoleCommandResult.Error("EngravingLoadout or PlayerCombatController is not active.");

        PlayerFormId form = combat.CurrentFormId;
        PassiveEngravingData passive = loadout.GetPassiveSlot(form, slot);
        if (!loadout.UnequipPassive(form, slot))
            return DeveloperConsoleCommandResult.Error("Slot empty or invalid.");

        return DeveloperConsoleCommandResult.Success(
            "Unequipped " + GetPassiveName(passive) + " from " + form + " passive slot " + slot + ".");
    }

    public DeveloperConsoleCommandResult ExecutePassiveShow()
    {
        if (!TryResolveEngravingContext(out EngravingLoadout loadout, out PlayerCombatController combat))
            return DeveloperConsoleCommandResult.Error("EngravingLoadout or PlayerCombatController is not active.");

        PlayerFormId form = combat.CurrentFormId;
        StringBuilder builder = new StringBuilder();
        builder.Append("Passive ");
        builder.Append(form);
        builder.Append(" slots: ");
        for (int i = 0; i < EngravingLoadout.PassiveSlotCount; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append('[');
            builder.Append(i);
            builder.Append("] ");
            builder.Append(GetPassiveName(loadout.GetPassiveSlot(form, i)));
        }

        builder.Append(" | pool: ");
        AppendPassiveList(builder, loadout, form);
        builder.Append(" | catalog: ");
        AppendPassiveCatalog(builder, combat.currentWeapon);
        return DeveloperConsoleCommandResult.Success(builder.ToString());
    }

    public DeveloperConsoleCommandResult ExecuteComboShow()
    {
        PlayerCombatController combat = ResolvePlayerCombatController();
        if (combat == null)
            return DeveloperConsoleCommandResult.Error("PlayerCombatController is not active.");

        return DeveloperConsoleCommandResult.Success(FormatComboState(combat));
    }

    public DeveloperConsoleCommandResult ExecuteComboAdd(int amount)
    {
        PlayerCombatController combat = ResolvePlayerCombatController();
        if (combat == null)
            return DeveloperConsoleCommandResult.Error("PlayerCombatController is not active.");

        if (!combat.AddComboStacks(amount))
            return DeveloperConsoleCommandResult.Error("Combo meter is not available.");

        return DeveloperConsoleCommandResult.Success(
            "Added " + amount + " combo stack(s). " + FormatComboState(combat));
    }

    public DeveloperConsoleCommandResult ExecuteAilment(AilmentType type, float tickDamage, float duration)
    {
        PlayerController activePlayer = PlayerController.Active;
        if (activePlayer == null)
            return DeveloperConsoleCommandResult.Error("PlayerController.Active is missing.");

        EnemyController target = FindNearestAliveEnemy(activePlayer.transform.position);
        if (target == null)
            return DeveloperConsoleCommandResult.Error("No alive enemy found.");

        target.ApplyAilment(type, tickDamage, duration);
        int stacks = target.GetAilmentStacks(type);
        return DeveloperConsoleCommandResult.Success(
            "Applied " + GetAilmentToken(type) + " to " + GetEnemyDisplayName(target) + " (stacks " + stacks + ").");
    }

    public DeveloperConsoleCommandResult ExecuteStun(float duration)
    {
        PlayerController activePlayer = PlayerController.Active;
        if (activePlayer == null)
            return DeveloperConsoleCommandResult.Error("PlayerController.Active is missing.");

        EnemyController target = FindNearestAliveEnemy(activePlayer.transform.position);
        if (target == null)
            return DeveloperConsoleCommandResult.Error("No alive enemy found.");

        target.ApplyStun(duration);
        return DeveloperConsoleCommandResult.Success(
            "Applied stun to " + GetEnemyDisplayName(target) + " (" + duration + "s).");
    }

    public DeveloperConsoleCommandResult ExecuteZone(
        int tickDamage,
        float duration,
        float slowPercentage,
        float slowDuration)
    {
        PlayerCombatController combat = ResolvePlayerCombatController();
        if (combat == null)
            return DeveloperConsoleCommandResult.Error("PlayerCombatController is not active.");
        if (DamageZoneSpawner.Instance == null)
            return DeveloperConsoleCommandResult.Error("DamageZoneSpawner is not active.");

        IReadOnlyList<AilmentApplication> bonuses = combat.BonusAttackAilments;
        AilmentApplication[] ailments = null;
        if (bonuses != null && bonuses.Count > 0)
        {
            ailments = new AilmentApplication[bonuses.Count];
            for (int i = 0; i < bonuses.Count; i++)
                ailments[i] = bonuses[i];
        }

        ZonePayload payload = new ZonePayload(
            tickDamage,
            duration,
            slowPercentage,
            slowDuration,
            ailments,
            combat.AilmentDamageMultiplier,
            1f,
            0.5f);
        DamageZoneSpawner.Instance.SpawnZone(
            combat.transform.position,
            null,
            in payload);
        return DeveloperConsoleCommandResult.Success(
            "Spawned damage zone (damage " + tickDamage + ", duration " + duration + "s).");
    }

    public DeveloperConsoleCommandResult ExecuteProc(int slotIndex)
    {
        PlayerCombatController combat = ResolvePlayerCombatController();
        if (combat == null)
            return DeveloperConsoleCommandResult.Error("PlayerCombatController is not active.");

        SkillData skill = combat.GetSkillData(slotIndex);
        if (skill == null)
            return DeveloperConsoleCommandResult.Error("Skill slot " + slotIndex + " is empty.");

        if (!combat.ExecuteSkillProc(
                skill,
                combat.transform.position,
                combat.CurrentAimDirection))
        {
            return DeveloperConsoleCommandResult.Error(
                "Proc rejected or failed for " + GetSkillName(skill) + ".");
        }

        return DeveloperConsoleCommandResult.Success(
            "Proc executed: " + GetSkillName(skill) + ".");
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

    private SoulEnhancementTable ResolveSoulEnhancementTable()
    {
        if (_soulEnhancementTable != null)
            return _soulEnhancementTable;

        SoulEnhancementTable[] tables = Resources.FindObjectsOfTypeAll<SoulEnhancementTable>();
        if (tables.Length > 0)
            _soulEnhancementTable = tables[0];

        return _soulEnhancementTable;
    }

    private ItemDatabase ResolveItemDatabase()
    {
        if (_itemDatabase != null)
            return _itemDatabase;

        ItemDatabase[] databases = Resources.FindObjectsOfTypeAll<ItemDatabase>();
        if (databases.Length > 0)
            _itemDatabase = databases[0];

        return _itemDatabase;
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

    private static EnemyController FindNearestAliveEnemy(Vector3 origin)
    {
        EnemyController[] enemies = UnityEngine.Object.FindObjectsByType<EnemyController>();
        EnemyController nearest = null;
        float nearestSqrDistance = float.PositiveInfinity;

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyController enemy = enemies[i];
            if (enemy == null || enemy.IsDead || !enemy.IsAlive || !enemy.isActiveAndEnabled)
                continue;

            float sqrDistance = (enemy.transform.position - origin).sqrMagnitude;
            if (sqrDistance >= nearestSqrDistance)
                continue;

            nearest = enemy;
            nearestSqrDistance = sqrDistance;
        }

        return nearest;
    }

    private static bool IsValidEngravingSlot(int slot)
    {
        return (uint)slot < (uint)EngravingLoadout.SlotCount;
    }

    private static bool IsValidPassiveSlot(int slot)
    {
        return (uint)slot < (uint)EngravingLoadout.PassiveSlotCount;
    }

    private static string GetSkillName(SkillData skill)
    {
        if (skill == null)
            return "(empty)";

        string skillName = string.IsNullOrWhiteSpace(skill.skillName) ? skill.name : skill.skillName;
        return skill is EngravingData engraving ? skillName + " [" + engraving.grade + "]" : skillName;
    }

    private static string GetPassiveName(PassiveEngravingData passive)
    {
        if (passive == null)
            return "(empty)";

        string passiveName = string.IsNullOrWhiteSpace(passive.passiveName)
            ? passive.name
            : passive.passiveName;
        return passiveName + " [" + passive.grade + "]";
    }

    private static void AppendPassiveList(
        StringBuilder builder,
        EngravingLoadout loadout,
        PlayerFormId form)
    {
        int poolCount = loadout.PassivePoolCount(form);
        if (poolCount == 0)
        {
            builder.Append("(empty)");
            return;
        }

        for (int i = 0; i < poolCount; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append(i);
            builder.Append(':');
            builder.Append(GetPassiveName(loadout.GetPassivePoolAt(form, i)));
        }
    }

    private static void AppendPassiveCatalog(StringBuilder builder, WeaponData weapon)
    {
        if (weapon == null || weapon.passiveEngravings == null || weapon.passiveEngravings.Count == 0)
        {
            builder.Append("(empty)");
            return;
        }

        for (int i = 0; i < weapon.passiveEngravings.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append(i);
            builder.Append(':');
            builder.Append(GetPassiveName(weapon.passiveEngravings[i]));
        }
    }

    private static string GetEnemyDisplayName(EnemyController enemy)
    {
        if (enemy == null)
            return "(missing)";

        if (enemy.data != null && !string.IsNullOrWhiteSpace(enemy.data.enemyName))
            return enemy.data.enemyName;

        return enemy.name;
    }

    private static string FormatComboState(PlayerCombatController combat)
    {
        return "Combo tier " + combat.CurrentComboTier +
               " | progress " + combat.CurrentComboProgress +
               " | total " + combat.CurrentComboStack +
               " | window " + combat.ComboWindowRemaining.ToString("0.00", CultureInfo.InvariantCulture) +
               "s (" + (combat.ComboWindowRemainingNormalized * 100f).ToString("0", CultureInfo.InvariantCulture) +
               "%) | multiplier x" + combat.CurrentComboDamageMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string GetAilmentToken(AilmentType type)
    {
        return type == AilmentType.Bleed ? "bleed" : "poison";
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
