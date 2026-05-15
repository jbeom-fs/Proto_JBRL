using UnityEngine;

public static class CombatLayers
{
    private const string EnemyLayerName  = "Enemy";
    private const string PlayerLayerName = "Player";
    private const string WallLayerName   = "Wall";

    private static readonly int             s_EnemyLayer   = LayerMask.NameToLayer(EnemyLayerName);
    private static readonly int             s_PlayerLayer  = LayerMask.NameToLayer(PlayerLayerName);
    private static readonly int             s_WallLayer    = LayerMask.NameToLayer(WallLayerName);
    private static readonly int             s_EnemyMask    = BuildLayerMask(s_EnemyLayer);
    private static readonly int             s_PlayerMask   = BuildLayerMask(s_PlayerLayer);
    private static readonly int             s_WallMask     = BuildNamedMask(WallLayerName, "Obstacle", "Obstacles");
    private static readonly ContactFilter2D s_EnemyFilter  = BuildLayerFilter(s_EnemyLayer, EnemyLayerName);
    private static readonly ContactFilter2D s_PlayerFilter = BuildLayerFilter(s_PlayerLayer, PlayerLayerName);
    private static readonly ContactFilter2D s_WallFilter   = BuildMaskFilter(s_WallMask, WallLayerName);

    public static bool             HasEnemyLayer  => s_EnemyLayer  >= 0;
    public static bool             HasPlayerLayer => s_PlayerLayer >= 0;
    public static bool             HasWallLayer   => s_WallLayer   >= 0;
    public static int              EnemyLayer     => s_EnemyLayer;
    public static int              PlayerLayer    => s_PlayerLayer;
    public static int              WallLayer      => s_WallLayer;
    public static int              EnemyMask      => s_EnemyMask;
    public static int              PlayerMask     => s_PlayerMask;
    public static int              WallMask       => s_WallMask;
    public static ContactFilter2D  EnemyFilter    => s_EnemyFilter;
    public static ContactFilter2D  PlayerFilter   => s_PlayerFilter;
    public static ContactFilter2D  WallFilter     => s_WallFilter;

    private static int BuildLayerMask(int layer)
    {
        return layer >= 0 ? 1 << layer : 0;
    }

    private static int BuildNamedMask(params string[] layerNames)
    {
        int mask = LayerMask.GetMask(layerNames);
        if (mask == 0)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[CombatLayers] Wall/Obstacle layers are not defined in Tags and Layers.");
#endif
        }

        return mask;
    }

    private static ContactFilter2D BuildLayerFilter(int layer, string layerName)
    {
        if (layer < 0)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning(
                $"[CombatLayers] Layer '{layerName}' is not defined in Tags and Layers. " +
                "Falling back to ContactFilter2D.noFilter for combat queries.");
#endif
            return ContactFilter2D.noFilter;
        }

        ContactFilter2D filter = new ContactFilter2D
        {
            useLayerMask = true,
            useTriggers = false,
        };
        filter.SetLayerMask(BuildLayerMask(layer));
        return filter;
    }

    private static ContactFilter2D BuildMaskFilter(int mask, string layerName)
    {
        if (mask == 0)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning(
                $"[CombatLayers] Layer mask for '{layerName}' is empty. " +
                "Falling back to ContactFilter2D.noFilter for combat queries.");
#endif
            return ContactFilter2D.noFilter;
        }

        ContactFilter2D filter = new ContactFilter2D
        {
            useLayerMask = true,
            useTriggers = false,
        };
        filter.SetLayerMask(mask);
        return filter;
    }
}
