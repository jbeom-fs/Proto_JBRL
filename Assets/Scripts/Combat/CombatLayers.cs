using UnityEngine;

public static class CombatLayers
{
    private const string EnemyLayerName  = "Enemy";
    private const string PlayerLayerName = "Player";

    private static readonly int             s_EnemyLayer   = LayerMask.NameToLayer(EnemyLayerName);
    private static readonly int             s_PlayerLayer  = LayerMask.NameToLayer(PlayerLayerName);
    private static readonly ContactFilter2D s_EnemyFilter  = BuildLayerFilter(s_EnemyLayer, EnemyLayerName);
    private static readonly ContactFilter2D s_PlayerFilter = BuildLayerFilter(s_PlayerLayer, PlayerLayerName);

    public static bool             HasEnemyLayer  => s_EnemyLayer  >= 0;
    public static bool             HasPlayerLayer => s_PlayerLayer >= 0;
    public static ContactFilter2D  EnemyFilter    => s_EnemyFilter;
    public static ContactFilter2D  PlayerFilter   => s_PlayerFilter;

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
            useTriggers = false,
        };
        filter.SetLayerMask(1 << layer);
        return filter;
    }
}
