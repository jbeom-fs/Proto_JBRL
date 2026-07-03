using System.Collections.Generic;
using UnityEngine;

public readonly struct CustomShapeMatcher
{
    private readonly Vector2 _origin;
    private readonly float _angleDeg;
    private readonly float _cellSize;
    private readonly IReadOnlyList<Vector2Int> _cells;

    public float AngleDeg => _angleDeg;
    public float CellSize => _cellSize;
    public IReadOnlyList<Vector2Int> Cells => _cells;

    public CustomShapeMatcher(
        Vector2 origin,
        float angleDeg,
        float cellSize,
        IReadOnlyList<Vector2Int> cells)
    {
        _origin = origin;
        _angleDeg = angleDeg;
        _cellSize = Mathf.Max(0.01f, cellSize);
        _cells = cells;
    }

    public Vector2 GetCellWorldCenter(Vector2Int cell)
    {
        Quaternion rotation = Quaternion.Euler(0f, 0f, _angleDeg);
        Vector3 offset = rotation * new Vector3(cell.x * _cellSize, cell.y * _cellSize, 0f);
        return _origin + new Vector2(offset.x, offset.y);
    }
}
