using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ComboCounterUI : MonoBehaviour
{
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text comboText;
    [SerializeField] private GameObject[] orbs;
    [SerializeField] private GameObject[] orbOnOverlays;
    [SerializeField] private Slider windowSlider;
    [SerializeField] private Image sliderFill;
    [SerializeField] private Color[] tierColors;

    private bool _isVisible;
    private int _lastCount = -1;
    private int _lastTier = -1;
    private int _lastMaxTier = -1;
    private float _lastWindow = -1f;
    private int _lastColorIndex = -1;
    private Color _lastColor;
    private bool _hasLastColor;

    private void Awake()
    {
        if (combat == null)
            combat = PlayerCombatController.Active;

        if (comboText == null)
            comboText = GetComponentInChildren<TMP_Text>(true);

        _isVisible = root == null || root.activeSelf;
    }

    private void Update()
    {
        if (combat == null)
            combat = PlayerCombatController.Active;

        bool visible = combat != null && combat.IsComboActive;
        SetVisible(visible);
        if (!visible)
            return;

        UpdateCount(combat.CurrentComboStack);
        UpdateOrbs(combat.CurrentComboTier, combat.MaxComboTier);
        UpdateWindow(combat.ComboWindowRemainingNormalized);
        UpdateColor(combat.CurrentComboTier);
    }

    private void UpdateCount(int count)
    {
        if (count == _lastCount)
            return;

        _lastCount = count;
        if (comboText != null)
            comboText.SetText("x{0}", count);
    }

    private void UpdateOrbs(int tier, int maxTier)
    {
        if (tier == _lastTier && maxTier == _lastMaxTier)
            return;

        _lastTier = tier;
        _lastMaxTier = maxTier;

        int orbCount = orbs != null ? orbs.Length : 0;
        int visibleOrbCount = Mathf.Clamp(maxTier, 0, orbCount);
        int litOrbCount = Mathf.Clamp(tier, 0, visibleOrbCount);

        for (int i = 0; i < orbCount; i++)
        {
            GameObject orb = orbs[i];
            if (orb == null)
                continue;

            bool orbVisible = i < visibleOrbCount;
            if (orb.activeSelf != orbVisible)
                orb.SetActive(orbVisible);

            GameObject onOverlay = orbOnOverlays != null && i < orbOnOverlays.Length
                ? orbOnOverlays[i]
                : null;
            bool lit = i < litOrbCount;
            if (onOverlay != null && onOverlay.activeSelf != lit)
                onOverlay.SetActive(lit);
        }
    }

    private void UpdateWindow(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        if (_lastWindow >= 0f && Mathf.Approximately(normalized, _lastWindow))
            return;

        _lastWindow = normalized;
        if (windowSlider != null)
            windowSlider.SetValueWithoutNotify(normalized);
    }

    private void UpdateColor(int tier)
    {
        if (tierColors == null || tierColors.Length == 0)
            return;

        int colorIndex = Mathf.Clamp(tier, 0, tierColors.Length - 1);
        Color color = tierColors[colorIndex];
        if (_hasLastColor && colorIndex == _lastColorIndex && color == _lastColor)
            return;

        _hasLastColor = true;
        _lastColorIndex = colorIndex;
        _lastColor = color;

        if (comboText != null)
            comboText.color = color;
        if (sliderFill != null)
            sliderFill.color = color;
    }

    private void SetVisible(bool visible)
    {
        if (_isVisible == visible)
            return;

        _isVisible = visible;
        if (root != null && root != gameObject)
        {
            root.SetActive(visible);
        }
        else if (root == gameObject)
        {
            SetSelfRootVisible(visible);
        }

        if (visible)
            InvalidateCaches();
    }

    private void SetSelfRootVisible(bool visible)
    {
        Graphic[] ownGraphics = GetComponents<Graphic>();
        for (int i = 0; i < ownGraphics.Length; i++)
            ownGraphics[i].enabled = visible;

        for (int i = 0; i < transform.childCount; i++)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (child.activeSelf != visible)
                child.SetActive(visible);
        }
    }

    private void InvalidateCaches()
    {
        _lastCount = -1;
        _lastTier = -1;
        _lastMaxTier = -1;
        _lastWindow = -1f;
        _lastColorIndex = -1;
        _hasLastColor = false;
    }
}
