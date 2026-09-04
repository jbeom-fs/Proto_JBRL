using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public sealed class PatternRangeData
{
    [SerializeField] private AttackPatternType patternType = AttackPatternType.Circle;
    [SerializeField, Min(0)] private int patternRange = 5;
    [SerializeField, Range(1f, 180f)] private float coneHalfAngle = 45f;
    [SerializeField] private List<Vector2Int> customCells = new();

    public AttackPatternType PatternType => patternType;
    public int PatternRange => Mathf.Max(0, patternRange);
    public float ConeHalfAngle => coneHalfAngle;
    public IReadOnlyList<Vector2Int> CustomCells => customCells;
}
