using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 단일 elapsedMs metadata stage 측정을 위한 zero-alloc using-scope 헬퍼입니다.
///
/// 사용 예시:
///   using (PerfStage.Begin("place_tiles_stage_clear"))
///       ClearTilemaps();
///
/// 동작:
///   • RuntimePerfLogger.IsActive == false 일 때:
///       Begin은 default 구조체를 반환하며 Time.realtimeSinceStartupAsDouble 호출도 생략합니다.
///       Dispose는 _name == null 분기로 즉시 return → 문자열 조합과 MarkEvent 호출이 모두 생략됩니다.
///       alloc / GC 0, boxing 0 (concrete PerfStage 타입으로 using 하므로 constrained.callvirt IL 생성).
///   • RuntimePerfLogger.IsActive == true 일 때:
///       Begin에서 시작 시간을 측정하고, Dispose에서 elapsedMs를 F3 + InvariantCulture로
///       포맷한 "elapsedMs=X" 메시지를 RuntimePerfLogger.MarkEvent에 전달합니다.
///       이는 기존 ElapsedMs(stageStart) 헬퍼와 동일 포맷입니다.
///
/// 제약:
///   • 단일 elapsedMs metadata를 가진 stage 전용입니다.
///     elapsedMs 외 다른 metadata(total/visible/index/rows 등)가 필요한 stage는
///     기존 stageStart + RuntimePerfLogger.IsActive + MarkEvent 패턴을 유지하세요.
///   • readonly struct + readonly fields → defensive copy 없음.
/// </summary>
public readonly struct PerfStage : IDisposable
{
    private readonly string _name;
    private readonly double _start;

    public static PerfStage Begin(string name)
    {
        if (!RuntimePerfLogger.IsActive) return default;
        return new PerfStage(name, Time.realtimeSinceStartupAsDouble);
    }

    private PerfStage(string name, double start)
    {
        _name = name;
        _start = start;
    }

    public void Dispose()
    {
        if (_name == null) return;
        double ms = (Time.realtimeSinceStartupAsDouble - _start) * 1000.0;
        RuntimePerfLogger.MarkEvent(_name, "elapsedMs=" + ms.ToString("F3", CultureInfo.InvariantCulture));
    }
}
