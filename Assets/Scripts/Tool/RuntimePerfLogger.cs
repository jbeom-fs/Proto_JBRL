/// <summary>
/// 런타임 성능 로그 기능은 제거되었습니다.
/// 기존 코드의 호출 지점을 한 번에 모두 정리하기 전까지 컴파일 호환성만 유지하는 no-op API입니다.
/// 파일 생성, 샘플링, 문자열 버퍼, Profiler 조회를 전혀 수행하지 않으므로 플레이 중 GC를 만들지 않습니다.
/// </summary>
public static class RuntimePerfLogger
{
    public static string CurrentLogPath => string.Empty;
    public static bool IsActive => false;

    public static void MarkEvent(string eventName, string detail = "")
    {
    }
}
