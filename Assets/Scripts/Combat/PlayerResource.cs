public class PlayerResource
{
    private int _currentHp;

    public bool IsAlive => _currentHp > 0;
    public int CurrentHp => _currentHp;

    public void Initialize(int maxHp)
    {
        _currentHp = maxHp;
    }

    public void TakeDamage(int damage)
    {
        _currentHp = System.Math.Max(0, _currentHp - damage);
    }

    public void RestoreHp(int amount, int maxHp)
    {
        _currentHp = System.Math.Min(maxHp, _currentHp + amount);
    }
}
