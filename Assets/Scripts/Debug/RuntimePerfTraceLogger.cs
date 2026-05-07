using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum ProjectileReleaseReason
{
    LifetimeExpired,
    PlayerHit,
    EnemyHit,
    WallHitDestroy,
    BounceLimit,
    Manual,
    FallbackDestroy
}

public class RuntimePerfTraceLogger : MonoBehaviour
{
    [SerializeField] private bool enableOnAwake;
    [SerializeField] private float spikeFrameMsThreshold = 33.3f;
    [SerializeField] private int summaryFrameInterval = 60;
    [SerializeField] private int flushFrameInterval = 300;

    [Header("Lightweight Mode")]
    [SerializeField] private bool logFrameSummary = true;
    [SerializeField] private bool logSpike = true;
    [SerializeField] private bool logFireEvents = false;
    [SerializeField] private bool logPoolEvents = false;
    [SerializeField] private bool logReturnEvents = false;

    private static readonly StringBuilder s_Buffer = new(32 * 1024);

    private static readonly ProfilerMarker s_AppendMarker = new ProfilerMarker("RuntimePerfLogger.Append");
    private static readonly ProfilerMarker s_FlushMarker = new ProfilerMarker("RuntimePerfLogger.Flush");

    private static string s_LogPath;
    private static bool s_Started;
    private static int s_LastSummaryFrame;
    private static int s_LastFlushFrame;
    private static float s_SessionStartTime;
    private static float s_MaxFrameMs;
    private static float s_MaxProjectileUpdateMs;
    private static float s_MaxHitCheckMs;
    private static float s_MaxWallCheckMs;
    private static int s_MaxActiveProjectiles;
    private static int s_SpikeCount;
    private static int s_TotalSpawned;
    private static int s_TotalReturned;
    private static int s_TotalInstantiated;
    private static int s_TotalFireRequests;
    private static int s_CurrentActiveProjectiles;

    private static FrameStats s_Frame;

    private static bool s_LogFrameSummary = true;
    private static bool s_LogSpike = true;
    private static bool s_LogFireEvents;
    private static bool s_LogPoolEvents;
    private static bool s_LogReturnEvents;

    public static bool Enabled { get; set; }
    public static bool IsEnabled => Enabled && s_Started;

    public static bool LogFrameSummary { get => s_LogFrameSummary; set => s_LogFrameSummary = value; }
    public static bool LogSpike { get => s_LogSpike; set => s_LogSpike = value; }
    public static bool LogFireEvents { get => s_LogFireEvents; set => s_LogFireEvents = value; }
    public static bool LogPoolEvents { get => s_LogPoolEvents; set => s_LogPoolEvents = value; }
    public static bool LogReturnEvents { get => s_LogReturnEvents; set => s_LogReturnEvents = value; }

    private struct FrameStats
    {
        public int Spawned;
        public int Returned;
        public int Instantiated;
        public int ActiveProjectiles;
        public bool HasActiveProjectileSample;
        public long ProjectileUpdateTicks;
        public long MoveTicks;
        public long WallTicks;
        public long HitTicks;
        public long BounceTicks;
        public long EnemyFireTicks;
        public long ReleaseTicks;
        public long ReleaseCallbackTicks;
        public long PoolGetTicks;
        public long PoolGetSetActiveOnTicks;
        public long PoolGetInstantiateTicks;
        public long PoolReturnTicks;
        public long PoolReturnSetActiveOffTicks;
        public long LoggerAppendTicks;
        public long LoggerFlushTicks;
    }

    private void Awake()
    {
        s_LogFrameSummary = logFrameSummary;
        s_LogSpike = logSpike;
        s_LogFireEvents = logFireEvents;
        s_LogPoolEvents = logPoolEvents;
        s_LogReturnEvents = logReturnEvents;

        if (!enableOnAwake)
            return;

        Configure(true, spikeFrameMsThreshold, summaryFrameInterval, flushFrameInterval);
    }

