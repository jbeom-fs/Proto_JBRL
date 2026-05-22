using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewElitePatternSet", menuName = "JBRogLike/Enemy/Elite Pattern Set")]
public sealed class ElitePatternSet : ScriptableObject
{
    [SerializeField] private List<ElitePatternData> patterns = new List<ElitePatternData>();

    public IReadOnlyList<ElitePatternData> Patterns => patterns;
}
