using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemyPatternSet", menuName = "JBRogLike/Enemy/Enemy Pattern Set")]
public sealed class EnemyPatternSet : ScriptableObject
{
    [SerializeField] private List<EnemyPatternData> patterns = new List<EnemyPatternData>();

    public IReadOnlyList<EnemyPatternData> Patterns => patterns;
}
