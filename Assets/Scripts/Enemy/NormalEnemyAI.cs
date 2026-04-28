using System;

/// <summary>
/// 기존 프리팹/씬에 붙어 있는 NormalEnemyAI 컴포넌트의 호환성을 유지하기 위한 래퍼입니다.
/// 실제 일반 몬스터 구현은 NormalEnemyBrain에 있으며, 새 적에는 NormalEnemyBrain을 직접 붙이면 됩니다.
/// </summary>
[Obsolete("Use NormalEnemyBrain instead. This wrapper remains for existing prefabs.")]
public class NormalEnemyAI : NormalEnemyBrain
{
}
