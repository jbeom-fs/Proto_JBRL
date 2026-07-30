using UnityEngine;

public sealed class PassiveHudManager : MonoBehaviour
{
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private EngravingLoadout loadout;
    [SerializeField] private CombatEventChannel combatChannel;
    [SerializeField] private DungeonEventChannel dungeonChannel;
    [SerializeField] private PassiveHudSlotUI slot;

    private void OnEnable()
    {
        if (loadout != null)
            loadout.OnPassiveChanged += RefreshAll;
        if (combatChannel != null)
            combatChannel.OnLoadoutChanged += RefreshAll;
        if (dungeonChannel != null)
            dungeonChannel.OnFloorChanged += HandleFloorChanged;
    }

    private void OnDisable()
    {
        if (loadout != null)
            loadout.OnPassiveChanged -= RefreshAll;
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
        if (slot == null)
            return;

        PlayerFormId form = combat != null ? combat.CurrentFormId : PlayerFormId.Normal;
        PassiveEngravingData passive = loadout != null
            ? loadout.GetPassive(form)
            : null;
        if (passive != null &&
            EngravingDisplayInfo.TryCreate(passive, out EngravingDisplayInfo info))
        {
            slot.gameObject.SetActive(true);
            slot.BindEngraving(info);
        }
        else
        {
            slot.gameObject.SetActive(false);
        }
    }
}
