using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추적 상태 전용 로직입니다.
/// A*는 이 상태가 활성화되어 있을 때만 pathUpdateInterval 주기로 실행됩니다.
/// 복도 추적 버그를 막기 위해 목표 좌표는 EnemyBrain.TargetHandler가 제공하는 전역 그리드 좌표를 사용합니다.
/// </summary>
public sealed class ChaseState : IEnemyState
{
    private readonly EnemyBrain _brain;
    private readonly AStarPathfinder _pathfinder = new AStarPathfinder();
    private readonly List<Vector2Int> _path = new List<Vector2Int>(64);

    private bool _active;
    private float _pathTimer;
    private int _waypointIndex;
    private Vector2Int _lastStart;
    private Vector2Int _lastGoal;

    public ChaseState(EnemyBrain brain)
    {
        _brain = brain;
    }

    public void OnEnter()
    {
        _active = true;
        _pathTimer = 0f;
        _waypointIndex = 0;
        _lastStart = new Vector2Int(int.MinValue, int.MinValue);
        _lastGoal = new Vector2Int(int.MinValue, int.MinValue);
        _path.Clear();
    }

    public void OnExit()
    {
        // Chase를 벗어나는 순간 경로 데이터와 이동 상태를 정리합니다.
        // AttackState로 넘어갈 때 이전 waypoint가 남아 다시 움직이는 상황을 막습니다.
        _active = false;
        _pathTimer = 0f;
        _waypointIndex = 0;
        _path.Clear();
        _brain.StopMoving();
    }

    public void Tick(float sqrDistanceToPlayer)
    {
        if (!_active) return;

        bool canAttackNow = _brain.CanAttack(sqrDistanceToPlayer);
        if (sqrDistanceToPlayer < 16f)
            EnemyAIDebugLogWriter.Log($"[ChaseState] enemy={_brain.name}, state={_brain.CurrentState}, sqrDist={sqrDistanceToPlayer:F3}, canAttack={canAttackNow}");

        if (canAttackNow)
        {
            EnemyAIDebugLogWriter.Log($"[ChaseState] AttackState transition enemy={_brain.name}, state={_brain.CurrentState}, sqrDist={sqrDistanceToPlayer:F3}");
            _brain.ChangeState(EnemyAIStateId.Attack);
            return;
        }

        if (!_brain.ShouldKeepChasing(sqrDistanceToPlayer))
        {
            _brain.ChangeState(EnemyAIStateId.Idle);
            return;
        }

        // EMPTY(0) 벽이 없는 직선 시야가 확보되면 A*를 사용하지 않고 바로 이동합니다.
        // 방과 복도를 가로지르는 상황에서도 시야가 뚫려 있으면 탐색 비용을 아낍니다.
        if (_brain.HasLineOfSightToPlayer())
        {
            ClearPathOnly();
            _brain.DirectMoveToPlayer();
            return;
        }

        UpdatePathIfNeeded();
        MoveAlongPath();
    }

    private void UpdatePathIfNeeded()
    {
        _pathTimer -= Time.deltaTime;

        Vector2Int start = _brain.GridPosition;
        Vector2Int goal = _brain.PlayerGridPosition;

        bool endpointChanged = start != _lastStart || goal != _lastGoal;
        if (_path.Count > 0 && _pathTimer > 0f && !endpointChanged)
            return;

        _pathTimer = Mathf.Max(0.05f, _brain.pathUpdateInterval);
        _lastStart = start;
        _lastGoal = goal;

        if (_pathfinder.FindPath(_brain.DungeonData, start, goal, _path))
        {
            // 경로의 0번은 보통 현재 적 위치입니다. 이미 밟고 있는 지점은 건너뛰어 미세 진동을 줄입니다.
            _waypointIndex = _path.Count > 1 ? 1 : 0;
        }
        else
        {
            _waypointIndex = 0;
            _path.Clear();
            _brain.StopMoving();
        }
    }

    private void MoveAlongPath()
    {
        if (_path.Count == 0 || _waypointIndex >= _path.Count)
        {
            _brain.StopMoving();
            return;
        }

        Vector3 target = _brain.GridToWorld(_path[_waypointIndex]);
        float reachSqr = _brain.waypointReachDistance * _brain.waypointReachDistance;

        while (_waypointIndex < _path.Count &&
               (target - _brain.transform.position).sqrMagnitude <= reachSqr)
        {
            _waypointIndex++;
            if (_waypointIndex < _path.Count)
                target = _brain.GridToWorld(_path[_waypointIndex]);
        }

        if (_waypointIndex >= _path.Count)
        {
            _brain.StopMoving();
            return;
        }

        _brain.MoveToward(target);
    }

    private void ClearPathOnly()
    {
        if (_path.Count == 0) return;

        _path.Clear();
        _waypointIndex = 0;
        _pathTimer = 0f;
    }
}
