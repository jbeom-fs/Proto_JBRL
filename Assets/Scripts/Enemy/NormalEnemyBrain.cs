/// <summary>
/// 일반 몬스터용 경량 Brain입니다.
/// EnemyBrain의 기본 Handler와 기본 상태만 사용하므로 에픽/보스용 추가 상태나 패턴 메모리를 들고 있지 않습니다.
/// </summary>
public class NormalEnemyBrain : EnemyBrain
{
    // 일반 몬스터는 기본 Idle/Chase/Attack만 사용합니다.
    // EpicEnemyBrain, BossEnemyBrain은 이 클래스를 건너뛰고 EnemyBrain을 직접 상속해도 됩니다.
}
