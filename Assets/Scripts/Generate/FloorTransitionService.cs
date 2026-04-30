// ═══════════════════════════════════════════════════════════════════
//  FloorTransitionService.cs
//  Domain Layer — 층 전환 안정화 시퀀스 전담
//
//  책임:
//    • 던전 생성 완료 후 Unity Tilemap/렌더러가 안정될 때까지 대기합니다.
//    • 1프레임 대기 → 선택적 GC → settle 시간 → settle 프레임 순서를 보장합니다.
//    • 상태를 보유하지 않는 순수 IEnumerator 서비스입니다.
//    • _isTransitioning, floor, 이벤트 발행, 로딩 화면에 관여하지 않습니다.
// ═══════════════════════════════════════════════════════════════════

using System.Collections;
using System.Globalization;
using UnityEngine;

public class FloorTransitionService
{
    /// <summary>
    /// 던전 생성 완료 직후 Unity 안정화 대기 시퀀스를 실행합니다.
    /// DungeonManager.FloorTransition()이 yield return으로 호출합니다.
    /// </summary>
    public IEnumerator RunPostGenerateSettle(
        float settleSeconds,
        int   settleFrames,
        bool  allowGc,
        bool  collectGc,
        int   gcPasses,
        bool  waitFinalizers,
        int   floorForLog)
    {
        // 3. Unity가 Tilemap 업데이트를 완료할 시간 확보
        double stageStart = Time.realtimeSinceStartupAsDouble;
        yield return null;
        RuntimePerfLogger.MarkEvent("floor_transition_post_generate_frame",
            "elapsedMs=" + ElapsedMs(stageStart));

        if (allowGc && collectGc && gcPasses > 0)
        {
            stageStart = Time.realtimeSinceStartupAsDouble;
            RuntimePerfLogger.MarkEvent("floor_transition_gc_begin",
                "floor=" + floorForLog +
                " passes=" + gcPasses +
                " waitFinalizers=" + waitFinalizers);

            for (int i = 0; i < gcPasses; i++)
                System.GC.Collect();

            if (waitFinalizers)
                System.GC.WaitForPendingFinalizers();

            RuntimePerfLogger.MarkEvent("floor_transition_gc_end",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " passes=" + gcPasses);
        }

        if (settleSeconds > 0f)
        {
            stageStart = Time.realtimeSinceStartupAsDouble;
            RuntimePerfLogger.MarkEvent("floor_transition_settle_time_begin",
                "seconds=" + settleSeconds.ToString("F3", CultureInfo.InvariantCulture));
            yield return YieldCache.WaitForSecondsRealTime(settleSeconds);
            RuntimePerfLogger.MarkEvent("floor_transition_settle_time_end",
                "elapsedMs=" + ElapsedMs(stageStart) +
                " dtMs=" + (Time.unscaledDeltaTime * 1000f).ToString("F3", CultureInfo.InvariantCulture));
        }

        for (int i = 0; i < settleFrames; i++)
        {
            stageStart = Time.realtimeSinceStartupAsDouble;
            RuntimePerfLogger.MarkEvent("floor_transition_settle_frame_begin",
                "index=" + i);
            yield return null;
            RuntimePerfLogger.MarkEvent("floor_transition_settle_frame",
                "index=" + i +
                " elapsedMs=" + ElapsedMs(stageStart) +
                " dtMs=" + (Time.unscaledDeltaTime * 1000f).ToString("F3", CultureInfo.InvariantCulture));
        }
    }

    private static string ElapsedMs(double startTime)
        => ((Time.realtimeSinceStartupAsDouble - startTime) * 1000.0)
            .ToString("F3", CultureInfo.InvariantCulture);
}
