using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

public sealed class DeveloperConsoleUI : MonoBehaviour
{
    private static DeveloperConsoleUI s_Active;

    [SerializeField] private GameObject root;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TMP_Text logText;
    [SerializeField] private ScrollRect logScrollRect;
    [SerializeField] private GamePauseController pauseController;
    [SerializeField] private Key toggleKey = Key.Backquote;
    [SerializeField] private bool closeWithEscape = true;
    [SerializeField] private int maxLogLines = 120;
    [SerializeField] private int maxCommandHistory = 50;
    [SerializeField] private DeveloperConsoleCommandExecutor commandExecutor;
    [SerializeField] private GameObject autocompletePanel;
    [SerializeField] private TMP_Text autocompleteText;

    private DeveloperConsoleService _service;
    private readonly List<string> _logLines = new List<string>(128);
    private readonly List<string> _commandHistory = new List<string>(64);
    private readonly StringBuilder _logBuilder = new StringBuilder(4096);
    private readonly List<string> _autocompleteSuggestions = new List<string>(MaxAutocompleteSuggestions);
    private readonly List<string> _commandNamesBuffer = new List<string>(16);
    private readonly StringBuilder _autocompleteBuilder = new StringBuilder(256);

    private int _historyIndex;
    private string _editingCommandBeforeHistory = string.Empty;
    private bool _isBrowsingHistory;
    private int _autocompleteIndex;
    private bool _isCyclingAutocomplete;
    private bool _suppressAutocompleteRefresh;
    private bool _warnedMissingPauseController;
    private bool _warnedMissingRoot;
    private bool _warnedMissingInputField;

    private const int MaxAutocompleteSuggestions = 5;

    public static DeveloperConsoleUI Active => s_Active;
    public static bool IsOpen => s_Active != null && s_Active.IsConsoleOpen;

    public bool IsConsoleOpen => root != null && root.activeSelf;

    private bool IsAutocompleteVisible =>
        autocompletePanel != null
        && autocompletePanel.activeInHierarchy
        && _autocompleteSuggestions.Count > 0;

    private void Awake()
    {
        if (s_Active != null && s_Active != this)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[DeveloperConsoleUI] Duplicate instance detected. Latest instance is now active.", this);
#endif
        }

        s_Active = this;
        _service = new DeveloperConsoleService(commandExecutor);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (commandExecutor == null)
            Debug.LogWarning("[DeveloperConsoleUI] commandExecutor is not assigned — game-state commands will fail.", this);
#endif

        if (root != null)
            root.SetActive(false);

