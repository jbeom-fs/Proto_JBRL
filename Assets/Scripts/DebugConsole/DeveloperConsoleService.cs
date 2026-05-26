using System;
using System.Collections.Generic;
using System.Text;

public sealed class DeveloperConsoleService
{
    private delegate DeveloperConsoleCommandResult CommandHandler(string arguments);
    private delegate void ArgumentSuggestionProvider(string currentArg, List<string> output, int maxCount);

    private static readonly string[] s_FloorArgs = { "add", "sub", "set" };
    private static readonly string[] s_DoorOpenArgs = { "normal", "elite" };

    private readonly Dictionary<string, CommandHandler> _commands = new Dictionary<string, CommandHandler>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ArgumentSuggestionProvider> _argumentProviders = new Dictionary<string, ArgumentSuggestionProvider>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _destinationIdBuffer = new List<string>(16);
    private readonly DeveloperConsoleCommandExecutor _executor;

    public DeveloperConsoleService(DeveloperConsoleCommandExecutor executor)
    {
        _executor = executor;
        RegisterDefaults();
    }

    public void GetCommandNames(List<string> output)
    {
        if (output == null)
            return;

        foreach (string name in _commands.Keys)
            output.Add(name);
    }

    public void GetArgumentSuggestions(string commandName, string currentArg, List<string> output, int maxCount)
    {
        if (output == null)
            return;

        if (string.IsNullOrWhiteSpace(commandName))
            return;

        if (!_argumentProviders.TryGetValue(commandName, out ArgumentSuggestionProvider provider))
            return;

        provider(currentArg, output, maxCount);
    }

    public DeveloperConsoleCommandResult Execute(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return DeveloperConsoleCommandResult.Ignored();

        string trimmed = input.Trim();
        int separatorIndex = trimmed.IndexOf(' ');
        string commandName = separatorIndex >= 0 ? trimmed.Substring(0, separatorIndex) : trimmed;
        string arguments = separatorIndex >= 0 ? trimmed.Substring(separatorIndex + 1).Trim() : string.Empty;
        if (commandName.Length > 0 && commandName[0] == '/')
            commandName = commandName.Substring(1);

        if (!_commands.TryGetValue(commandName, out CommandHandler handler))
            return DeveloperConsoleCommandResult.Error("Unknown command: " + commandName);

        return handler(arguments);
    }

    private void RegisterDefaults()
    {
        _commands["help"] = ExecuteHelp;
        _commands["clear"] = ExecuteClear;
        _commands["echo"] = ExecuteEcho;
        _commands["tp"] = ExecuteTeleport;
        _commands["dooropen"] = ExecuteDoorOpen;
        _commands["kill"] = ExecuteKill;
        _commands["floor"] = ExecuteFloor;

        _argumentProviders["floor"] = ProvideFloorSuggestions;
        _argumentProviders["dooropen"] = ProvideDoorOpenSuggestions;
        _argumentProviders["tp"] = ProvideTeleportSuggestions;
    }

    private static void ProvideFloorSuggestions(string currentArg, List<string> output, int maxCount)
        => FilterSuggestions(s_FloorArgs, currentArg, output, maxCount);

    private static void ProvideDoorOpenSuggestions(string currentArg, List<string> output, int maxCount)
        => FilterSuggestions(s_DoorOpenArgs, currentArg, output, maxCount);

    private void ProvideTeleportSuggestions(string currentArg, List<string> output, int maxCount)
    {
        if (_executor == null)
            return;

        _destinationIdBuffer.Clear();
        _executor.GetTeleportDestinationIds(_destinationIdBuffer);
        FilterSuggestions(_destinationIdBuffer, currentArg, output, maxCount);
    }

