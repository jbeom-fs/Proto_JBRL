using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
public sealed class ArenaDoor : MonoBehaviour
{
    [SerializeField] private Tilemap doorTilemap;

    private readonly List<DoorTileSnapshot> _doorTiles = new();
    private bool _warnedEmptyDoorCache;

    private void Awake()
    {
        CacheDoorTiles();
    }

    public void Close()
    {
        if (doorTilemap == null || _doorTiles.Count == 0)
            return;

        for (int i = 0; i < _doorTiles.Count; i++)
        {
            DoorTileSnapshot tile = _doorTiles[i];
            doorTilemap.SetTile(tile.Cell, tile.Tile);
        }
    }

    public void Open()
    {
        if (doorTilemap == null || _doorTiles.Count == 0)
            return;

        for (int i = 0; i < _doorTiles.Count; i++)
            doorTilemap.SetTile(_doorTiles[i].Cell, null);
    }

    private void CacheDoorTiles()
    {
        _doorTiles.Clear();

        if (doorTilemap != null)
        {
            foreach (Vector3Int cell in doorTilemap.cellBounds.allPositionsWithin)
            {
                if (!doorTilemap.HasTile(cell))
                    continue;

                _doorTiles.Add(new DoorTileSnapshot(cell, doorTilemap.GetTile(cell)));
            }
        }

        if (_doorTiles.Count == 0 && !_warnedEmptyDoorCache)
        {
            Debug.LogWarning("[ArenaDoor] Door tilemap has no cached door tiles.", this);
            _warnedEmptyDoorCache = true;
        }
    }

    private readonly struct DoorTileSnapshot
    {
        public DoorTileSnapshot(Vector3Int cell, TileBase tile)
        {
            Cell = cell;
            Tile = tile;
        }

        public Vector3Int Cell { get; }
        public TileBase Tile { get; }
    }
}
