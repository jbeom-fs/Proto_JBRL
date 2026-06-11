using System;

public enum ItemEffectType
{
    None,
    HealHp,
    MaxHpBonus,
    AttackBonus,
    DefenseBonus,
    MoveSpeedBonus
}

[Serializable]
public struct ItemEffect
{
    public ItemEffectType type;
    public int value;
}
