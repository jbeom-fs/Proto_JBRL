using UnityEngine;

[CreateAssetMenu(fileName = "TierColorTable", menuName = "JBRogLike/UI/Tier Color Table")]
public sealed class TierColorTable : ScriptableObject
{
    [Tooltip("티어 색. index 0/1/2 = Faint/Whole/Primordial = Common/Rare/Legendary.")]
    [SerializeField] private Color[] tierColors = new Color[3];
    [SerializeField] private Color neutralColor = Color.gray;

    public Color GetColor(int tier, bool hasTier)
    {
        if (!hasTier || tierColors == null || tierColors.Length == 0)
            return neutralColor;

        return tierColors[Mathf.Clamp(tier, 0, tierColors.Length - 1)];
    }
}
