using UnityEngine;

public interface ISkillResourceLedger
{
    bool Has(SkillResourceType type, int requiredAmount);
    bool Spend(SkillResourceType type, int consumeAmount);
    int GetAmount(SkillResourceType type);
}

/// <summary>
/// Runtime state for one skill slot.
/// Currently used by the player Q/W/E/R slots, but kept independent from
/// MonoBehaviour so enemy and boss skill slots can reuse it later.
/// Skill execution itself stays outside this class and will be moved to a
/// SkillExecutor / SkillExecutionContext layer in a later refactor.
/// </summary>
public sealed class SkillSlotRuntime
{
    public SkillData Data { get; private set; }
    public float CooldownRemaining { get; private set; }

    public bool HasSkill => Data != null;
    public bool IsCooldownReady => CooldownRemaining <= 0f;

    public void Bind(SkillData data)
    {
        Data = data;
        ResetRuntimeState();
    }

    public void TickCooldown(float deltaTime)
    {
        if (CooldownRemaining > 0f)
            CooldownRemaining -= deltaTime;
    }

    public bool CanUse(ISkillResourceLedger resources)
    {
        if (!HasSkill || !IsCooldownReady)
            return false;

        int required = ResolveRequiredAmount(Data);
        return resources == null || resources.Has(Data.resourceType, required);
    }

    public void StartCooldown(float multiplier = 1f)
    {
        float baseCd = Data != null ? Mathf.Max(0f, Data.cooldown) : 0f;
        CooldownRemaining = baseCd * Mathf.Max(0f, multiplier);
    }

    public void ResetRuntimeState()
    {
        CooldownRemaining = 0f;
    }

    private static int ResolveRequiredAmount(SkillData skill)
    {
        if (skill == null || skill.resourceType == SkillResourceType.None)
            return 0;

        if (skill.resourceType == SkillResourceType.Bullet &&
            skill.bulletShortageMode == BulletShortageMode.AllowPartialUse &&
            SkillProjectileUtility.GetEffectiveProjectileCount(skill) == skill.consumeAmount)
        {
            return Mathf.Max(0, skill.requiredAmount);
        }

        return Mathf.Max(skill.requiredAmount, skill.consumeAmount);
    }
}
