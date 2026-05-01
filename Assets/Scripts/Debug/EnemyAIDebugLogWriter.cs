using System;
using System.IO;
using UnityEngine;

public static class EnemyAIDebugLogWriter
{
    private const string FileName = "EnemyAI_DebugLog.txt";

    private static bool s_initialized;
    private static bool s_fileIoWarningLogged;

    public static string LogFilePath => Path.Combine(Application.persistentDataPath, FileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        s_initialized = false;
        s_fileIoWarningLogged = false;
    }

    public static void Log(string message)
    {
        string line = $"[frame={Time.frameCount} time={Time.time:F3}] {message}";
        Debug.Log(line);

        EnsureInitialized();
        AppendLine(line);
    }

    private static void EnsureInitialized()
    {
        if (s_initialized) return;

        s_initialized = true;

        try
        {
            string directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(LogFilePath, string.Empty);
            Debug.Log($"[EnemyAI DebugLog] file={LogFilePath}");
            AppendLine($"[frame={Time.frameCount} time={Time.time:F3}] [EnemyAI DebugLog] file={LogFilePath}");
        }
        catch (Exception ex)
        {
            WarnFileIoFailed(ex);
        }
    }

    private static void AppendLine(string line)
    {
        try
        {
            File.AppendAllText(LogFilePath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            WarnFileIoFailed(ex);
        }
    }

    private static void WarnFileIoFailed(Exception ex)
    {
        if (s_fileIoWarningLogged) return;

        s_fileIoWarningLogged = true;
        Debug.LogWarning($"[EnemyAI DebugLog] file write failed: {ex.Message}");
    }
}
