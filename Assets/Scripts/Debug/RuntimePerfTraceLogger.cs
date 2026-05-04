using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum ProjectileReleaseReason
{
    LifetimeExpired,
    PlayerHit,
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

    private static readonly StringBuilder s_Buffer = new(32 * 1024);

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

    public static bool Enabled { get; set; }
    public static bool IsEnabled => Enabled && s_Started;

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
    }

    private void Awake()
    {
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

    public static void RecordPoolGet(string prefabName, bool instantiated, int activeCount, int inactiveCount)
    {
        if (!IsEnabled)
            return;

        s_Frame.Spawned++;
        s_Frame.ActiveProjectiles = activeCount;
        s_Frame.HasActiveProjectileSample = true;
        s_CurrentActiveProjectiles = activeCount;
        s_TotalSpawned++;
        if (instantiated)
        {
            s_Frame.Instantiated++;
            s_TotalInstantiated++;
            s_Buffer.Append("[PoolEvent] frame=").Append(Time.frameCount)
                .Append(" prefab=").Append(prefabName)
                .Append(" action=Instantiate active=").Append(activeCount)
                .Append(" inactive=").Append(inactiveCount)
                .AppendLine();
        }
    }

    public static void RecordPoolReturn(ProjectileReleaseReason reason, int activeCount, int inactiveCount)
    {
        if (!IsEnabled)
            return;

        s_Frame.Returned++;
        s_Frame.ActiveProjectiles = activeCount;
        s_Frame.HasActiveProjectileSample = true;
        s_CurrentActiveProjectiles = activeCount;
        s_TotalReturned++;

        s_Buffer.Append("[ReturnEvent] frame=").Append(Time.frameCount)
            .Append(" reason=").Append(reason)
            .Append(" active=").Append(activeCount)
            .Append(" inactive=").Append(inactiveCount)
            .AppendLine();
    }

    public static void RecordFireEvent(EnemyData data, int requestedProjectiles, long elapsedTicks)
    {
        if (!IsEnabled || data == null)
            return;

        s_Frame.EnemyFireTicks += elapsedTicks;
        s_TotalFireRequests += requestedProjectiles;
        s_Buffer.Append("[FireEvent] frame=").Append(Time.frameCount)
            .Append(" enemy=").Append(data.enemyName)
            .Append(" pattern=").Append(data.firePattern)
            .Append(" count=").Append(requestedProjectiles)
            .Append(" speed=").Append(data.projectileSpeed)
            .Append(" lifetime=").Append(data.projectileLifetime)
            .Append(" wallMode=").Append(data.projectileWallHitMode)
            .Append(" prewarm=").Append(data.projectilePrewarmCount)
            .Append(" windup=").Append(data.attackWindup)
            .Append(" recovery=").Append(data.attackRecovery)
            .Append(" cooldown=").Append(data.attackCooldown)
            .AppendLine();
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
            .Append("logPath=").Append(s_LogPath).AppendLine();

        UnityEngine.Debug.Log($"[RuntimePerfTraceLogger] Writing projectile perf log to: {s_LogPath}");
    }

    private void EndFrame()
    {
        float frameMs = Time.unscaledDeltaTime * 1000f;
        float projectileUpdateMs = TicksToMs(s_Frame.ProjectileUpdateTicks);
        float hitMs = TicksToMs(s_Frame.HitTicks);
        float wallMs = TicksToMs(s_Frame.WallTicks);
        float moveMs = TicksToMs(s_Frame.MoveTicks);
        float bounceMs = TicksToMs(s_Frame.BounceTicks);
        float enemyFireMs = TicksToMs(s_Frame.EnemyFireTicks);
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
            AppendFrameSummary(frameMs, projectileUpdateMs, hitMs, wallMs, moveMs, bounceMs, enemyFireMs);
            s_LastSummaryFrame = Time.frameCount;
        }

        if (isSpike)
        {
            s_SpikeCount++;
            s_Buffer.Append("[Spike] frame=").Append(Time.frameCount)
                .Append(" dtMs=").Append(frameMs.ToString("F3"))
                .Append(" activeProjectiles=").Append(s_Frame.ActiveProjectiles)
                .Append(" projectileUpdateMs=").Append(projectileUpdateMs.ToString("F3"))
                .Append(" hitMs=").Append(hitMs.ToString("F3"))
                .Append(" wallMs=").Append(wallMs.ToString("F3"))
                .Append(" moveMs=").Append(moveMs.ToString("F3"))
                .Append(" bounceMs=").Append(bounceMs.ToString("F3"))
                .Append(" enemyFireMs=").Append(enemyFireMs.ToString("F3"))
                .Append(" spawned=").Append(s_Frame.Spawned)
                .Append(" returned=").Append(s_Frame.Returned)
                .Append(" instantiated=").Append(s_Frame.Instantiated)
                .AppendLine();
        }

        if (Time.frameCount - s_LastFlushFrame >= Mathf.Max(1, flushFrameInterval))
        {
            Flush();
            s_LastFlushFrame = Time.frameCount;
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
        float enemyFireMs)
    {
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
            .AppendLine();
    }

    private static float TicksToMs(long ticks)
    {
        return ticks <= 0L ? 0f : (float)(ticks * 1000.0 / Stopwatch.Frequency);
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
