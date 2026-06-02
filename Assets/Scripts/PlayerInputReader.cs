using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

[DefaultExecutionOrder(-10)]
public class PlayerInputReader : MonoBehaviour
{
    [SerializeField] private PlayerInputKeySettings keySettings;
    [SerializeField] private Camera aimCamera;

    public Vector2 MoveInput { get; private set; }
    public bool HasMouseAim { get; private set; }
    public Vector2 AimWorldPoint { get; private set; }
    public bool InteractConfirmPressedThisFrame { get; private set; }
    public bool InventoryPressedThisFrame { get; private set; }
    public bool WasBasicAttackPressed { get; private set; }
    public bool WasReloadPressed { get; private set; }

    public bool WasStairPressed => InteractConfirmPressedThisFrame;
    public bool IsGamePaused => GamePauseController.IsPaused;
    public bool IsGameplayInputBlocked => GamePauseController.IsPaused || DeveloperConsoleUI.IsOpen;

    private readonly bool[] _wasSkillPressed = new bool[4];
    private bool _warnedMissingSettings;
    private Camera _cachedAimCamera;

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

            return IsBindingHeld(GetBasicAttackBinding(), blockMouseOverUi: true);
        }
    }

    public bool IsSkillHeld(int slot)
    {
        if (IsGameplayInputBlocked)
            return false;

        return IsBindingHeld(GetSkillSlotBinding(slot), blockMouseOverUi: true);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        PlayerInputKeySettings settings = ResolveSettings();
        UpdateMouseAim(settings, mouse);

        InventoryPressedThisFrame = WasPressedThisFrame(keyboard, settings != null ? settings.inventory : Key.I);

        if (IsGameplayInputBlocked)
        {
            ClearGameplayFlags();
            return;
        }

        float x = 0f;
        float y = 0f;
        if (IsPressed(keyboard, settings != null ? settings.GetMoveUpKey() : Key.UpArrow)) y = 1f;
        if (IsPressed(keyboard, settings != null ? settings.GetMoveDownKey() : Key.DownArrow)) y = -1f;
        if (IsPressed(keyboard, settings != null ? settings.GetMoveLeftKey() : Key.LeftArrow)) x = -1f;
        if (IsPressed(keyboard, settings != null ? settings.GetMoveRightKey() : Key.RightArrow)) x = 1f;
        MoveInput = new Vector2(x, y);

        InteractConfirmPressedThisFrame = WasPressedThisFrame(keyboard, settings != null ? settings.interactConfirm : Key.Z);
        WasBasicAttackPressed = WasBindingPressedThisFrame(
            settings != null ? settings.GetBasicAttackBinding() : new InputBinding(Key.Space),
            blockMouseOverUi: true);
        WasReloadPressed = WasPressedThisFrame(keyboard, settings != null ? settings.GetReloadKey() : Key.A);

        for (int i = 0; i < _wasSkillPressed.Length; i++)
        {
            InputBinding binding = settings != null
                ? settings.GetSkillSlotBinding(i)
                : new InputBinding(GetDefaultSkillSlotKey(i));

            _wasSkillPressed[i] = WasBindingPressedThisFrame(binding, blockMouseOverUi: true);
        }
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

    private void UpdateMouseAim(PlayerInputKeySettings settings, Mouse mouse)
    {
        HasMouseAim = settings != null && settings.controlScheme == PlayerControlScheme.ActionMouseAim;
        if (!HasMouseAim || mouse == null)
            return;

        Camera camera = ResolveAimCamera();
        if (camera == null)
        {
            AimWorldPoint = transform.position;
            return;
        }

        Vector2 screenPosition = mouse.position.ReadValue();
        float depth = camera.WorldToScreenPoint(transform.position).z;
        Vector3 worldPosition = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
        AimWorldPoint = new Vector2(worldPosition.x, worldPosition.y);
    }

    private Camera ResolveAimCamera()
    {
        if (aimCamera != null)
            return aimCamera;

        if (_cachedAimCamera == null)
            _cachedAimCamera = Camera.main;

        return _cachedAimCamera;
    }

    private static bool IsPressed(Keyboard keyboard, Key key)
    {
        if (keyboard == null || key == Key.None)
            return false;

        KeyControl control = keyboard[key];
        return control != null && control.isPressed;
    }

    private static bool WasPressedThisFrame(Keyboard keyboard, Key key)
    {
        if (keyboard == null || key == Key.None)
            return false;

        KeyControl control = keyboard[key];
        return control != null && control.wasPressedThisFrame;
    }

    private InputBinding GetBasicAttackBinding()
        => keySettings != null ? keySettings.GetBasicAttackBinding() : new InputBinding(Key.Space);

    private InputBinding GetSkillSlotBinding(int slot)
        => keySettings != null ? keySettings.GetSkillSlotBinding(slot) : new InputBinding(GetDefaultSkillSlotKey(slot));

    private static bool WasBindingPressedThisFrame(InputBinding binding, bool blockMouseOverUi)
    {
        bool keyPressed = binding.HasKey && WasPressedThisFrame(Keyboard.current, binding.key);
        bool mousePressed = binding.HasMouseButton &&
            (!blockMouseOverUi || !IsPointerOverUi()) &&
            WasMousePressedThisFrame(Mouse.current, binding.mouseButton);

        return keyPressed || mousePressed;
    }

    private static bool IsBindingHeld(InputBinding binding, bool blockMouseOverUi)
    {
        bool keyHeld = binding.HasKey && IsPressed(Keyboard.current, binding.key);
        bool mouseHeld = binding.HasMouseButton &&
            (!blockMouseOverUi || !IsPointerOverUi()) &&
            IsMousePressed(Mouse.current, binding.mouseButton);

        return keyHeld || mouseHeld;
    }

    private static bool WasMousePressedThisFrame(Mouse mouse, PointerButton button)
    {
        ButtonControl control = GetMouseButton(mouse, button);
        return control != null && control.wasPressedThisFrame;
    }

    private static bool IsMousePressed(Mouse mouse, PointerButton button)
    {
        ButtonControl control = GetMouseButton(mouse, button);
        return control != null && control.isPressed;
    }

    private static ButtonControl GetMouseButton(Mouse mouse, PointerButton button)
    {
        if (mouse == null)
            return null;

        switch (button)
        {
            case PointerButton.Left: return mouse.leftButton;
            case PointerButton.Right: return mouse.rightButton;
            case PointerButton.Middle: return mouse.middleButton;
            default: return null;
        }
    }

    private static bool IsPointerOverUi()
    {
        EventSystem eventSystem = EventSystem.current;
        return eventSystem != null && eventSystem.IsPointerOverGameObject();
    }

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

    private void ClearGameplayFlags()
    {
        MoveInput = Vector2.zero;
        InteractConfirmPressedThisFrame = false;
        WasBasicAttackPressed = false;
        WasReloadPressed = false;
        _wasSkillPressed[0] = _wasSkillPressed[1] = _wasSkillPressed[2] = _wasSkillPressed[3] = false;
    }
}
