// ═══════════════════════════════════════════════════════════════════
//  EnemyController.cs
//  책임: 적 HP 관리, 피해 수신, 사망 처리
//
//  알지 말아야 할 것:
//    • 플레이어 구현 세부사항
//    • 공격 패턴 계산 (AttackPattern 담당)
//    • 던전 생성 로직
// ═══════════════════════════════════════════════════════════════════

using System;
using UnityEngine;

public class EnemyController : MonoBehaviour, IDamageable
{
    [Header("Data")]
    public EnemyData data;

    [Header("Events")]
    public CombatEventChannel combatChannel;

    private int             _currentHp;
    private EnemyHealthBar  _healthBar;

    public bool IsAlive => _currentHp > 0;

    public event Action<EnemyController> OnDied;

    private void Awake()
    {
        _healthBar = GetComponent<EnemyHealthBar>();
        if (data != null)
        {
            _currentHp = data.maxHp;
            _healthBar?.SetHp(_currentHp, data.maxHp);
        }
    }

    /// <summary>프리팹 풀에서 꺼낼 때 데이터를 주입합니다.</summary>
    public void Initialize(EnemyData enemyData)
    {
        data       = enemyData;
        _currentHp = data.maxHp;
        _healthBar?.SetHp(_currentHp, data.maxHp);
        gameObject.SetActive(true);
    }

    public void TakeDamage(int damage)
    {
        if (!IsAlive) return;

        int actual = Mathf.Max(1, damage - (data?.defense ?? 0));
        _currentHp = Mathf.Max(0, _currentHp - actual);
        _healthBar?.SetHp(_currentHp, data.maxHp);

#if UNITY_EDITOR
        Debug.Log($"[Enemy:{data?.enemyName}] -{actual} HP → {_currentHp}/{data?.maxHp}");
#endif

        if (_currentHp == 0) Die();
    }

    private void Die()
    {
        combatChannel?.RaiseEnemyKilled(this);
        OnDied?.Invoke(this);
#if UNITY_EDITOR
        Debug.Log($"[Enemy:{data?.enemyName}] 사망");
#endif
        gameObject.SetActive(false);
    }
}