    private void LateUpdate()
    {
        if (!IsEnabled)
            return;

        EndFrame();
    }

    private void OnApplicationQuit()
    {
        FlushSessionSummary();
    }

    private void OnDisable()
    {
        Flush();
    }

    public static void Configure(
        bool enabled,
        float spikeThresholdMs = 33.3f,
        int summaryInterval = 60,
        int flushInterval = 300)
    {
        Enabled = enabled;
        if (!Enabled)
            return;

        EnsureStarted(spikeThresholdMs, summaryInterval, flushInterval);
    }

    public static long Timestamp()
    {
        return IsEnabled ? Stopwatch.GetTimestamp() : 0L;
    }

    public static void RecordProjectileUpdate(long updateTicks, long moveTicks, long wallTicks, long hitTicks, long bounceTicks)
    {
        if (!IsEnabled)
            return;

        s_Frame.ProjectileUpdateTicks += updateTicks;
        s_Frame.MoveTicks += moveTicks;
        s_Frame.WallTicks += wallTicks;
        s_Frame.HitTicks += hitTicks;
        s_Frame.BounceTicks += bounceTicks;
    }

    public static void RecordRelease(long totalTicks, long callbackTicks)
    {
        if (!IsEnabled)
            return;

        s_Frame.ReleaseTicks += totalTicks;
        s_Frame.ReleaseCallbackTicks += callbackTicks;
    }

    public static void RecordPoolGet(
        string prefabName,
        bool instantiated,
        int activeCount,
        int inactiveCount,
        long totalTicks,
        long instantiateTicks,
        long setActiveOnTicks)
    {
        if (!IsEnabled)
            return;

        s_Frame.Spawned++;
        s_Frame.ActiveProjectiles = activeCount;
        s_Frame.HasActiveProjectileSample = true;
        s_CurrentActiveProjectiles = activeCount;
        s_TotalSpawned++;
        s_Frame.PoolGetTicks += totalTicks;
        s_Frame.PoolGetInstantiateTicks += instantiateTicks;
        s_Frame.PoolGetSetActiveOnTicks += setActiveOnTicks;

        if (instantiated)
        {
            s_Frame.Instantiated++;
            s_TotalInstantiated++;
        }

        if (!s_LogPoolEvents || !instantiated)
            return;

        long appendStart = Stopwatch.GetTimestamp();
        s_AppendMarker.Begin();
        s_Buffer.Append("[PoolEvent] frame=").Append(Time.frameCount)
            .Append(" prefab=").Append(prefabName)
            .Append(" action=Instantiate active=").Append(activeCount)
            .Append(" inactive=").Append(inactiveCount)
            .AppendLine();
        s_AppendMarker.End();
        s_Frame.LoggerAppendTicks += Stopwatch.GetTimestamp() - appendStart;
    }

    public static void RecordPoolReturn(
        ProjectileReleaseReason reason,
        int activeCount,
        int inactiveCount,
        long totalTicks,
        long setActiveOffTicks)
    {
        if (!IsEnabled)
            return;

        s_Frame.Returned++;
        s_Frame.ActiveProjectiles = activeCount;
        s_Frame.HasActiveProjectileSample = true;
        s_CurrentActiveProjectiles = activeCount;
        s_TotalReturned++;
        s_Frame.PoolReturnTicks += totalTicks;
        s_Frame.PoolReturnSetActiveOffTicks += setActiveOffTicks;

        if (!s_LogReturnEvents)
            return;

        long appendStart = Stopwatch.GetTimestamp();
        s_AppendMarker.Begin();
        s_Buffer.Append("[ReturnEvent] frame=").Append(Time.frameCount)
            .Append(" reason=").Append(ReasonToString(reason))
            .Append(" active=").Append(activeCount)
            .Append(" inactive=").Append(inactiveCount)
            .AppendLine();
        s_AppendMarker.End();
        s_Frame.LoggerAppendTicks += Stopwatch.GetTimestamp() - appendStart;
    }

