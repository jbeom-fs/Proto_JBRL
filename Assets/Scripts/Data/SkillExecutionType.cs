/// <summary>
/// High-level execution route for a skill.
/// Existing skills use InstantArea; other values are reserved for later handlers.
/// </summary>
public enum SkillExecutionType
{
    InstantArea = 0,
    Projectile = 1,
    Dash = 2,
    AreaOverTime = 3,
    Buff = 4
}
