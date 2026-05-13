using UnityEngine;

/// <summary>
/// Renderer.enabled를 FogOfWarController의 visible 상태에 맞춰 토글하는 범용 컴포넌트.
/// Enemy / Enemy projectile / 적 attack effect 등에 prefab 단위로 부착해 사용한다.
///
/// 게임플레이 로직(AI, 충돌, 데미지, lifetime, pool)에는 영향을 주지 않는다.
/// 매 프레임 GetComponent / FindAnyObjectByType / 매 프레임 allocation을 사용하지 않는다.
/// </summary>
public sealed class FogVisibilityRenderer : MonoBehaviour
{
    [Tooltip("비워두면 Awake에서 자동 캐시한다. 명시적으로 지정하면 그 배열만 사용한다.")]
    [SerializeField] private Renderer[] managedRenderers;

    [Tooltip("자동 캐시 시 자식까지 포함할지 여부.")]
    [SerializeField] private bool includeChildrenOnAutoCache = true;

    [Tooltip("FogOfWarController가 없을 때 숨길지 여부. 기본 false (테스트 씬에서 표시 유지).")]
    [SerializeField] private bool hideWhenFogControllerMissing = false;

    [Tooltip("0이면 매 Update 평가. 양수이면 해당 초마다 평가. 20Hz(=0.05)면 시야 경계 가시성도 시각적으로 거슬리지 않고 부담은 매 프레임 대비 1/3 수준이다.")]
    [SerializeField, Min(0f)] private float updateInterval = 0.05f;

    private bool[] _rendererInitialEnabled;
    private bool _renderersResolved;
    private bool _currentlyVisible = true;
    private bool _hasAppliedOnce;
    private float _accumulatedTime;

    private void Awake()
    {
        EnsureRenderers();
    }

    private void OnEnable()
    {
        EnsureRenderers();
        RefreshVisibilityImmediate();
    }

    private void LateUpdate()
    {
        if (updateInterval > 0f)
        {
            _accumulatedTime += Time.deltaTime;
            if (_accumulatedTime < updateInterval) return;
            _accumulatedTime = 0f;
        }

        EvaluateAndApply();
    }

    /// <summary>
    /// pool 재사용 시 호출. 캐시된 renderer를 prefab 초기 enabled 상태로 복원하고
    /// 다음 평가 타이머를 0으로 리셋한다.
    /// </summary>
    public void ResetToVisible()
    {
        EnsureRenderers();
        _accumulatedTime = 0f;
        ApplyVisibility(true);
    }

    public void HideImmediatelyForPool()
    {
        EnsureRenderers();
        _accumulatedTime = 0f;
        ApplyVisibility(false);
    }

    /// <summary>
    /// 현재 transform.position 기준으로 즉시 visible 여부를 평가해 적용한다.
    /// pool 재사용 직후 또는 Initialize 직후에 호출해 한 프레임 지연 없이 상태를 확정한다.
    /// </summary>
    public void RefreshVisibilityImmediate()
    {
        EnsureRenderers();
        _accumulatedTime = 0f;
        EvaluateAndApply();
    }

    private void EvaluateAndApply()
    {
        bool visible = ResolveVisibility();
        if (!_hasAppliedOnce || visible != _currentlyVisible)
            ApplyVisibility(visible);
    }

    private bool ResolveVisibility()
    {
        FogOfWarController fog = FogOfWarController.Active;
        if (fog == null)
            return !hideWhenFogControllerMissing;
        return fog.IsVisibleWorldPosition(transform.position);
    }

    private void ApplyVisibility(bool visible)
    {
        _currentlyVisible = visible;
        _hasAppliedOnce = true;

        if (managedRenderers == null) return;

        for (int i = 0; i < managedRenderers.Length; i++)
        {
            Renderer r = managedRenderers[i];
            if (r == null) continue;

            bool target = visible && _rendererInitialEnabled[i];
            if (r.enabled != target)
                r.enabled = target;
        }
    }

    private void EnsureRenderers()
    {
        if (_renderersResolved) return;
        _renderersResolved = true;

        if (managedRenderers == null || managedRenderers.Length == 0)
        {
            managedRenderers = includeChildrenOnAutoCache
                ? GetComponentsInChildren<Renderer>(true)
                : GetComponents<Renderer>();
        }

        if (managedRenderers != null && managedRenderers.Length > 0)
        {
            _rendererInitialEnabled = new bool[managedRenderers.Length];
            for (int i = 0; i < managedRenderers.Length; i++)
            {
                Renderer r = managedRenderers[i];
                _rendererInitialEnabled[i] = r != null && r.enabled;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (managedRenderers == null || managedRenderers.Length == 0)
            Debug.LogWarning(
                "[FogVisibilityRenderer] 캐시할 Renderer가 없습니다 — 본인/자식에 Renderer를 두거나 managedRenderers를 명시하세요.",
                this);
#endif
    }
}