    public static void RecordFireEvent(EnemyData data, int requestedProjectiles, long elapsedTicks)
    {
        if (!IsEnabled || data == null)
            return;

        s_Frame.EnemyFireTicks += elapsedTicks;
        s_TotalFireRequests += requestedProjectiles;

        if (!s_LogFireEvents)
            return;

        long appendStart = Stopwatch.GetTimestamp();
        s_AppendMarker.Begin();
        s_Buffer.Append("[FireEvent] frame=").Append(Time.frameCount)
            .Append(" enemy=").Append(data.enemyName)
            .Append(" pattern=").Append(FirePatternToString(data.firePattern))
            .Append(" count=").Append(requestedProjectiles)
            .Append(" speed=").Append(data.projectileSpeed)
            .Append(" lifetime=").Append(data.projectileLifetime)
            .Append(" wallMode=").Append(WallHitModeToString(data.projectileWallHitMode))
            .Append(" windup=").Append(data.attackWindup)
            .Append(" recovery=").Append(data.attackRecovery)
            .Append(" cooldown=").Append(data.attackCooldown)
            .AppendLine();
        s_AppendMarker.End();
        s_Frame.LoggerAppendTicks += Stopwatch.GetTimestamp() - appendStart;
    }

    private static void EnsureStarted(float spikeThresholdMs, int summaryInterval, int flushInterval)
    {
        if (s_Started)
            return;

        s_Started = true;
        s_SessionStartTime = Time.realtimeSinceStartup;
        s_LastSummaryFrame = Time.frameCount;
        s_LastFlushFrame = Time.frameCount;
        s_MaxFrameMs = 0f;
        s_MaxProjectileUpdateMs = 0f;
        s_MaxHitCheckMs = 0f;
        s_MaxWallCheckMs = 0f;
        s_MaxActiveProjectiles = 0;
        s_SpikeCount = 0;
        s_TotalSpawned = 0;
        s_TotalReturned = 0;
        s_TotalInstantiated = 0;
        s_TotalFireRequests = 0;
        s_CurrentActiveProjectiles = 0;
        s_Frame = default;

        string directory = Path.Combine(Application.persistentDataPath, "PerfLogs");
        Directory.CreateDirectory(directory);
        string fileName = "projectile_perf_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
        s_LogPath = Path.Combine(directory, fileName);

        s_Buffer.AppendLine("[Session]")
            .Append("startTime=").Append(DateTime.Now.ToString("O")).AppendLine()
            .Append("scene=").Append(SceneManager.GetActiveScene().name).AppendLine()
            .Append("targetFrameRate=").Append(Application.targetFrameRate).AppendLine()
            .Append("spikeFrameMsThreshold=").Append(spikeThresholdMs).AppendLine()
            .Append("summaryFrameInterval=").Append(Mathf.Max(1, summaryInterval)).AppendLine()
            .Append("flushFrameInterval=").Append(Mathf.Max(1, flushInterval)).AppendLine()
            .Append("logFrameSummary=").Append(s_LogFrameSummary).AppendLine()
            .Append("logSpike=").Append(s_LogSpike).AppendLine()
            .Append("logFireEvents=").Append(s_LogFireEvents).AppendLine()
            .Append("logPoolEvents=").Append(s_LogPoolEvents).AppendLine()
            .Append("logReturnEvents=").Append(s_LogReturnEvents).AppendLine()
            .Append("logPath=").Append(s_LogPath).AppendLine();

        UnityEngine.Debug.Log($"[RuntimePerfTraceLogger] Writing projectile perf log to: {s_LogPath}");
    }

