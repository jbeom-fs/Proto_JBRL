public enum SoulStatType
{
    /// <summary>Sword/Dagger/Freischutz, percent attack cooldown reduction.</summary>
    AttackSpeed = 0,

    /// <summary>All non-Normal forms, percent skill cooldown reduction.</summary>
    CooldownReduction = 1,

    /// <summary>All non-Normal forms, percent critical chance.</summary>
    Crit = 2,

    /// <summary>Sword/Dagger, percent damage converted to healing.</summary>
    Lifesteal = 3,

    /// <summary>Freischutz, flat magazine size bonus.</summary>
    MagazineSize = 4,

    /// <summary>Freischutz, percent reload time reduction.</summary>
    ReloadSpeed = 5,

    /// <summary>Parry, flat max parry stack bonus.</summary>
    ParryStackMax = 6,

    /// <summary>Parry, flat parry stack grace seconds bonus.</summary>
    ParryGrace = 7,

    /// <summary>Sword, percent combo damage bonus.</summary>
    ComboDamage = 8,

    /// <summary>Dagger, percent ailment damage bonus.</summary>
    AilmentDamage = 9
}
