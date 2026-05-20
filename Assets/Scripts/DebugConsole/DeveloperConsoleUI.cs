using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

public sealed class DeveloperConsoleUI : MonoBehaviour
{
    private static DeveloperConsoleUI s_Active;

    [SerializeField] private GameObject root;
    [SerializeField] private InputField inputField;
    [SerializeField] private Text logText;
    [SerializeField] private GamePauseController pauseController;
    [SerializeField] private Key toggleKey = Key.Backquote;
    [SerializeField] private bool closeWithEscape = true;

    private readonly DeveloperConsoleService _service = new DeveloperConsoleService();
    private bool _warnedMissingPauseController;
    private bool _warnedMissingRoot;
    private bool _warnedMissingInputField;

    public static DeveloperConsoleUI Active => s_Active;
    public static bool IsOpen => s_Active != null && s_Active.IsConsoleOpen;

    public bool IsConsoleOpen => root != null && root.activeSelf;

    private void Awake()
    {
        if (s_Active != null && s_Active != this)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[DeveloperConsoleUI] Duplicate instance detected. Latest instance is now active.", this);
#endif
        }

        s_Active = this;
        if (root != null)
            root.SetActive(false);
    }

    private void OnEnable()
    {
        if (inputField != null)
            inputField.onSubmit.AddListener(HandleSubmit);
    }

    private void OnDisable()
    {
        if (inputField != null)
            inputField.onSubmit.RemoveListener(HandleSubmit);

        if (IsConsoleOpen)
            Close();

        if (s_Active == this)
            s_Active = null;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (IsTogglePressed(keyboard))
        {
            Toggle();
            return;
        }

        if (closeWithEscape && IsConsoleOpen && keyboard.escapeKey.wasPressedThisFrame)
            Close();
    }

    public void Toggle()
    {
        if (IsConsoleOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (!CanUseRequiredUi())
            return;

        root.SetActive(true);
        ResolvePauseController()?.Pause(GamePauseSource.DeveloperConsole);

        inputField.text = string.Empty;
        FocusInputField();
    }

    public void Close()
    {
        if (inputField != null)
        {
            inputField.DeactivateInputField();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == inputField.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        if (root != null)
            root.SetActive(false);

        ResolvePauseController()?.Resume(GamePauseSource.DeveloperConsole);
    }

    private void HandleSubmit(string commandText)
    {
        if (!IsConsoleOpen)
            return;

        DeveloperConsoleCommandResult result = _service.Execute(commandText, new DeveloperConsoleCommandContext(this));
        inputField.text = string.Empty;

        if (result.Handled)
            AppendResult(commandText, result);

        FocusInputField();
    }

    private void AppendResult(string commandText, DeveloperConsoleCommandResult result)
    {
        if (result.ClearLog)
        {
            if (logText != null)
                logText.text = string.Empty;

            Debug.Log("[DeveloperConsole] " + result.Message, this);
            return;
        }

        string prefix = result.IsError ? "Error: " : string.Empty;
        string line = "> " + commandText + "\n" + prefix + result.Message;

        if (logText != null)
            logText.text = line;

        if (result.IsError)
            Debug.LogWarning("[DeveloperConsole] " + result.Message, this);
        else
            Debug.Log("[DeveloperConsole] " + result.Message, this);
    }

    private void FocusInputField()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(inputField.gameObject);

        inputField.Select();
        inputField.ActivateInputField();
    }

    private GamePauseController ResolvePauseController()
    {
        if (pauseController != null)
            return pauseController;

        GamePauseController active = GamePauseController.Active;
        if (active != null)
            return active;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_warnedMissingPauseController)
        {
            Debug.LogWarning("[DeveloperConsoleUI] pauseController is not assigned and GamePauseController.Active is missing.", this);
            _warnedMissingPauseController = true;
        }
#endif
        return null;
    }

    private bool CanUseRequiredUi()
    {
        bool canUse = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (root == null && !_warnedMissingRoot)
        {
            Debug.LogWarning("[DeveloperConsoleUI] root is not assigned.", this);
            _warnedMissingRoot = true;
        }

        if (inputField == null && !_warnedMissingInputField)
        {
            Debug.LogWarning("[DeveloperConsoleUI] inputField is not assigned.", this);
            _warnedMissingInputField = true;
        }
#endif
        if (root == null)
            canUse = false;
        if (inputField == null)
            canUse = false;

        return canUse;
    }

    private static bool WasPressedThisFrame(Keyboard keyboard, Key key)
    {
        if (key == Key.None)
            return false;

        KeyControl control = keyboard[key];
        return control != null && control.wasPressedThisFrame;
    }

    private bool IsTogglePressed(Keyboard keyboard)
    {
        if (WasPressedThisFrame(keyboard, toggleKey))
            return true;

        return toggleKey == Key.None && keyboard.backquoteKey.wasPressedThisFrame;
    }
}