    private void EndFrame()
    {
        // Phase 1: Flush at frame start so this frame's flush time is captured in this frame's summary.
        // The append from this frame's summary will be picked up by the next scheduled flush.
        if (Time.frameCount - s_LastFlushFrame >= Mathf.Max(1, flushFrameInterval))
        {
            long flushStart = Stopwatch.GetTimestamp();
            s_FlushMarker.Begin();
            Flush();
            s_FlushMarker.End();
            s_Frame.LoggerFlushTicks += Stopwatch.GetTimestamp() - flushStart;
            s_LastFlushFrame = Time.frameCount;
        }

        float frameMs = Time.unscaledDeltaTime * 1000f;
        float projectileUpdateMs = TicksToMs(s_Frame.ProjectileUpdateTicks);
        float hitMs = TicksToMs(s_Frame.HitTicks);
        float wallMs = TicksToMs(s_Frame.WallTicks);
        float moveMs = TicksToMs(s_Frame.MoveTicks);
        float bounceMs = TicksToMs(s_Frame.BounceTicks);
        float enemyFireMs = TicksToMs(s_Frame.EnemyFireTicks);
        float releaseMs = TicksToMs(s_Frame.ReleaseTicks);
        float releaseCallbackMs = TicksToMs(s_Frame.ReleaseCallbackTicks);
        float poolGetMs = TicksToMs(s_Frame.PoolGetTicks);
        float poolGetSetActiveOnMs = TicksToMs(s_Frame.PoolGetSetActiveOnTicks);
        float poolGetInstantiateMs = TicksToMs(s_Frame.PoolGetInstantiateTicks);
        float poolReturnMs = TicksToMs(s_Frame.PoolReturnTicks);
        float poolReturnSetActiveOffMs = TicksToMs(s_Frame.PoolReturnSetActiveOffTicks);
        float loggerAppendMs = TicksToMs(s_Frame.LoggerAppendTicks);
        float loggerFlushMs = TicksToMs(s_Frame.LoggerFlushTicks);

        if (!s_Frame.HasActiveProjectileSample)
            s_Frame.ActiveProjectiles = s_CurrentActiveProjectiles;

        s_MaxFrameMs = Mathf.Max(s_MaxFrameMs, frameMs);
        s_MaxProjectileUpdateMs = Mathf.Max(s_MaxProjectileUpdateMs, projectileUpdateMs);
        s_MaxHitCheckMs = Mathf.Max(s_MaxHitCheckMs, hitMs);
        s_MaxWallCheckMs = Mathf.Max(s_MaxWallCheckMs, wallMs);
        s_MaxActiveProjectiles = Mathf.Max(s_MaxActiveProjectiles, s_Frame.ActiveProjectiles);

        bool shouldSummarize = Time.frameCount - s_LastSummaryFrame >= Mathf.Max(1, summaryFrameInterval);
        bool isSpike = frameMs >= spikeFrameMsThreshold;

        if (shouldSummarize || isSpike)
        {
            if (s_LogFrameSummary || (isSpike && s_LogSpike))
            {
                AppendFrameSummary(frameMs, projectileUpdateMs, hitMs, wallMs, moveMs, bounceMs, enemyFireMs,
                    releaseMs, releaseCallbackMs, poolGetMs, poolGetSetActiveOnMs, poolGetInstantiateMs,
                    poolReturnMs, poolReturnSetActiveOffMs, loggerAppendMs, loggerFlushMs);
            }

            if (shouldSummarize)
                s_LastSummaryFrame = Time.frameCount;
        }

        if (isSpike)
        {
            s_SpikeCount++;
            if (s_LogSpike)
            {
                AppendSpike(frameMs, projectileUpdateMs, hitMs, wallMs, moveMs, bounceMs, enemyFireMs,
                    releaseMs, releaseCallbackMs, poolGetMs, poolGetSetActiveOnMs, poolGetInstantiateMs,
                    poolReturnMs, poolReturnSetActiveOffMs, loggerAppendMs, loggerFlushMs);
            }
        }

        s_Frame = default;
    }

