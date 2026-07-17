using UnityEngine;
using UnityEngine.InputSystem;

public enum PlayerControlScheme
{
    ClassicKeyboard,
    ActionMouseAim
}

public enum PointerButton
{
    None,
    Left,
    Right,
    Middle
}

[System.Serializable]
public struct InputBinding
{
    public Key key;
    public PointerButton mouseButton;

    public InputBinding(Key key, PointerButton mouseButton = PointerButton.None)
    {
        this.key = key;
        this.mouseButton = mouseButton;
    }

    public bool HasKey => key != Key.None;
    public bool HasMouseButton => mouseButton != PointerButton.None;
}

[CreateAssetMenu(fileName = "PlayerInputKeySettings", menuName = "JBRogLike/Input/Player Input Key Settings")]
public sealed class PlayerInputKeySettings : ScriptableObject
{
    [Header("Scheme")]
    public PlayerControlScheme controlScheme = PlayerControlScheme.ClassicKeyboard;

    [Header("Move")]
    public Key up = Key.UpArrow;
    public Key down = Key.DownArrow;
    public Key left = Key.LeftArrow;
    public Key right = Key.RightArrow;

    [Header("Actions")]
    public Key interactConfirm = Key.Z;
    public Key inventory = Key.I;
    public Key basicAttack = Key.X;
    public Key reload = Key.A;
    public Key dodge = Key.Space;

    [Header("Skills")]
    public Key skillSlot1 = Key.Q;
    public Key skillSlot2 = Key.W;
    public Key skillSlot3 = Key.E;
    public Key skillSlot4 = Key.R;

    [Header("Action Mouse Aim Move")]
    public Key actionMouseUp = Key.W;
    public Key actionMouseDown = Key.S;
    public Key actionMouseLeft = Key.A;
    public Key actionMouseRight = Key.D;

    [Header("Action Mouse Aim Actions")]
    public InputBinding actionMouseBasicAttack = new InputBinding(Key.None, PointerButton.Left);
    public Key actionMouseReload = Key.Tab;
    public InputBinding actionMouseDodge = new InputBinding(Key.Space);

    [Header("Action Mouse Aim Skills")]
    public InputBinding actionMouseSkillSlot1 = new InputBinding(Key.None, PointerButton.Right);
    public InputBinding actionMouseSkillSlot2 = new InputBinding(Key.Q);
    public InputBinding actionMouseSkillSlot3 = new InputBinding(Key.E);
    public InputBinding actionMouseSkillSlot4 = new InputBinding(Key.Digit4);

    // Skill slots keep existing 0-based WeaponData.skills order.
    public Key GetSkillSlotKey(int slotIndex)
    {
        if (controlScheme == PlayerControlScheme.ActionMouseAim)
            return GetSkillSlotBinding(slotIndex).key;

        switch (slotIndex)
        {
            case 0: return skillSlot1;
            case 1: return skillSlot2;
            case 2: return skillSlot3;
            case 3: return skillSlot4;
            default: return Key.None;
        }
    }

    public Key GetMoveUpKey() => controlScheme == PlayerControlScheme.ActionMouseAim ? actionMouseUp : up;
    public Key GetMoveDownKey() => controlScheme == PlayerControlScheme.ActionMouseAim ? actionMouseDown : down;
    public Key GetMoveLeftKey() => controlScheme == PlayerControlScheme.ActionMouseAim ? actionMouseLeft : left;
    public Key GetMoveRightKey() => controlScheme == PlayerControlScheme.ActionMouseAim ? actionMouseRight : right;
    public Key GetReloadKey() => controlScheme == PlayerControlScheme.ActionMouseAim ? actionMouseReload : reload;

    public InputBinding GetBasicAttackBinding()
        => controlScheme == PlayerControlScheme.ActionMouseAim
            ? actionMouseBasicAttack
            : new InputBinding(basicAttack);

    public InputBinding GetDodgeBinding()
        => controlScheme == PlayerControlScheme.ActionMouseAim
            ? actionMouseDodge
            : new InputBinding(dodge);

