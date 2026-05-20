public readonly struct DeveloperConsoleCommandContext
{
    public DeveloperConsoleCommandContext(DeveloperConsoleUI console)
    {
        Console = console;
    }

    public DeveloperConsoleUI Console { get; }
}
