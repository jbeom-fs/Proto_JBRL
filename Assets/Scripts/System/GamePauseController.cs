using UnityEngine;

public sealed class GamePauseController : MonoBehaviour
{
    private const int PauseSourceCount = 6;

    private static GamePauseController s_Active;

    [SerializeField] private bool restoreTimeScaleOnDisable = true;

    private readonly bool[] _pauseRequests = new bool[PauseSourceCount];
    private float _timeScaleBeforePause = 1f;
    private int _pauseRequestCount;
    private bool _hasAppliedPause;

    public static GamePauseController Active => s_Active;
    public static bool IsPaused => s_Active != null && s_Active.HasAnyPauseRequest;

    public bool HasAnyPauseRequest => _pauseRequestCount > 0;

    private void Awake()
    {
        if (s_Active != null && s_Active != this)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[GamePauseController] Duplicate instance detected. Latest instance is now active.", this);
#endif
        }

        s_Active = this;
        ApplyPauseState();
    }

    private void OnDisable()
    {
        if (s_Active != this)
            return;

        if (restoreTimeScaleOnDisable && _hasAppliedPause)
            Time.timeScale = _timeScaleBeforePause;

        s_Active = null;
        _hasAppliedPause = false;
    }

    public void Pause(GamePauseSource source)
    {
        int index = ToIndex(source);
        if (index < 0)
            return;

        if (_pauseRequests[index])
            return;

        _pauseRequests[index] = true;
        _pauseRequestCount++;
        ApplyPauseState();
    }

    public void Resume(GamePauseSource source)
    {
        int index = ToIndex(source);
        if (index < 0)
            return;

        if (!_pauseRequests[index])
            return;

        _pauseRequests[index] = false;
        _pauseRequestCount--;
        ApplyPauseState();
    }

    public bool IsPausedBy(GamePauseSource source)
    {
        int index = ToIndex(source);
        return index >= 0 && _pauseRequests[index];
    }

    private void ApplyPauseState()
    {
        if (_pauseRequestCount > 0)
        {
            if (!_hasAppliedPause)
            {
                _timeScaleBeforePause = Time.timeScale;
                _hasAppliedPause = true;
            }

            Time.timeScale = 0f;
            return;
        }

        if (_hasAppliedPause)
        {
            Time.timeScale = _timeScaleBeforePause;
            _hasAppliedPause = false;
        }
    }

    private static int ToIndex(GamePauseSource source)
    {
        int index = (int)source;
        if ((uint)index < PauseSourceCount)
            return index;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogWarning("[GamePauseController] Unknown pause source: " + source);
#endif
        return -1;
    }
}