        // TMP resets text on Esc (OnCancel) and fires onValueChanged — this prevents that.
        if (inputField != null)
            inputField.restoreOriginalTextOnEscape = false;
    }

    private void OnEnable()
    {
        if (inputField != null)
        {
            inputField.onSubmit.AddListener(HandleSubmit);
            inputField.onValueChanged.AddListener(OnInputValueChanged);
        }
    }

    private void OnDisable()
    {
        if (inputField != null)
        {
            inputField.onSubmit.RemoveListener(HandleSubmit);
            inputField.onValueChanged.RemoveListener(OnInputValueChanged);
        }

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

        if (!IsConsoleOpen)
            return;

        if (IsConsoleInputFocused() && keyboard.tabKey.wasPressedThisFrame)
        {
            HandleAutocompleteTab();
            return;
        }

        if (HandleHistoryNavigation(keyboard))
            return;

        if (closeWithEscape && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (IsAutocompleteVisible)
            {
                HideAutocomplete();
                FocusInputField();
                return;
            }

            Close();
        }
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

        ResetHistoryNavigation();
        HideAutocomplete();
        inputField.text = string.Empty;
        FocusInputField();
    }

    public void Close()
    {
        HideAutocomplete();

        if (inputField != null)
        {
            inputField.DeactivateInputField();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == inputField.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        ResetHistoryNavigation();

        if (root != null)
            root.SetActive(false);

        ResolvePauseController()?.Resume(GamePauseSource.DeveloperConsole);
    }

    private void HandleSubmit(string commandText)
    {
        if (!IsConsoleOpen)
            return;

        StoreCommandHistory(commandText);
        ResetHistoryNavigation();

        DeveloperConsoleCommandResult result = _service.Execute(commandText);
        inputField.text = string.Empty;

        if (result.Handled)
            AppendCommandResult(commandText, result);

        FocusInputField();
    }

    private void OnInputValueChanged(string value)
    {
        if (_suppressAutocompleteRefresh)
            return;

        // Belt-and-suspenders: suppress any onValueChanged that TMP fires during Esc handling.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            return;

        _isCyclingAutocomplete = false;
        _autocompleteIndex = 0;
        RefreshAutocomplete(value);
    }

    private void RefreshAutocomplete(string input)
    {
        _autocompleteSuggestions.Clear();

        if (string.IsNullOrWhiteSpace(input))
        {
            SetAutocompleteVisible(false);
            return;
        }

        string trimmed = input.TrimStart();
        ParseAutocompleteTokens(trimmed, _commandNamesBuffer, out bool trailingSpace);
        int tokenCount = _commandNamesBuffer.Count;

        if (tokenCount == 0)
        {
            _commandNamesBuffer.Clear();
            SetAutocompleteVisible(false);
            return;
        }

        if (tokenCount == 1 && !trailingSpace)
        {
            string token0 = _commandNamesBuffer[0];
            _commandNamesBuffer.Clear();
            BuildCommandNameSuggestions(token0);
        }
        else if ((tokenCount == 1 && trailingSpace) || (tokenCount == 2 && !trailingSpace))
        {
            string rawCmd = _commandNamesBuffer[0];
            string currentArg = tokenCount == 2 ? _commandNamesBuffer[1] : string.Empty;
            _commandNamesBuffer.Clear();

            string commandName = rawCmd.Length > 0 && rawCmd[0] == '/' ? rawCmd.Substring(1) : rawCmd;
            string commandPrefix = rawCmd.Length > 0 && rawCmd[0] == '/' ? rawCmd : "/" + rawCmd;

            _service.GetArgumentSuggestions(commandName, currentArg, _commandNamesBuffer, MaxAutocompleteSuggestions);
            for (int i = 0; i < _commandNamesBuffer.Count; i++)
                _autocompleteSuggestions.Add(commandPrefix + " " + _commandNamesBuffer[i]);
            _commandNamesBuffer.Clear();
        }
        else if ((tokenCount == 2 && trailingSpace) || (tokenCount == 3 && !trailingSpace))
        {
            string rawCmd = _commandNamesBuffer[0];
            string subCommand = _commandNamesBuffer[1];
            string currentArg = tokenCount == 3 ? _commandNamesBuffer[2] : string.Empty;
            _commandNamesBuffer.Clear();

            string commandName = rawCmd.Length > 0 && rawCmd[0] == '/' ? rawCmd.Substring(1) : rawCmd;
            string commandPrefix = (rawCmd.Length > 0 && rawCmd[0] == '/' ? rawCmd : "/" + rawCmd) + " " + subCommand;

            _service.GetSubArgumentSuggestions(commandName, subCommand, currentArg, _commandNamesBuffer, MaxAutocompleteSuggestions);
            for (int i = 0; i < _commandNamesBuffer.Count; i++)
                _autocompleteSuggestions.Add(commandPrefix + " " + _commandNamesBuffer[i]);
            _commandNamesBuffer.Clear();
        }
        else
        {
            _commandNamesBuffer.Clear();
            SetAutocompleteVisible(false);
            return;
        }

        if (_autocompleteSuggestions.Count == 0)
        {
            SetAutocompleteVisible(false);
            return;
        }

        UpdateAutocompleteText();
        SetAutocompleteVisible(true);
    }

    private void BuildCommandNameSuggestions(string token)
    {
        string matchText = token.Length > 0 && token[0] == '/' ? token.Substring(1) : token;
        _service.GetCommandNames(_commandNamesBuffer);
        for (int i = 0; i < _commandNamesBuffer.Count && _autocompleteSuggestions.Count < MaxAutocompleteSuggestions; i++)
        {
            if (string.IsNullOrEmpty(matchText) ||
                _commandNamesBuffer[i].StartsWith(matchText, StringComparison.OrdinalIgnoreCase))
            {
                _autocompleteSuggestions.Add("/" + _commandNamesBuffer[i]);
            }
        }
        _commandNamesBuffer.Clear();
    }

    private static void ParseAutocompleteTokens(string input, List<string> tokens, out bool trailingSpace)
    {
        tokens.Clear();
        trailingSpace = input.Length > 0 && input[input.Length - 1] == ' ';
        int i = 0;
        int len = input.Length;
        while (i < len)
        {
            while (i < len && input[i] == ' ')
                i++;
            if (i >= len)
                break;
            int start = i;
            while (i < len && input[i] != ' ')
                i++;
            tokens.Add(input.Substring(start, i - start));
        }
    }

    private void UpdateAutocompleteText()
    {
        if (autocompleteText == null)
            return;

        _autocompleteBuilder.Length = 0;
        _autocompleteBuilder.Append("Suggestions:");
        for (int i = 0; i < _autocompleteSuggestions.Count; i++)
        {
            _autocompleteBuilder.Append('\n');
            _autocompleteBuilder.Append(i + 1);
            _autocompleteBuilder.Append(". ");
            _autocompleteBuilder.Append(_autocompleteSuggestions[i]);
        }

        autocompleteText.text = _autocompleteBuilder.ToString();
    }

    private void HandleAutocompleteTab()
    {
        if (_autocompleteSuggestions.Count == 0)
            return;

        if (!_isCyclingAutocomplete)
        {
            _isCyclingAutocomplete = true;
            _autocompleteIndex = 0;
        }
        else
        {
            _autocompleteIndex = (_autocompleteIndex + 1) % _autocompleteSuggestions.Count;
        }

        string suggestion = _autocompleteSuggestions[_autocompleteIndex];
        string completed = suggestion + " ";

        _suppressAutocompleteRefresh = true;
        inputField.text = completed;
        _suppressAutocompleteRefresh = false;

        int caretPos = completed.Length;
        inputField.caretPosition = caretPos;
        inputField.selectionAnchorPosition = caretPos;
        inputField.selectionFocusPosition = caretPos;
        inputField.ActivateInputField();

        if (suggestion.IndexOf(' ') < 0)
        {
            _isCyclingAutocomplete = false;
            _autocompleteIndex = 0;
            RefreshAutocomplete(completed);
        }
    }

    private void SetAutocompleteVisible(bool visible)
    {
        if (autocompletePanel == null)
            return;

        if (autocompletePanel.activeSelf != visible)
            autocompletePanel.SetActive(visible);
    }

    private void HideAutocomplete()
    {
        _autocompleteSuggestions.Clear();
        _autocompleteIndex = 0;
        _isCyclingAutocomplete = false;
        SetAutocompleteVisible(false);
    }

    private void AppendCommandResult(string commandText, DeveloperConsoleCommandResult result)
    {
        if (result.ClearLog)
        {
            ClearLog();
            Debug.Log("[DeveloperConsole] " + result.Message, this);
            return;
        }

        AppendLogLine("> " + commandText);
        if (!string.IsNullOrEmpty(result.Message))
            AppendLogLine((result.IsError ? "Error: " : string.Empty) + result.Message);

        RefreshLogText();
        ScrollLogToBottom();

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

    private bool HandleHistoryNavigation(Keyboard keyboard)
    {
        if (!IsConsoleInputFocused())
            return false;

        if (keyboard.upArrowKey.wasPressedThisFrame)
        {
            NavigateHistoryOlder();
            return true;
        }

        if (keyboard.downArrowKey.wasPressedThisFrame)
        {
            NavigateHistoryNewer();
            return true;
        }

        return false;
    }

    private bool IsConsoleInputFocused()
    {
        if (!IsConsoleOpen || inputField == null || !inputField.isFocused)
            return false;

        return EventSystem.current == null || EventSystem.current.currentSelectedGameObject == inputField.gameObject;
    }

    private void NavigateHistoryOlder()
    {
        if (_commandHistory.Count == 0)
            return;

        if (!_isBrowsingHistory)
        {
            _editingCommandBeforeHistory = inputField.text;
            _historyIndex = _commandHistory.Count;
            _isBrowsingHistory = true;
        }

        if (_historyIndex > 0)
            _historyIndex--;

        ApplyHistoryText(_commandHistory[_historyIndex]);
    }

    private void NavigateHistoryNewer()
    {
        if (!_isBrowsingHistory)
            return;

        if (_historyIndex < _commandHistory.Count - 1)
        {
            _historyIndex++;
            ApplyHistoryText(_commandHistory[_historyIndex]);
            return;
        }

        _historyIndex = _commandHistory.Count;
        _isBrowsingHistory = false;
        ApplyHistoryText(_editingCommandBeforeHistory);
        _editingCommandBeforeHistory = string.Empty;
    }

    private void ApplyHistoryText(string command)
    {
        if (inputField == null)
            return;

        inputField.text = command ?? string.Empty;
        int caretPosition = inputField.text.Length;
        inputField.caretPosition = caretPosition;
        inputField.selectionAnchorPosition = caretPosition;
        inputField.selectionFocusPosition = caretPosition;
        inputField.ActivateInputField();
    }

    private void StoreCommandHistory(string commandText)
    {
        if (maxCommandHistory <= 0 || string.IsNullOrWhiteSpace(commandText))
            return;

        string trimmedCommand = commandText.Trim();
        int lastIndex = _commandHistory.Count - 1;
        if (lastIndex >= 0 && _commandHistory[lastIndex] == trimmedCommand)
            return;

        _commandHistory.Add(trimmedCommand);

        int limit = Mathf.Max(1, maxCommandHistory);
        int overflow = _commandHistory.Count - limit;
        if (overflow > 0)
            _commandHistory.RemoveRange(0, overflow);
    }

    private void ResetHistoryNavigation()
    {
        _historyIndex = _commandHistory.Count;
        _editingCommandBeforeHistory = string.Empty;
        _isBrowsingHistory = false;
    }

    private void AppendLogLine(string line)
    {
        _logLines.Add(line);
        TrimLogLines();
    }

    private void TrimLogLines()
    {
        int limit = Mathf.Max(1, maxLogLines);
        int overflow = _logLines.Count - limit;
        if (overflow <= 0)
            return;

        _logLines.RemoveRange(0, overflow);
    }

    private void ClearLog()
    {
        _logLines.Clear();
        _logBuilder.Length = 0;

        if (logText != null)
            logText.text = string.Empty;

        ScrollLogToBottom();
    }

    private void RefreshLogText()
    {
        if (logText == null)
            return;

        _logBuilder.Length = 0;
        for (int i = 0; i < _logLines.Count; i++)
        {
            if (i > 0)
                _logBuilder.Append('\n');

            _logBuilder.Append(_logLines[i]);
        }

        logText.text = _logBuilder.ToString();
    }

    private void ScrollLogToBottom()
    {
        if (logScrollRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        logScrollRect.verticalNormalizedPosition = 0f;
        Canvas.ForceUpdateCanvases();
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
