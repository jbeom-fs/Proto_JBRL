using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(LocationRoot))]
public sealed class ArenaSpace : MonoBehaviour
{
    [SerializeField] private ArenaDoor arenaDoor;
    [SerializeField] private Tilemap walkTilemap;
    [SerializeField] private WalkabilityArea walkabilityArea;
    [SerializeField] private Transform enemySpawnPoint;
    [SerializeField] private Portal clearedPortal;
    [SerializeField] private Transform clearedPortalSpawnPoint;

    public ArenaDoor ArenaDoor => arenaDoor;
    public Tilemap WalkTilemap => walkTilemap;
    public WalkabilityArea WalkabilityArea => walkabilityArea;
    public Transform EnemySpawnPoint => enemySpawnPoint;
    public Portal ClearedPortal => clearedPortal;
    public Transform ClearedPortalSpawnPoint => clearedPortalSpawnPoint;

#if UNITY_EDITOR
    private void OnValidate()
    {
        List<string> missing = new();

        if (arenaDoor == null)
            missing.Add(nameof(arenaDoor));
        if (walkTilemap == null)
            missing.Add(nameof(walkTilemap));
        if (walkabilityArea == null)
            missing.Add(nameof(walkabilityArea));
        if (clearedPortal == null)
            missing.Add(nameof(clearedPortal));

        if (missing.Count > 0)
        {
            Debug.LogWarning(
                "[ArenaSpace] " + gameObject.name + " missing: " + string.Join(", ", missing),
                this);
        }
    }
#endif
}