    private static void AppendFrameSummary(
        float frameMs,
        float projectileUpdateMs,
        float hitMs,
        float wallMs,
        float moveMs,
        float bounceMs,
        float enemyFireMs,
        float releaseMs,
        float releaseCallbackMs,
        float poolGetMs,
        float poolGetSetActiveOnMs,
        float poolGetInstantiateMs,
        float poolReturnMs,
        float poolReturnSetActiveOffMs,
        float loggerAppendMs,
        float loggerFlushMs)
    {
        long appendStart = Stopwatch.GetTimestamp();
        s_AppendMarker.Begin();
        s_Buffer.Append("[FrameSummary] frame=").Append(Time.frameCount)
            .Append(" time=").Append(Time.time.ToString("F3"))
            .Append(" dtMs=").Append(frameMs.ToString("F3"))
            .Append(" activeProjectiles=").Append(s_Frame.ActiveProjectiles)
            .Append(" spawned=").Append(s_Frame.Spawned)
            .Append(" returned=").Append(s_Frame.Returned)
            .Append(" instantiated=").Append(s_Frame.Instantiated)
            .Append(" projectileUpdateMs=").Append(projectileUpdateMs.ToString("F3"))
            .Append(" hitMs=").Append(hitMs.ToString("F3"))
            .Append(" wallMs=").Append(wallMs.ToString("F3"))
            .Append(" moveMs=").Append(moveMs.ToString("F3"))
            .Append(" bounceMs=").Append(bounceMs.ToString("F3"))
            .Append(" enemyFireMs=").Append(enemyFireMs.ToString("F3"))
            .Append(" releaseMs=").Append(releaseMs.ToString("F3"))
            .Append(" releaseCallbackMs=").Append(releaseCallbackMs.ToString("F3"))
            .Append(" poolGetMs=").Append(poolGetMs.ToString("F3"))
            .Append(" poolGetSetActiveOnMs=").Append(poolGetSetActiveOnMs.ToString("F3"))
            .Append(" poolGetInstantiateMs=").Append(poolGetInstantiateMs.ToString("F3"))
            .Append(" poolReturnMs=").Append(poolReturnMs.ToString("F3"))
            .Append(" poolReturnSetActiveOffMs=").Append(poolReturnSetActiveOffMs.ToString("F3"))
            .Append(" loggerAppendMs=").Append(loggerAppendMs.ToString("F3"))
            .Append(" loggerFlushMs=").Append(loggerFlushMs.ToString("F3"))
            .AppendLine();
        s_AppendMarker.End();
        s_Frame.LoggerAppendTicks += Stopwatch.GetTimestamp() - appendStart;
    }

    private static void AppendSpike(
        float frameMs,
        float projectileUpdateMs,
        float hitMs,
        float wallMs,
        float moveMs,
        float bounceMs,
        float enemyFireMs,
        float releaseMs,
        float releaseCallbackMs,
        float poolGetMs,
        float poolGetSetActiveOnMs,
        float poolGetInstantiateMs,
        float poolReturnMs,
        float poolReturnSetActiveOffMs,
        float loggerAppendMs,
        float loggerFlushMs)
    {
        long appendStart = Stopwatch.GetTimestamp();
        s_AppendMarker.Begin();
        s_Buffer.Append("[Spike] frame=").Append(Time.frameCount)
            .Append(" dtMs=").Append(frameMs.ToString("F3"))
            .Append(" activeProjectiles=").Append(s_Frame.ActiveProjectiles)
            .Append(" spawned=").Append(s_Frame.Spawned)
            .Append(" returned=").Append(s_Frame.Returned)
            .Append(" instantiated=").Append(s_Frame.Instantiated)
            .Append(" projectileUpdateMs=").Append(projectileUpdateMs.ToString("F3"))
            .Append(" hitMs=").Append(hitMs.ToString("F3"))
            .Append(" wallMs=").Append(wallMs.ToString("F3"))
            .Append(" moveMs=").Append(moveMs.ToString("F3"))
            .Append(" bounceMs=").Append(bounceMs.ToString("F3"))
            .Append(" enemyFireMs=").Append(enemyFireMs.ToString("F3"))
            .Append(" releaseMs=").Append(releaseMs.ToString("F3"))
            .Append(" releaseCallbackMs=").Append(releaseCallbackMs.ToString("F3"))
            .Append(" poolGetMs=").Append(poolGetMs.ToString("F3"))
            .Append(" poolGetSetActiveOnMs=").Append(poolGetSetActiveOnMs.ToString("F3"))
            .Append(" poolGetInstantiateMs=").Append(poolGetInstantiateMs.ToString("F3"))
            .Append(" poolReturnMs=").Append(poolReturnMs.ToString("F3"))
            .Append(" poolReturnSetActiveOffMs=").Append(poolReturnSetActiveOffMs.ToString("F3"))
            .Append(" loggerAppendMs=").Append(loggerAppendMs.ToString("F3"))
            .Append(" loggerFlushMs=").Append(loggerFlushMs.ToString("F3"))
            .AppendLine();
        s_AppendMarker.End();
        s_Frame.LoggerAppendTicks += Stopwatch.GetTimestamp() - appendStart;
    }

