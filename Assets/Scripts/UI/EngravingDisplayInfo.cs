using UnityEngine;

public readonly struct EngravingDisplayInfo
{
    public readonly Sprite Icon;
    public readonly string Name;
    public readonly string Description;
    public readonly bool IsPassive;
    public readonly bool HasGrade;
    public readonly EngravingGrade Grade;

    private EngravingDisplayInfo(
        Sprite icon,
        string displayName,
        string description,
        bool isPassive,
        bool hasGrade,
        EngravingGrade grade)
    {
        Icon = icon;
        Name = displayName;
        Description = description;
        IsPassive = isPassive;
        HasGrade = hasGrade;
        Grade = grade;
    }

    public static bool TryCreate(SkillData skill, out EngravingDisplayInfo info)
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
            grade);
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
            true,
            passive.grade);
        return true;
    }
}