    private static void FilterSuggestions(IReadOnlyList<string> candidates, string prefix, List<string> output, int maxCount)
    {
        for (int i = 0; i < candidates.Count && output.Count < maxCount; i++)
        {
            if (string.IsNullOrEmpty(prefix) ||
                candidates[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                output.Add(candidates[i]);
        }
    }

    private DeveloperConsoleCommandResult ExecuteHelp(string arguments)
    {
        StringBuilder builder = new StringBuilder();
        bool first = true;
        foreach (string commandName in _commands.Keys)
        {
            if (!first)
                builder.Append(", ");

            builder.Append(commandName);
            first = false;
        }

        return DeveloperConsoleCommandResult.Success(
            "Commands: " + builder +
            "\nUsage: /TP [destinationId]" +
            "\nUsage: /DoorOpen [normal|elite]" +
            "\nUsage: /kill" +
            "\nUsage: /floor add [count] | /floor sub [count] | /floor set [floor]");
    }

    private DeveloperConsoleCommandResult ExecuteClear(string arguments)
        => DeveloperConsoleCommandResult.Clear();

    private DeveloperConsoleCommandResult ExecuteEcho(string arguments)
        => DeveloperConsoleCommandResult.Success(arguments);

    private DeveloperConsoleCommandResult ExecuteTeleport(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return DeveloperConsoleCommandResult.Error("Usage: /TP [destinationId]");

        if (_executor == null)
            return DeveloperConsoleCommandResult.Error("Command executor is not assigned.");

        string destinationId = arguments.Trim();
        if (destinationId.IndexOf(' ') >= 0)
            return DeveloperConsoleCommandResult.Error("Usage: /TP [destinationId]");

        return _executor.ExecuteTeleport(destinationId);
    }

    private DeveloperConsoleCommandResult ExecuteDoorOpen(string arguments)
    {
        if (_executor == null)
            return DeveloperConsoleCommandResult.Error("Command executor is not assigned.");

        string doorType = string.IsNullOrWhiteSpace(arguments) ? "normal" : arguments.Trim();
        if (doorType.IndexOf(' ') >= 0)
            return DeveloperConsoleCommandResult.Error("Usage: /DoorOpen [normal|elite]");

        if (string.Equals(doorType, "normal", StringComparison.OrdinalIgnoreCase))
            return _executor.ExecuteOpenNormalDoors();

        if (string.Equals(doorType, "elite", StringComparison.OrdinalIgnoreCase))
            return _executor.ExecuteOpenEliteDoors();

        return DeveloperConsoleCommandResult.Error("Unsupported DoorType: " + doorType + ". Use normal or elite.");
    }

    private DeveloperConsoleCommandResult ExecuteKill(string arguments)
    {
        if (!string.IsNullOrWhiteSpace(arguments))
            return DeveloperConsoleCommandResult.Error("Usage: /kill");

        if (_executor == null)
            return DeveloperConsoleCommandResult.Error("Command executor is not assigned.");

        return _executor.ExecuteKill();
    }

    private DeveloperConsoleCommandResult ExecuteFloor(string arguments)
    {
        if (_executor == null)
            return DeveloperConsoleCommandResult.Error("Command executor is not assigned.");

        if (!TryReadFloorArguments(arguments, out string subCommand, out string valueText))
            return DeveloperConsoleCommandResult.Error(GetFloorUsage());

        if (string.Equals(subCommand, "add", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParsePositiveOptionalCount(valueText, out int count))
                return DeveloperConsoleCommandResult.Error("Usage: /floor add [positiveCount]");
            return _executor.ExecuteFloorAdd(count);
        }

        if (string.Equals(subCommand, "sub", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParsePositiveOptionalCount(valueText, out int count))
                return DeveloperConsoleCommandResult.Error("Usage: /floor sub [positiveCount]");
            return _executor.ExecuteFloorSub(count);
        }

        if (string.Equals(subCommand, "set", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(valueText))
                return DeveloperConsoleCommandResult.Error("Usage: /floor set [floor]");
            if (!TryParsePositiveInt(valueText, out int targetFloor))
                return DeveloperConsoleCommandResult.Error("Usage: /floor set [floor]");
            return _executor.ExecuteFloorSet(targetFloor);
        }

        return DeveloperConsoleCommandResult.Error(GetFloorUsage());
    }

    private static bool TryReadFloorArguments(string arguments, out string subCommand, out string valueText)
    {
        subCommand = string.Empty;
        valueText = string.Empty;

        if (string.IsNullOrWhiteSpace(arguments))
            return false;

        string trimmed = arguments.Trim();
        int separatorIndex = trimmed.IndexOf(' ');
        subCommand = separatorIndex >= 0 ? trimmed.Substring(0, separatorIndex) : trimmed;
        valueText = separatorIndex >= 0 ? trimmed.Substring(separatorIndex + 1).Trim() : string.Empty;

        if (valueText.IndexOf(' ') >= 0)
            return false;

        return !string.IsNullOrWhiteSpace(subCommand);
    }

    private static bool TryParsePositiveOptionalCount(string valueText, out int count)
    {
        if (string.IsNullOrWhiteSpace(valueText))
        {
            count = 1;
            return true;
        }

        return TryParsePositiveInt(valueText, out count);
    }

    private static bool TryParsePositiveInt(string valueText, out int value)
    {
        if (!int.TryParse(valueText, out value))
            return false;

        return value > 0;
    }

    private static string GetFloorUsage()
        => "Usage: /floor add [count] | /floor sub [count] | /floor set [floor]";
}
