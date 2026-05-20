using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

[DefaultExecutionOrder(-10)]
public class PlayerInputReader : MonoBehaviour
{
    [SerializeField] private PlayerInputKeySettings keySettings;

    public Vector2 MoveInput { get; private set; }
    public bool InteractConfirmPressedThisFrame { get; private set; }
    public bool InventoryPressedThisFrame { get; private set; }
    public bool WasBasicAttackPressed { get; private set; }

    public bool WasStairPressed => InteractConfirmPressedThisFrame;
    public bool IsGamePaused => GamePauseController.IsPaused;
    public bool IsGameplayInputBlocked => GamePauseController.IsPaused || DeveloperConsoleUI.IsOpen || InventoryUIController.IsOpen;

    private readonly bool[] _wasSkillPressed = new bool[4];
    private bool _warnedMissingSettings;

    // Existing skill callers use 0-based slots: 0=Q, 1=W, 2=E, 3=R.
    public bool WasSkillPressed(int slot) => GetSkillSlotPressedThisFrame(slot);

    public bool GetSkillSlotPressedThisFrame(int slotIndex)
        => (uint)slotIndex < 4u && _wasSkillPressed[slotIndex];

    public bool IsBasicAttackHeld
    {
        get
        {
            if (IsGameplayInputBlocked)
                return false;

            Keyboard keyboard = Keyboard.current;
            return keyboard != null && IsPressed(keyboard, GetBasicAttackKey());
        }
    }

    public bool IsSkillHeld(int slot)
    {
        if (IsGameplayInputBlocked)
            return false;

        Keyboard keyboard = Keyboard.current;
        return keyboard != null && IsPressed(keyboard, GetSkillSlotKey(slot));
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            ClearAllFlags();
            return;
        }

        PlayerInputKeySettings settings = ResolveSettings();
        InventoryPressedThisFrame = WasPressedThisFrame(keyboard, settings != null ? settings.inventory : Key.I);

        if (IsGameplayInputBlocked)
        {
            ClearGameplayFlags();
            return;
        }

        float x = 0f;
        float y = 0f;
        if (IsPressed(keyboard, settings != null ? settings.up : Key.UpArrow)) y = 1f;
        if (IsPressed(keyboard, settings != null ? settings.down : Key.DownArrow)) y = -1f;
        if (IsPressed(keyboard, settings != null ? settings.left : Key.LeftArrow)) x = -1f;
        if (IsPressed(keyboard, settings != null ? settings.right : Key.RightArrow)) x = 1f;
        MoveInput = new Vector2(x, y);

        InteractConfirmPressedThisFrame = WasPressedThisFrame(keyboard, settings != null ? settings.interactConfirm : Key.Z);
        WasBasicAttackPressed = WasPressedThisFrame(keyboard, settings != null ? settings.basicAttack : Key.Space);

        for (int i = 0; i < _wasSkillPressed.Length; i++)
            _wasSkillPressed[i] = WasPressedThisFrame(keyboard, settings != null ? settings.GetSkillSlotKey(i) : GetDefaultSkillSlotKey(i));
    }

    private PlayerInputKeySettings ResolveSettings()
    {
        if (keySettings != null)
            return keySettings;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_warnedMissingSettings)
        {
            Debug.LogWarning("[PlayerInputReader] keySettings is not assigned; using built-in default keys.", this);
            _warnedMissingSettings = true;
        }
#endif
        return null;
    }

    private static bool IsPressed(Keyboard keyboard, Key key)
    {
        if (key == Key.None)
            return false;

        KeyControl control = keyboard[key];
        return control != null && control.isPressed;
    }

    private static bool WasPressedThisFrame(Keyboard keyboard, Key key)
    {
        if (key == Key.None)
            return false;

        KeyControl control = keyboard[key];
        return control != null && control.wasPressedThisFrame;
    }

    private Key GetBasicAttackKey()
        => keySettings != null ? keySettings.basicAttack : Key.Space;

    private Key GetSkillSlotKey(int slot)
        => keySettings != null ? keySettings.GetSkillSlotKey(slot) : GetDefaultSkillSlotKey(slot);

    private static Key GetDefaultSkillSlotKey(int slot)
    {
        switch (slot)
        {
            case 0: return Key.Q;
            case 1: return Key.W;
            case 2: return Key.E;
            case 3: return Key.R;
            default: return Key.None;
        }
    }

    private void ClearAllFlags()
    {
        ClearGameplayFlags();
        InventoryPressedThisFrame = false;
    }

    private void ClearGameplayFlags()
    {
        MoveInput = Vector2.zero;
        InteractConfirmPressedThisFrame = false;
        WasBasicAttackPressed = false;
        _wasSkillPressed[0] = _wasSkillPressed[1] = _wasSkillPressed[2] = _wasSkillPressed[3] = false;
    }
}
