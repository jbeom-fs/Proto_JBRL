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
            "Commands: " + builder + "\nUsage: /TP [destinationId]\nUsage: /DoorOpen [doorType]");
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
}
