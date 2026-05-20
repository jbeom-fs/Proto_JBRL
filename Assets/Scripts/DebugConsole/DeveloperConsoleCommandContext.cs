public readonly struct DeveloperConsoleCommandContext
{
    public DeveloperConsoleCommandContext(
        DeveloperConsoleUI console,
        TownDungeonTransitionManager transitionManager,
        TeleportDestinationDatabase teleportDestinationDatabase,
        PlayerController player,
        DungeonManager dungeonManager)
    {
        Console = console;
        TransitionManager = transitionManager;
        TeleportDestinationDatabase = teleportDestinationDatabase;
        Player = player;
        DungeonManager = dungeonManager;
    }

    public DeveloperConsoleUI Console { get; }
    public TownDungeonTransitionManager TransitionManager { get; }
    public TeleportDestinationDatabase TeleportDestinationDatabase { get; }
    public PlayerController Player { get; }
    public DungeonManager DungeonManager { get; }
}
