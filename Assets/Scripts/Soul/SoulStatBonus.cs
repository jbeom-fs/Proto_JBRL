using System;

public sealed class SoulStatBonus
{
    private const int StatCount = (int)SoulStatType.AilmentDamage + 1;

    private readonly float[] _bonuses = new float[StatCount];

    public void Recalculate(PlayerFormId form, PlayerSoulEnhancements enhancements, SoulEnhancementTable table)
    {
        Array.Clear(_bonuses, 0, _bonuses.Length);

        if (enhancements == null || table == null)
            return;

        for (int i = 0; i < StatCount; i++)
        {
            SoulStatType stat = (SoulStatType)i;
            int level = enhancements.GetLevel(form, stat);
            if (level <= 0)
                continue;

            if (!table.TryGetPerLevel(form, stat, out float perLevel))
                continue;

            _bonuses[i] = level * perLevel;
        }
    }

    public float Get(SoulStatType stat)
    {
        int index = (int)stat;
        return (uint)index < (uint)_bonuses.Length ? _bonuses[index] : 0f;
    }
}
