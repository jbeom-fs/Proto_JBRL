public class PlayerResource
{
    private int _currentHp;
    private int _currentMp;

    public bool IsAlive => _currentHp > 0;
    public int CurrentHp => _currentHp;
    public int CurrentMp => _currentMp;

    public void Initialize(int maxHp, int maxMp)
    {
        _currentHp = maxHp;
        _currentMp = maxMp;
    }

    public void TakeDamage(int damage)
    {
        _currentHp = System.Math.Max(0, _currentHp - damage);
    }

    public void RestoreHp(int amount, int maxHp)
    {
        _currentHp = System.Math.Min(maxHp, _currentHp + amount);
    }

    public void SpendMp(int amount)
    {
        _currentMp = System.Math.Max(0, _currentMp - amount);
    }

    public void RestoreMp(int amount, int maxMp)
    {
        _currentMp = System.Math.Min(maxMp, _currentMp + amount);
    }
}
