using UnityEngine;

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

    public bool CanUse(int availableMp)
    {
        return HasSkill && IsCooldownReady && availableMp >= Data.mpCost;
    }

    public void StartCooldown()
    {
        CooldownRemaining = Data != null ? Mathf.Max(0f, Data.cooldown) : 0f;
    }

    public void ResetRuntimeState()
    {
        CooldownRemaining = 0f;
    }
}
