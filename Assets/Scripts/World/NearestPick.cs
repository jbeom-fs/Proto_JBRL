using UnityEngine;

/// <summary>
/// 한 ring 안에서 desired에 가장 가까운 후보 하나를 고르는 소형 집계기입니다.
///
/// Area / Dungeon 어느 백엔드도 알지 못하며 월드 좌표만 비교합니다.
/// 따라서 두 백엔드의 Y 순회 방향이 서로 반대여도 결과가 달라지지 않습니다.
///
/// 거리가 같은 후보가 여러 개면 먼저 Consider된 쪽을 유지합니다.
/// 기존 순회 순서를 동점 tiebreak로 함께 보존하면서 거리만 최소화하기 위함입니다.
///
/// struct이므로 힙 할당은 없습니다.
/// </summary>
internal struct NearestPick
{
    private Vector3 _best;
    private float _bestSqr;
    private bool _has;

    /// <summary>후보가 하나라도 채택되었는지 여부입니다.</summary>
    public bool Has => _has;

    /// <summary>현재까지 desired에 가장 가까운 후보입니다. Has가 false면 의미 없습니다.</summary>
    public Vector3 Best => _best;

    public void Consider(Vector3 world, Vector3 desired)
    {
        float sqr = (world - desired).sqrMagnitude;
        if (_has && sqr >= _bestSqr)
            return;

        _best = world;
        _bestSqr = sqr;
        _has = true;
    }
}