    private static float TicksToMs(long ticks)
    {
        return ticks <= 0L ? 0f : (float)(ticks * 1000.0 / Stopwatch.Frequency);
    }

    private static string ReasonToString(ProjectileReleaseReason reason)
    {
        switch (reason)
        {
            case ProjectileReleaseReason.LifetimeExpired: return "LifetimeExpired";
            case ProjectileReleaseReason.PlayerHit: return "PlayerHit";
            case ProjectileReleaseReason.EnemyHit: return "EnemyHit";
            case ProjectileReleaseReason.WallHitDestroy: return "WallHitDestroy";
            case ProjectileReleaseReason.BounceLimit: return "BounceLimit";
            case ProjectileReleaseReason.Manual: return "Manual";
            case ProjectileReleaseReason.FallbackDestroy: return "FallbackDestroy";
            default: return "Unknown";
        }
    }

    private static string FirePatternToString(ProjectileFirePattern pattern)
    {
        switch (pattern)
        {
            case ProjectileFirePattern.Single: return "Single";
            case ProjectileFirePattern.Burst: return "Burst";
            case ProjectileFirePattern.Spread: return "Spread";
            case ProjectileFirePattern.Circle: return "Circle";
            default: return "Unknown";
        }
    }

    private static string WallHitModeToString(ProjectileWallHitMode mode)
    {
        switch (mode)
        {
            case ProjectileWallHitMode.Destroy: return "Destroy";
            case ProjectileWallHitMode.PassThrough: return "PassThrough";
            case ProjectileWallHitMode.Bounce: return "Bounce";
            default: return "Unknown";
        }
    }

    private static void FlushSessionSummary()
    {
        if (!s_Started)
            return;

        s_Buffer.AppendLine("[SessionSummary]")
            .Append("duration=").Append((Time.realtimeSinceStartup - s_SessionStartTime).ToString("F3")).AppendLine()
            .Append("maxFrameMs=").Append(s_MaxFrameMs.ToString("F3")).AppendLine()
            .Append("maxActiveProjectiles=").Append(s_MaxActiveProjectiles).AppendLine()
            .Append("totalSpawned=").Append(s_TotalSpawned).AppendLine()
            .Append("totalReturned=").Append(s_TotalReturned).AppendLine()
            .Append("totalInstantiated=").Append(s_TotalInstantiated).AppendLine()
            .Append("totalFireRequests=").Append(s_TotalFireRequests).AppendLine()
            .Append("maxProjectileUpdateMs=").Append(s_MaxProjectileUpdateMs.ToString("F3")).AppendLine()
            .Append("maxHitCheckMs=").Append(s_MaxHitCheckMs.ToString("F3")).AppendLine()
            .Append("maxWallCheckMs=").Append(s_MaxWallCheckMs.ToString("F3")).AppendLine()
            .Append("spikeCount=").Append(s_SpikeCount).AppendLine();

        Flush();
    }

    private static void Flush()
    {
        if (!s_Started || s_Buffer.Length == 0 || string.IsNullOrEmpty(s_LogPath))
            return;

        File.AppendAllText(s_LogPath, s_Buffer.ToString());
        s_Buffer.Clear();
    }
}
