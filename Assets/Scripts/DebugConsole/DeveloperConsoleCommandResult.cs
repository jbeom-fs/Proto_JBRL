public readonly struct DeveloperConsoleCommandResult
{
    private DeveloperConsoleCommandResult(bool handled, bool clearLog, bool isError, string message)
    {
        Handled = handled;
        ClearLog = clearLog;
        IsError = isError;
        Message = message;
    }

    public bool Handled { get; }
    public bool ClearLog { get; }
    public bool IsError { get; }
    public string Message { get; }

    public static DeveloperConsoleCommandResult Ignored()
        => new DeveloperConsoleCommandResult(false, false, false, string.Empty);

    public static DeveloperConsoleCommandResult Success(string message)
        => new DeveloperConsoleCommandResult(true, false, false, message);

    public static DeveloperConsoleCommandResult Error(string message)
        => new DeveloperConsoleCommandResult(true, false, true, message);

    public static DeveloperConsoleCommandResult Clear()
        => new DeveloperConsoleCommandResult(true, true, false, "Console cleared.");
}
