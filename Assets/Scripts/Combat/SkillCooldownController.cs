public class SkillCooldownController
{
    private float _attackCooldown;

    public void Tick(float dt)
    {
        _attackCooldown -= dt;
    }

    public bool IsAttackReady => _attackCooldown <= 0f;

    public void SetAttackCooldown(float value)
    {
        _attackCooldown = value;
    }

    public void ResetAll()
    {
        _attackCooldown = 0f;
    }
}
