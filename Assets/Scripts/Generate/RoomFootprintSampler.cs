using UnityEngine;

/// <summary>
/// 방 진입/overlap 검증을 위한 공용 9-sample footprint 생성기입니다.
/// PlayerController(RoomInfo 후보 선정)와 DungeonTilemapRenderer(특정 room overlap 검증) 둘 다
/// 같은 sample 배치를 사용하지만, 후속 카운트 로직은 호출자가 자체 의미에 맞게 유지합니다.
/// </summary>
public static class RoomFootprintSampler
{
    /// <summary>
    /// center를 제외한 8방향 sample 중 이 수 이상이 조건을 만족하면 룸 안에 있다고 판단합니다.
    /// </summary>
    public const int Threshold = 3;

    /// <summary>center + 8방향 = 9개 샘플 포인트.</summary>
    public const int SampleCount = 9;

    /// <summary>
    /// buffer[0..8]에 center + 8방향 sample을 채웁니다. buffer.Length는 SampleCount 이상이어야 합니다.
    /// 인덱스 순서: 0=center, 1=left, 2=right, 3=up, 4=down,
    /// 5=down-left, 6=down-right, 7=up-left, 8=up-right.
    /// 호출자가 sample 인덱스의 의미에 의존한다면 이 순서를 유지하세요.
    /// </summary>
    public static void FillSamples(Vector3[] buffer, Vector3 center, float radius)
    {
        buffer[0] = center;
        buffer[1] = center + new Vector3(-radius, 0f, 0f);
        buffer[2] = center + new Vector3(radius, 0f, 0f);
        buffer[3] = center + new Vector3(0f, radius, 0f);
        buffer[4] = center + new Vector3(0f, -radius, 0f);
        buffer[5] = center + new Vector3(-radius, -radius, 0f);
        buffer[6] = center + new Vector3(radius, -radius, 0f);
        buffer[7] = center + new Vector3(-radius, radius, 0f);
        buffer[8] = center + new Vector3(radius, radius, 0f);
    }
}
