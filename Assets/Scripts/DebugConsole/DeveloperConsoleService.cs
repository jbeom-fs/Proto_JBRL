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

        if (!_commands.TryGetValue(commandName, out CommandHandler handler))
            return DeveloperConsoleCommandResult.Error("Unknown command: " + commandName);

        return handler(context, arguments);
    }

    private void RegisterDefaults()
    {
        _commands["help"] = ExecuteHelp;
        _commands["clear"] = ExecuteClear;
        _commands["echo"] = ExecuteEcho;
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

        return DeveloperConsoleCommandResult.Success("Commands: " + builder);
    }

    private DeveloperConsoleCommandResult ExecuteClear(DeveloperConsoleCommandContext context, string arguments)
        => DeveloperConsoleCommandResult.Clear();

    private DeveloperConsoleCommandResult ExecuteEcho(DeveloperConsoleCommandContext context, string arguments)
        => DeveloperConsoleCommandResult.Success(arguments);
}
