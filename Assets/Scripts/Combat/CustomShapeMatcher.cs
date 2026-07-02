using System.Collections.Generic;
using UnityEngine;

public readonly struct CustomShapeMatcher
{
    private readonly Vector2 _origin;
    private readonly float _angleDeg;
    private readonly float _cellSize;
    private readonly IReadOnlyList<Vector2Int> _cells;

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

    public bool Contains(Vector2 worldPoint)
    {
        return TryGetCell(worldPoint, out _);
    }

    public bool TryGetCell(Vector2 worldPoint, out Vector2Int cell)
    {
        Vector2 delta = worldPoint - _origin;
        Quaternion inverseRotation = Quaternion.Euler(0f, 0f, -_angleDeg);
        Vector3 local = inverseRotation * new Vector3(delta.x, delta.y, 0f);
        cell = new Vector2Int(
            Mathf.RoundToInt(local.x / _cellSize),
            Mathf.RoundToInt(local.y / _cellSize));

        if (_cells == null)
            return false;

        for (int i = 0; i < _cells.Count; i++)
        {
            if (_cells[i] == cell)
                return true;
        }

        return false;
    }

    public Vector2 GetCellWorldCenter(Vector2Int cell)
    {
        Quaternion rotation = Quaternion.Euler(0f, 0f, _angleDeg);
        Vector3 offset = rotation * new Vector3(cell.x * _cellSize, cell.y * _cellSize, 0f);
        return _origin + new Vector2(offset.x, offset.y);
    }
}
