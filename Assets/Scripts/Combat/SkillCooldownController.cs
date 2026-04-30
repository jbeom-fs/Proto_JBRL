public class SkillCooldownController
{
    private float _attackCooldown;
    private readonly float[] _skillCooldowns = new float[4];

    public void Tick(float dt)
    {
        _attackCooldown -= dt;
        for (int i = 0; i < _skillCooldowns.Length; i++)
            _skillCooldowns[i] -= dt;
    }

    public bool IsAttackReady => _attackCooldown <= 0f;

    public void SetAttackCooldown(float value)
    {
        _attackCooldown = value;
    }

    public bool IsSkillReady(int slot)
    {
        return (uint)slot < 4u && _skillCooldowns[slot] <= 0f;
    }

    public void SetSkillCooldown(int slot, float value)
    {
        if ((uint)slot < 4u)
            _skillCooldowns[slot] = value;
    }

    public float GetSkillRemaining(int slot)
    {
        return (uint)slot < 4u ? System.Math.Max(0f, _skillCooldowns[slot]) : 0f;
    }

    public void ResetAll()
    {
        _attackCooldown = 0f;
        System.Array.Clear(_skillCooldowns, 0, _skillCooldowns.Length);
    }
}
