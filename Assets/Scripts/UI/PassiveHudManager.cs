using UnityEngine;

public sealed class PassiveHudManager : MonoBehaviour
{
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private EngravingLoadout loadout;
    [SerializeField] private PlayerPassiveSlotUnlocks passiveUnlocks;
    [SerializeField] private CombatEventChannel combatChannel;
    [SerializeField] private DungeonEventChannel dungeonChannel;
    [SerializeField] private PassiveHudSlotUI[] slots = new PassiveHudSlotUI[EngravingLoadout.PassiveSlotCount];
    [SerializeField] private Sprite emptySlotSprite;
    [SerializeField] private Sprite lockSprite;

    private void OnEnable()
    {
        if (loadout != null)
            loadout.OnPassiveChanged += RefreshAll;
        if (passiveUnlocks != null)
            passiveUnlocks.OnChanged += RefreshAll;
        if (combatChannel != null)
            combatChannel.OnLoadoutChanged += RefreshAll;
        if (dungeonChannel != null)
            dungeonChannel.OnFloorChanged += HandleFloorChanged;
    }

    private void OnDisable()
    {
        if (loadout != null)
            loadout.OnPassiveChanged -= RefreshAll;
        if (passiveUnlocks != null)
            passiveUnlocks.OnChanged -= RefreshAll;
        if (combatChannel != null)
            combatChannel.OnLoadoutChanged -= RefreshAll;
        if (dungeonChannel != null)
            dungeonChannel.OnFloorChanged -= HandleFloorChanged;
    }

    private void Start()
    {
        RefreshAll();
    }

    private void HandleFloorChanged(int previousFloor, int newFloor)
    {
        RefreshAll();
    }

    private void RefreshAll()
    {
        if (slots == null)
            return;

        PlayerFormId form = combat != null ? combat.CurrentFormId : PlayerFormId.Normal;
        int unlockCount = passiveUnlocks != null
            ? passiveUnlocks.GetUnlockCount(form)
            : 1;

        for (int i = 0; i < slots.Length; i++)
        {
            PassiveHudSlotUI slot = slots[i];
            if (slot == null)
                continue;

            if (i >= unlockCount)
            {
                slot.BindLocked(lockSprite);
                continue;
            }

            PassiveEngravingData passive = loadout != null
                ? loadout.GetPassiveSlot(form, i)
                : null;
            if (passive != null &&
                EngravingDisplayInfo.TryCreate(passive, out EngravingDisplayInfo info))
            {
                slot.BindEngraving(info);
            }
            else
            {
                slot.BindEmpty(emptySlotSprite);
            }
        }
    }
}
