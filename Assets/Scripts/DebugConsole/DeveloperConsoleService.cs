using System;
using System.Collections.Generic;
using System.Text;

public sealed class DeveloperConsoleService
{
    private delegate DeveloperConsoleCommandResult CommandHandler(DeveloperConsoleCommandContext context, string arguments);

    private readonly Dictionary<string, CommandHandler> _commands = new Dictionary<string, CommandHandler>(StringComparer.OrdinalIgnoreCase);

    public DeveloperConsoleService()
    {
        RegisterDefaults();
    }

    public DeveloperConsoleCommandResult Execute(string input, DeveloperConsoleCommandContext context)
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

        return handler(context, arguments);
    }

    private void RegisterDefaults()
    {
        _commands["help"] = ExecuteHelp;
        _commands["clear"] = ExecuteClear;
        _commands["echo"] = ExecuteEcho;
        _commands["tp"] = ExecuteTeleport;
        _commands["dooropen"] = ExecuteDoorOpen;
        _commands["floor"] = ExecuteFloor;
    }

    private DeveloperConsoleCommandResult ExecuteHelp(DeveloperConsoleCommandContext context, string arguments)
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
            "\nUsage: /DoorOpen [doorType]" +
            "\nUsage: /floor add [count] | /floor sub [count] | /floor set [floor]");
    }

    private DeveloperConsoleCommandResult ExecuteClear(DeveloperConsoleCommandContext context, string arguments)
        => DeveloperConsoleCommandResult.Clear();

    private DeveloperConsoleCommandResult ExecuteEcho(DeveloperConsoleCommandContext context, string arguments)
        => DeveloperConsoleCommandResult.Success(arguments);

    private DeveloperConsoleCommandResult ExecuteTeleport(DeveloperConsoleCommandContext context, string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return DeveloperConsoleCommandResult.Error("Usage: /TP [destinationId]");

        if (context.TransitionManager == null)
            return DeveloperConsoleCommandResult.Error("Teleport manager is not assigned.");

        if (context.TeleportDestinationDatabase == null)
            return DeveloperConsoleCommandResult.Error("Teleport destination database is not assigned.");

        if (context.Player == null)
            return DeveloperConsoleCommandResult.Error("Player is not assigned.");

        string destinationId = arguments.Trim();
        if (destinationId.IndexOf(' ') >= 0)
            return DeveloperConsoleCommandResult.Error("Usage: /TP [destinationId]");

        if (!context.TeleportDestinationDatabase.TryResolveLocationId(destinationId, out string resolvedId))
            return DeveloperConsoleCommandResult.Error("Unknown destinationId: " + destinationId);

        context.TransitionManager.TeleportPlayer(context.Player, resolvedId);
        return DeveloperConsoleCommandResult.Success("Teleported to " + resolvedId);
    }

    private DeveloperConsoleCommandResult ExecuteDoorOpen(DeveloperConsoleCommandContext context, string arguments)
    {
        if (context.DungeonManager == null)
            return DeveloperConsoleCommandResult.Error("DungeonManager is not assigned.");

        string doorType = string.IsNullOrWhiteSpace(arguments) ? "normal" : arguments.Trim();
        if (doorType.IndexOf(' ') >= 0)
            return DeveloperConsoleCommandResult.Error("Usage: /DoorOpen [doorType]");

        int openedCount;
        string label;
        if (IsNormalDoorType(doorType))
        {
            openedCount = context.DungeonManager.OpenDebugNormalDoors();
            label = "normal";
        }
        else if (string.Equals(doorType, "elite", StringComparison.OrdinalIgnoreCase))
        {
            openedCount = context.DungeonManager.OpenDebugEliteDoors();
            label = "elite";
        }
        else
        {
            return DeveloperConsoleCommandResult.Error("Unsupported DoorType: " + doorType + ". Use normal/basic/default or elite.");
        }

        if (openedCount <= 0)
            return DeveloperConsoleCommandResult.Success("No " + label + " doors to open.");

        return DeveloperConsoleCommandResult.Success("Opened " + openedCount + " " + label + " door(s).");
    }

    private static bool IsNormalDoorType(string doorType)
    {
        return string.Equals(doorType, "normal", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(doorType, "basic", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(doorType, "default", StringComparison.OrdinalIgnoreCase);
    }

    private DeveloperConsoleCommandResult ExecuteFloor(DeveloperConsoleCommandContext context, string arguments)
    {
        if (context.DungeonManager == null)
            return DeveloperConsoleCommandResult.Error("DungeonManager is not assigned.");

        string subCommand;
        string valueText;
        if (!TryReadFloorArguments(arguments, out subCommand, out valueText))
            return DeveloperConsoleCommandResult.Error(GetFloorUsage());

        if (string.Equals(subCommand, "add", StringComparison.OrdinalIgnoreCase))
            return ExecuteFloorAdd(context.DungeonManager, valueText);

        if (string.Equals(subCommand, "sub", StringComparison.OrdinalIgnoreCase))
            return ExecuteFloorSub(context.DungeonManager, valueText);

        if (string.Equals(subCommand, "set", StringComparison.OrdinalIgnoreCase))
            return ExecuteFloorSet(context.DungeonManager, valueText);

        return DeveloperConsoleCommandResult.Error(GetFloorUsage());
    }

    private DeveloperConsoleCommandResult ExecuteFloorAdd(DungeonManager dungeonManager, string valueText)
    {
        if (!TryParsePositiveOptionalCount(valueText, out int count))
            return DeveloperConsoleCommandResult.Error("Usage: /floor add [positiveCount]");

        if (count > dungeonManager.MaxFloor - dungeonManager.CurrentFloor)
            return DeveloperConsoleCommandResult.Error("Invalid floor: target floor exceeds max floor.");

        int targetFloor = dungeonManager.CurrentFloor + count;
        return ExecuteFloorTransition(dungeonManager, targetFloor);
    }

    private DeveloperConsoleCommandResult ExecuteFloorSub(DungeonManager dungeonManager, string valueText)
    {
        if (!TryParsePositiveOptionalCount(valueText, out int count))
            return DeveloperConsoleCommandResult.Error("Usage: /floor sub [positiveCount]");

        if (count > dungeonManager.CurrentFloor - dungeonManager.MinFloor)
            return DeveloperConsoleCommandResult.Error("Invalid floor: target floor must be 1 or higher.");

        int targetFloor = dungeonManager.CurrentFloor - count;
        return ExecuteFloorTransition(dungeonManager, targetFloor);
    }

    private DeveloperConsoleCommandResult ExecuteFloorSet(DungeonManager dungeonManager, string valueText)
    {
        if (string.IsNullOrWhiteSpace(valueText))
            return DeveloperConsoleCommandResult.Error("Usage: /floor set [floor]");

        if (!TryParsePositiveInt(valueText, out int targetFloor))
            return DeveloperConsoleCommandResult.Error("Usage: /floor set [floor]");

        if (targetFloor < dungeonManager.MinFloor || targetFloor > dungeonManager.MaxFloor)
        {
            return DeveloperConsoleCommandResult.Error(
                "Invalid floor: floor must be between " + dungeonManager.MinFloor + " and " + dungeonManager.MaxFloor + ".");
        }

        return ExecuteFloorTransition(dungeonManager, targetFloor);
    }

    private DeveloperConsoleCommandResult ExecuteFloorTransition(DungeonManager dungeonManager, int targetFloor)
    {
        if (targetFloor == dungeonManager.CurrentFloor)
            return DeveloperConsoleCommandResult.Success("Already on floor " + dungeonManager.CurrentFloor + ".");

        if (!dungeonManager.TryTransitionToFloor(targetFloor, out string message))
            return DeveloperConsoleCommandResult.Error(message);

        return DeveloperConsoleCommandResult.Success(message);
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
