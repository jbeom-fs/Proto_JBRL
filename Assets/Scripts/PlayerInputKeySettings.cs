using UnityEngine;
using UnityEngine.InputSystem;

[CreateAssetMenu(fileName = "PlayerInputKeySettings", menuName = "JBRogLike/Input/Player Input Key Settings")]
public sealed class PlayerInputKeySettings : ScriptableObject
{
    [Header("Move")]
    public Key up = Key.UpArrow;
    public Key down = Key.DownArrow;
    public Key left = Key.LeftArrow;
    public Key right = Key.RightArrow;

    [Header("Actions")]
    public Key interactConfirm = Key.Z;
    public Key inventory = Key.I;
    public Key openDoorDebug = Key.F10;
    public Key basicAttack = Key.Space;

    [Header("Skills")]
    public Key skillSlot1 = Key.Q;
    public Key skillSlot2 = Key.W;
    public Key skillSlot3 = Key.E;
    public Key skillSlot4 = Key.R;

    // Skill slots use existing 0-based indices: 0=Q, 1=W, 2=E, 3=R.
    public Key GetSkillSlotKey(int slotIndex)
    {
        switch (slotIndex)
        {
            case 0: return skillSlot1;
            case 1: return skillSlot2;
            case 2: return skillSlot3;
            case 3: return skillSlot4;
            default: return Key.None;
        }
    }

    private void OnValidate()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Key[] keys =
        {
            up, down, left, right,
            interactConfirm, inventory, openDoorDebug, basicAttack,
            skillSlot1, skillSlot2, skillSlot3, skillSlot4,
        };
        string[] names =
        {
            nameof(up), nameof(down), nameof(left), nameof(right),
            nameof(interactConfirm), nameof(inventory), nameof(openDoorDebug), nameof(basicAttack),
            nameof(skillSlot1), nameof(skillSlot2), nameof(skillSlot3), nameof(skillSlot4),
        };

        for (int i = 0; i < keys.Length; i++)
            ValidateKey(keys[i], names[i]);

        for (int i = 0; i < keys.Length - 1; i++)
            for (int j = i + 1; j < keys.Length; j++)
                WarnIfDuplicate(keys[i], names[i], keys[j], names[j]);
#endif
    }

    private void ValidateKey(Key key, string actionName)
    {
        if (key == Key.None)
            Debug.LogWarning("[PlayerInputKeySettings] " + actionName + " uses Key.None.", this);
    }

    private void WarnIfDuplicate(Key a, string aName, Key b, string bName)
    {
        if (a != Key.None && a == b)
            Debug.LogWarning("[PlayerInputKeySettings] Duplicate key '" + a + "' for " + aName + " and " + bName + ".", this);
    }
}
