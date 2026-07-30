using UnityEngine;

public readonly struct EngravingDisplayInfo
{
    public readonly Sprite Icon;
    public readonly string Name;
    public readonly string Description;
    public readonly bool IsPassive;
    public readonly bool HasGrade;
    public readonly EngravingGrade Grade;
    public readonly bool HasCooldown;
    public readonly float Cooldown;

    private EngravingDisplayInfo(
        Sprite icon,
        string displayName,
        string description,
        bool isPassive,
        bool hasGrade,
        EngravingGrade grade,
        bool hasCooldown,
        float cooldown)
    {
        Icon = icon;
        Name = displayName;
        Description = description;
        IsPassive = isPassive;
        HasGrade = hasGrade;
        Grade = grade;
        HasCooldown = hasCooldown;
        Cooldown = cooldown;
    }

    public static bool TryCreate(SkillData skill, out EngravingDisplayInfo info)
    {
        float cooldown = skill != null ? skill.cooldown : 0f;
        return TryCreate(skill, cooldown, out info);
    }

    public static bool TryCreate(
        SkillData skill,
        float cooldownOverride,
        out EngravingDisplayInfo info)
    {
        if (skill == null)
        {
            info = default;
            return false;
        }

        bool hasGrade = skill is EngravingData;
        EngravingGrade grade = hasGrade
            ? ((EngravingData)skill).grade
            : default;
        string displayName = string.IsNullOrWhiteSpace(skill.skillName)
            ? skill.name
            : skill.skillName;
        info = new EngravingDisplayInfo(
            skill.icon,
            displayName,
            skill.description,
            false,
            hasGrade,
            grade,
            cooldownOverride > 0f,
            cooldownOverride);
        return true;
    }

    public static bool TryCreate(PassiveEngravingData passive, out EngravingDisplayInfo info)
    {
        if (passive == null)
        {
            info = default;
            return false;
        }

        string displayName = string.IsNullOrWhiteSpace(passive.passiveName)
            ? passive.name
            : passive.passiveName;
        info = new EngravingDisplayInfo(
            passive.icon,
            displayName,
            passive.description,
            true,
            false,
            passive.grade,
            false,
            0f);
        return true;
    }
}
