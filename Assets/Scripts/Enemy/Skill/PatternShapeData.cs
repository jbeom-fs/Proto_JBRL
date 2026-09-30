using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public sealed class PatternShapeData
{
    // 새 적 스킬의 기본 모양은 반경 기반 원형(값 7).
    [SerializeField] private AttackPatternType patternType = AttackPatternType.Circle;
    [SerializeField, Range(1f, 180f)] private float coneHalfAngle = 45f;
    [SerializeField] private List<Vector2Int> customCells = new();

    public AttackPatternType PatternType => patternType;
    public float ConeHalfAngle => coneHalfAngle;
    public IReadOnlyList<Vector2Int> CustomCells => customCells;
}
