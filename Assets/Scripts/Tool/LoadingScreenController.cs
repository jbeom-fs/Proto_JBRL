// ═══════════════════════════════════════════════════════════════════
//  LoadingScreenController.cs
//  Presentation Layer — 층 이동 시 로딩 이미지 표시
//
//  씬 설정:
//    1. Canvas (Screen Space - Overlay) 생성
//    2. Image 오브젝트 추가 (전체 화면 덮는 크기)
//    3. 이 스크립트를 Canvas에 부착
//    4. Inspector에서 loadingImage 슬롯에 Image 연결
//    5. DungeonManager의 Loading Screen 슬롯에 이 오브젝트 연결
// ═══════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class LoadingScreenController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("화면을 덮는 로딩 이미지")]
    public Image loadingImage;

    [Header("페이드 설정")]
    [Tooltip("페이드 인/아웃 시간 (초)")]
    public float fadeDuration = 0.25f;

    // ── 초기화 ─────────────────────────────────────────────────────

    private void Awake()
    {
        // 시작 시 투명하게 숨김
        if (loadingImage != null)
            loadingImage.color = new Color(loadingImage.color.r,
                                           loadingImage.color.g,
                                           loadingImage.color.b, 0f);
        loadingImage.gameObject.SetActive(false);
    }

    // ── 공개 API ─────────────────────────────────────────────────────

    /// <summary>로딩 이미지를 페이드 인합니다.</summary>
    public IEnumerator Show()
    {
        loadingImage.gameObject.SetActive(true);
        yield return StartCoroutine(Fade(0f, 1f));
    }

    /// <summary>로딩 이미지를 페이드 아웃 후 숨깁니다.</summary>
    public IEnumerator Hide()
    {
        yield return StartCoroutine(Fade(1f, 0f));
        loadingImage.gameObject.SetActive(false);
    }

    // ── 내부 ─────────────────────────────────────────────────────────

    private IEnumerator Fade(float from, float to)
    {
        if (loadingImage == null) yield break;

        Color c       = loadingImage.color;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed         += Time.unscaledDeltaTime;
            c.a              = Mathf.Lerp(from, to, elapsed / fadeDuration);    
            loadingImage.color = c;
            yield return YieldCache.WaitForEndOfFrame;
        }

        c.a                = to;
        loadingImage.color = c;
    }
}