    public InputBinding GetSkillSlotBinding(int slotIndex)
    {
        if (controlScheme != PlayerControlScheme.ActionMouseAim)
            return new InputBinding(GetClassicSkillSlotKey(slotIndex));

        switch (slotIndex)
        {
            case 0: return actionMouseSkillSlot1;
            case 1: return actionMouseSkillSlot2;
            case 2: return actionMouseSkillSlot3;
            case 3: return actionMouseSkillSlot4;
            default: return new InputBinding(Key.None);
        }
    }

    private Key GetClassicSkillSlotKey(int slotIndex)
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
        ValidateClassicKeyboardBindings();
        ValidateActionMouseAimBindings();
#endif
    }

    private void ValidateClassicKeyboardBindings()
    {
        Key[] keys =
        {
            up, down, left, right,
            interactConfirm, inventory, basicAttack, reload, dodge,
            skillSlot1, skillSlot2, skillSlot3, skillSlot4,
        };
        string[] names =
        {
            nameof(up), nameof(down), nameof(left), nameof(right),
            nameof(interactConfirm), nameof(inventory), nameof(basicAttack), nameof(reload), nameof(dodge),
            nameof(skillSlot1), nameof(skillSlot2), nameof(skillSlot3), nameof(skillSlot4),
        };

        for (int i = 0; i < keys.Length; i++)
            ValidateKey(keys[i], names[i]);

        for (int i = 0; i < keys.Length - 1; i++)
            for (int j = i + 1; j < keys.Length; j++)
                WarnIfDuplicate(keys[i], names[i], keys[j], names[j]);
    }

    private void ValidateActionMouseAimBindings()
    {
        Key[] keys =
        {
            actionMouseUp, actionMouseDown, actionMouseLeft, actionMouseRight,
            interactConfirm, inventory, actionMouseReload,
        };
        string[] names =
        {
            nameof(actionMouseUp), nameof(actionMouseDown), nameof(actionMouseLeft), nameof(actionMouseRight),
            nameof(interactConfirm), nameof(inventory), nameof(actionMouseReload),
        };

        for (int i = 0; i < keys.Length; i++)
            ValidateKey(keys[i], names[i]);

        InputBinding[] bindings =
        {
            actionMouseBasicAttack,
            actionMouseDodge,
            actionMouseSkillSlot1,
            actionMouseSkillSlot2,
            actionMouseSkillSlot3,
            actionMouseSkillSlot4,
        };
        string[] bindingNames =
        {
            nameof(actionMouseBasicAttack),
            nameof(actionMouseDodge),
            nameof(actionMouseSkillSlot1),
            nameof(actionMouseSkillSlot2),
            nameof(actionMouseSkillSlot3),
            nameof(actionMouseSkillSlot4),
        };

        for (int i = 0; i < bindings.Length; i++)
            ValidateBinding(bindings[i], bindingNames[i]);

        for (int i = 0; i < keys.Length - 1; i++)
            for (int j = i + 1; j < keys.Length; j++)
                WarnIfDuplicate(keys[i], names[i], keys[j], names[j]);

        for (int i = 0; i < bindings.Length; i++)
        {
            if (bindings[i].HasKey)
            {
                for (int j = 0; j < keys.Length; j++)
                    WarnIfDuplicate(bindings[i].key, bindingNames[i], keys[j], names[j]);
            }

            for (int j = i + 1; j < bindings.Length; j++)
                WarnIfDuplicate(bindings[i], bindingNames[i], bindings[j], bindingNames[j]);
        }
    }

    private void ValidateBinding(InputBinding binding, string actionName)
    {
        if (!binding.HasKey && !binding.HasMouseButton)
            Debug.LogWarning("[PlayerInputKeySettings] " + actionName + " has no input binding.", this);

        if (binding.HasKey && binding.HasMouseButton)
            Debug.LogWarning("[PlayerInputKeySettings] " + actionName + " has both key and mouse button bindings.", this);
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

    private void WarnIfDuplicate(InputBinding a, string aName, InputBinding b, string bName)
    {
        WarnIfDuplicate(a.key, aName, b.key, bName);

        if (a.mouseButton != PointerButton.None && a.mouseButton == b.mouseButton)
            Debug.LogWarning("[PlayerInputKeySettings] Duplicate mouse button '" + a.mouseButton + "' for " + aName + " and " + bName + ".", this);
    }
}
