using System;
using UnityEngine;

/// <summary>
/// 전투 이벤트 채널 — 기존 DungeonEventChannel 패턴과 동일한 ScriptableObject 방식.
/// Assets > Create > JBRogLike > Events > CombatEventChannel 로 생성합니다.
/// </summary>
[CreateAssetMenu(fileName = "CombatEventChannel", menuName = "JBRogLike/Events/CombatEventChannel")]
public class CombatEventChannel : ScriptableObject
{
    public event Action<EnemyController> OnEnemyKilled;
    public event Action<int, int>        OnPlayerHpChanged;  // (current, max)
    public event Action<PlayerCombatController> OnPlayerDied;
    public event Action<SkillData>       OnSkillUsed;
    public event Action                  OnLoadoutChanged;

    public void RaiseEnemyKilled(EnemyController enemy)     => OnEnemyKilled?.Invoke(enemy);
    public void RaisePlayerHpChanged(int cur, int max)      => OnPlayerHpChanged?.Invoke(cur, max);
    public void RaisePlayerDied(PlayerCombatController player) => OnPlayerDied?.Invoke(player);
    public void RaiseSkillUsed(SkillData skill)             => OnSkillUsed?.Invoke(skill);
    public void RaiseLoadoutChanged()                       => OnLoadoutChanged?.Invoke();
}
