// ═══════════════════════════════════════════════════════════════════
//  EnemyHealthBar.cs
//  책임: 적 머리 위 체력바 렌더링
//
//  사용법:
//    EnemyController와 같은 GameObject에 이 컴포넌트를 추가합니다.
//    자식 오브젝트(BG/Fill)를 Awake에서 자동 생성하므로 프리팹 설정 불필요.
//    EnemyController가 SetHp()를 호출하면 자동으로 갱신됩니다.
//
//  알지 말아야 할 것:
//    • EnemyController 내부 로직
//    • 던전 / 전투 시스템
// ═══════════════════════════════════════════════════════════════════

using UnityEngine;

public class EnemyHealthBar : MonoBehaviour
{
    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("크기 및 위치")]
    [SerializeField] private float barWidth  = 0.8f;
    [SerializeField] private float barHeight = 0.08f;
    [SerializeField] private float yOffset   = 0.55f;

    [Header("색상")]
    [SerializeField] private bool  colorGradient = true;  // HP 비율에 따라 녹→적 전환
    [SerializeField] private Color fillColorFull = new Color(0.20f, 0.85f, 0.20f);
    [SerializeField] private Color fillColorEmpty = Color.red;
    [SerializeField] private Color bgColor       = new Color(0.12f, 0.12f, 0.12f, 0.85f);

    [Header("렌더링 순서")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int    sortingOrder     = 10;

    [Header("표시 조건")]
    [Tooltip("체력이 가득 찼을 때 숨김")]
    [SerializeField] private bool  hideWhenFull   = true;
    [Tooltip("마지막 피해 후 N초 뒤 자동 숨김. 0 = 항상 표시.")]
    [SerializeField] private float autoHideDelay  = 2.5f;

    // ── 런타임 참조 ─────────────────────────────────────────────────

    private SpriteRenderer _fillSr;
    private Transform      _fillTf;
    private Transform      _bgTf;
    private float          _hideTimer;
    private bool           _isVisible;

    // ── 정적 픽셀 스프라이트 (적 N마리가 공유, 텍스처 1회만 생성) ────

    private static Sprite s_Pixel;

    // ══════════════════════════════════════════════════════════════
    //  초기화
    // ══════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (s_Pixel == null) s_Pixel = BuildPixelSprite();
        CreateBarObjects();
        SetVisible(!hideWhenFull);
    }

    private void CreateBarObjects()
    {
        _bgTf  = CreateBarChild("HPBar_BG",   bgColor,            sortingOrder,     out _);
        _fillTf = CreateBarChild("HPBar_Fill", fillColorFull, sortingOrder + 1, out _fillSr);

        _bgTf.localScale    = new Vector3(barWidth,  barHeight, 1f);
        _bgTf.localPosition = new Vector3(0f, yOffset, 0f);

        // Fill 초기 상태 = 가득 참
        _fillTf.localScale    = new Vector3(barWidth, barHeight, 1f);
        _fillTf.localPosition = new Vector3(0f, yOffset, 0f);
    }

    private Transform CreateBarChild(string childName, Color color, int order, out SpriteRenderer sr)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        sr               = go.AddComponent<SpriteRenderer>();
        sr.sprite        = s_Pixel;
        sr.color         = color;
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder  = order;
        return go.transform;
    }

    private static Sprite BuildPixelSprite()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
    }

    // ══════════════════════════════════════════════════════════════
    //  갱신 (EnemyController에서 호출)
    // ══════════════════════════════════════════════════════════════

    /// <summary>HP 값을 받아 바 크기·색상을 즉시 갱신합니다.</summary>
    public void SetHp(int current, int max)
    {
        float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;

        // Fill 스케일 & 위치 — 왼쪽 앵커, 오른쪽에서 줄어듦
        float fillW = barWidth * ratio;
        _fillTf.localScale    = new Vector3(fillW, barHeight, 1f);
        _fillTf.localPosition = new Vector3(barWidth * (ratio - 1f) * 0.5f, yOffset, 0f);

        // 색상 그라디언트
        if (colorGradient)
            _fillSr.color = Color.Lerp(fillColorEmpty, fillColorFull, ratio);

        if (hideWhenFull && ratio >= 1f)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        if (autoHideDelay > 0f)
            _hideTimer = autoHideDelay;
    }

    // ══════════════════════════════════════════════════════════════
    //  자동 숨김 타이머
    // ══════════════════════════════════════════════════════════════

    private void Update()
    {
        if (!_isVisible || autoHideDelay <= 0f) return;
        _hideTimer -= Time.deltaTime;
        if (_hideTimer <= 0f) SetVisible(false);
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────

    private void SetVisible(bool v)
    {
        _isVisible = v;
        _bgTf?.gameObject.SetActive(v);
        _fillTf?.gameObject.SetActive(v);
    }
}
