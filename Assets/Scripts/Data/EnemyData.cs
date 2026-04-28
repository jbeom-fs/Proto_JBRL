using UnityEngine;

/// <summary>
/// 적 데이터 — EnemyController에 할당합니다.
/// Assets > Create > JBLogLike > Enemy 로 생성합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewEnemy", menuName = "JBLogLike/Enemy")]
public class EnemyData : ScriptableObject
{
    [Header("기본 정보")]
    public string enemyName = "슬라임";

    [Header("전투 스탯")]
    public int maxHp     = 5;
    public int attack    = 2;
    public int defense   = 0;
    public int expReward = 10;

    [Header("AI 행동")]
    [Tooltip("플레이어 감지 거리 (월드 단위)")]
    public float detectRange    = 5f;

    [Tooltip("근접 공격이 닿는 거리 (월드 단위). detectRange보다 작아야 합니다.")]
    public float attackRange    = 1.2f;

    [Tooltip("이동 속도 (월드 단위/초)")]
    public float moveSpeed      = 2f;

    [Tooltip("공격 후 다음 공격까지 대기 시간 (초)")]
    public float attackCooldown = 1.5f;

    [Tooltip("공격 선딜레이 (초). 이 시간 동안 플레이어가 피할 수 있습니다.")]
    public float attackWindup   = 0.4f;
}
