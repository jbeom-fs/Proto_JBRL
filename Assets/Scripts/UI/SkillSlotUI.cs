// ═══════════════════════════════════════════════════════════════════
//  SkillSlotUI.cs
//  책임: 스킬 슬롯 하나의 렌더링 (아이콘 · 쿨타임 덮개 · 남은 시간 텍스트)
//
//  계층 구조 예시:
//    Slot_Q  ← 이 컴포넌트를 부착
//    ├── Icon             (Image)
//    ├── CooldownOverlay  (Image — Filled / Radial 360 / Fill Origin: Top)
//    └── CooldownText     (TextMeshProUGUI — 중앙 정렬)
//
//  사용법:
//    SkillUIManager.Awake()에서 Initialize()를 호출합니다.
//    무기/스킬이 교체되면 RefreshIcon()을 호출합니다.
// ═══════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkillSlotUI : MonoBehaviour
{
    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("UI 컴포넌트 참조")]
    [SerializeField] private Image           iconImage;
    [SerializeField] private Image           cooldownOverlay; // Image Type: Filled / Radial 360
    [SerializeField] private TextMeshProUGUI cooldownText;

    [Header("Recast Chain")]
    [SerializeField] private Color recastWindowFillColor = new Color(1f, 0.55f, 0.1f, 0.9f);

    // ── 런타임 상태 ─────────────────────────────────────────────────

    private int                    _slotIndex;
    private PlayerCombatController _combat;
    private CombatEventChannel     _channel;
    private Sprite                 _emptySlotSprite;  // 빈 슬롯 대체 이미지 (SkillUIManager에서 주입)

    private SkillData _skill;          // 현재 슬롯에 할당된 스킬 캐시
    private float     _cooldownMax;    // 스킬 사용 시점의 최대 쿨타임
    private bool      _trackingCooldown;
    private int       _lastDisplayedTenths = int.MinValue;
    private bool?     _cooldownUIVisible;
    private Sprite    _rootIconSprite;
    private bool      _rootIconEnabled;
    private Color     _normalFillColor;
    private Image.Type _normalFillType;
    private Image.FillMethod _normalFillMethod;
    private int       _normalFillOrigin;
    private bool      _normalFillClockwise;
    private bool      _isInitialized;
    private bool      _showingRecast;
    private Sprite    _lastAppliedIcon;
    private bool      _lastAppliedIconEnabled;
    private bool      _hasLastAppliedIcon;
    private Color     _lastAppliedFillColor;
    private bool      _hasLastAppliedFillColor;
    private float     _lastAppliedFillAmount = -1f;

    // ══════════════════════════════════════════════════════════════
    //  초기화 (SkillUIManager가 호출)
    // ══════════════════════════════════════════════════════════════

    /// <summary>슬롯 인덱스·전투 컨트롤러·이벤트 채널·빈 슬롯 스프라이트를 주입합니다.</summary>
    public void Initialize(int slotIndex, PlayerCombatController combat,
                           CombatEventChannel channel, Sprite emptySlotSprite)
    {
        _slotIndex       = slotIndex;
        _combat          = combat;
        _channel         = channel;
        _emptySlotSprite = emptySlotSprite;

        if (_combat == null)
            Debug.LogWarning($"[SkillSlotUI] 슬롯 {_slotIndex}: PlayerCombatController가 연결되지 않아 스킬 정보를 표시할 수 없습니다.");
        if (_channel == null)
            Debug.LogWarning($"[SkillSlotUI] 슬롯 {_slotIndex}: CombatEventChannel이 연결되지 않아 쿨다운 이벤트를 받을 수 없습니다.");

        bool hasRequiredUi = true;
        if (iconImage == null)
        {
            Debug.LogError($"[SkillSlotUI] 슬롯 {_slotIndex}: iconImage가 연결되지 않았습니다.");
            hasRequiredUi = false;
        }
        if (cooldownOverlay == null)
        {
            Debug.LogError($"[SkillSlotUI] 슬롯 {_slotIndex}: cooldownOverlay가 연결되지 않았습니다.");
            hasRequiredUi = false;
        }
        if (cooldownText == null)
        {
            Debug.LogError($"[SkillSlotUI] 슬롯 {_slotIndex}: cooldownText가 연결되지 않았습니다.");
            hasRequiredUi = false;
        }
        if (!hasRequiredUi)
        {
            enabled = false;
            return;
        }

        _normalFillColor = cooldownOverlay.color;
        _normalFillType = cooldownOverlay.type;
        _normalFillMethod = cooldownOverlay.fillMethod;
        _normalFillOrigin = cooldownOverlay.fillOrigin;
        _normalFillClockwise = cooldownOverlay.fillClockwise;
        _isInitialized = true;
        InvalidateVisualCaches();

        if (_channel != null)
            _channel.OnSkillUsed += HandleSkillUsed;

        RefreshIcon();
        SetCooldownVisible(false);
    }

    private void OnDestroy()
    {
        if (_channel != null)
            _channel.OnSkillUsed -= HandleSkillUsed;
    }

    private void OnEnable()
    {
        InvalidateVisualCaches();
        if (!_isInitialized)
            return;

        _showingRecast = false;
        RestoreNormalOverlayConfiguration();
        ApplyIcon(_rootIconSprite, _rootIconEnabled);
        ApplyFillColor(_normalFillColor);
    }

    private void OnDisable()
    {
        if (!_isInitialized)
            return;

        ExitRecastVisuals();
        SetCooldownVisible(false);
        InvalidateVisualCaches();
    }

    // ══════════════════════════════════════════════════════════════
    //  아이콘 갱신 — 무기·스킬 교체 시 SkillUIManager가 호출
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 현재 장착 무기의 슬롯 데이터를 다시 읽어 아이콘을 갱신합니다.
    /// 스킬이 없으면 emptySlotSprite를 표시하고 쿨타임 추적을 중단합니다.
    /// </summary>
    public void RefreshIcon()
    {
        SkillData previousSkill = _skill;
        _skill = null;

        if (_combat != null)
            _skill = _combat.GetSkillData(_slotIndex);

        if (!ReferenceEquals(previousSkill, _skill))
        {
            _trackingCooldown = false;
            _cooldownMax = 0f;
            _lastDisplayedTenths = int.MinValue;

            if (_showingRecast)
                ExitRecastVisuals();

            SetCooldownVisible(false);
        }

        // 스킬 아이콘 or 빈 슬롯 스프라이트 — 둘 다 없으면 Image 비활성화
        _rootIconSprite = (_skill != null && _skill.icon != null) ? _skill.icon : _emptySlotSprite;
        _rootIconEnabled = _rootIconSprite != null;
        if (!_showingRecast)
            ApplyIcon(_rootIconSprite, _rootIconEnabled);

        // 스킬 없음 → 쿨타임 UI 초기화
        if (_skill == null)
        {
            _trackingCooldown = false;
            _lastDisplayedTenths = int.MinValue;
            SetCooldownVisible(false);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  이벤트 핸들러 — 이 슬롯의 스킬이 사용됐을 때만 반응
    // ══════════════════════════════════════════════════════════════

    private void HandleSkillUsed(SkillData usedSkill)
    {
        if (usedSkill == null || usedSkill != _skill) return;

        _cooldownMax      = _skill.cooldown;
        _trackingCooldown = true;
        _lastDisplayedTenths = int.MinValue;
    }

    // ══════════════════════════════════════════════════════════════
    //  매 프레임 쿨타임 갱신
    // ══════════════════════════════════════════════════════════════

    private void Update()
    {
        if (_combat == null)
            return;

        SkillData currentRootSkill = _combat.GetSkillData(_slotIndex);
        if (!ReferenceEquals(currentRootSkill, _skill))
            RefreshIcon();

        if (_combat.TryGetRecastRecoveryState(
                _slotIndex,
                out float recoveryRemaining,
                out float recoveryTotal,
                out SkillData _))
        {
            UpdateRecastRecoveryVisuals(recoveryRemaining, recoveryTotal);
            return;
        }

        if (_combat.TryGetRecastState(
                _slotIndex,
                out float recastRemaining,
                out float recastTotal,
                out SkillData nextStage))
        {
            UpdateRecastVisuals(recastRemaining, recastTotal, nextStage);
            return;
        }

        ExitRecastVisuals();
        UpdateCooldownVisuals();
    }

    private void UpdateCooldownVisuals()
    {
        float remaining = _combat.GetSkillCooldownRemaining(_slotIndex);

        if (!_trackingCooldown && remaining > 0f)
        {
            _trackingCooldown = true;
            _cooldownMax = _combat.GetSkillCooldownMax(_slotIndex);
            _lastDisplayedTenths = int.MinValue;
        }

        if (remaining <= 0f)
        {
            _trackingCooldown = false;
            _lastDisplayedTenths = int.MinValue;
            SetCooldownVisible(false);
            return;
        }

        // fillAmount: 1(사용 직후) → 0(완료)
        ApplyFillAmount(_cooldownMax > 0f ? remaining / _cooldownMax : 0f);
        int tenths = Mathf.RoundToInt(remaining * 10f);
        if (tenths != _lastDisplayedTenths)
        {
            _lastDisplayedTenths = tenths;
            cooldownText.text = $"{tenths * 0.1f:F1}s";
        }
        SetCooldownVisible(true);
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────

    private void UpdateRecastRecoveryVisuals(float remaining, float total)
    {
        if (_showingRecast)
            ExitRecastVisuals();

        ApplyIcon(_rootIconSprite, _rootIconEnabled);
        ApplyFillColor(_normalFillColor);
        ApplyFillAmount(total > 0f ? remaining / total : 0f);
        UpdateRemainingText(remaining);
        SetCooldownVisible(true);
    }

    private void UpdateRecastVisuals(float remaining, float total, SkillData nextStage)
    {
        if (!_showingRecast)
        {
            _showingRecast = true;
            _lastDisplayedTenths = int.MinValue;
            _lastAppliedFillAmount = -1f;
            ConfigureRecastOverlay();
        }

        Sprite stageIcon = nextStage != null && nextStage.icon != null
            ? nextStage.icon
            : _rootIconSprite;
        ApplyIcon(stageIcon, stageIcon != null);
        ApplyFillColor(recastWindowFillColor);
        ApplyFillAmount(total > 0f ? remaining / total : 0f);
        UpdateRemainingText(remaining);
        SetCooldownVisible(true);
    }

    private void ExitRecastVisuals()
    {
        if (!_showingRecast)
            return;

        _showingRecast = false;
        RestoreNormalOverlayConfiguration();
        ApplyIcon(_rootIconSprite, _rootIconEnabled);
        ApplyFillColor(_normalFillColor);
        _lastDisplayedTenths = int.MinValue;
        _lastAppliedFillAmount = -1f;
    }

    private void ConfigureRecastOverlay()
    {
        if (cooldownOverlay.type != Image.Type.Filled)
            cooldownOverlay.type = Image.Type.Filled;
        if (cooldownOverlay.fillMethod != Image.FillMethod.Radial360)
            cooldownOverlay.fillMethod = Image.FillMethod.Radial360;

        int topOrigin = (int)Image.Origin360.Top;
        if (cooldownOverlay.fillOrigin != topOrigin)
            cooldownOverlay.fillOrigin = topOrigin;
        if (!cooldownOverlay.fillClockwise)
            cooldownOverlay.fillClockwise = true;
    }

    private void RestoreNormalOverlayConfiguration()
    {
        if (cooldownOverlay.type != _normalFillType)
            cooldownOverlay.type = _normalFillType;
        if (cooldownOverlay.fillMethod != _normalFillMethod)
            cooldownOverlay.fillMethod = _normalFillMethod;
        if (cooldownOverlay.fillOrigin != _normalFillOrigin)
            cooldownOverlay.fillOrigin = _normalFillOrigin;
        if (cooldownOverlay.fillClockwise != _normalFillClockwise)
            cooldownOverlay.fillClockwise = _normalFillClockwise;
    }

    private void UpdateRemainingText(float remaining)
    {
        int tenths = Mathf.RoundToInt(Mathf.Max(0f, remaining) * 10f);
        if (tenths == _lastDisplayedTenths)
            return;

        _lastDisplayedTenths = tenths;
        cooldownText.text = $"{tenths * 0.1f:F1}s";
    }

    private void ApplyIcon(Sprite sprite, bool enabledState)
    {
        if (_hasLastAppliedIcon &&
            _lastAppliedIcon == sprite &&
            _lastAppliedIconEnabled == enabledState)
        {
            return;
        }

        _hasLastAppliedIcon = true;
        _lastAppliedIcon = sprite;
        _lastAppliedIconEnabled = enabledState;
        iconImage.sprite = sprite;
        iconImage.enabled = enabledState;
    }

    private void ApplyFillColor(Color color)
    {
        if (_hasLastAppliedFillColor && _lastAppliedFillColor == color)
            return;

        _hasLastAppliedFillColor = true;
        _lastAppliedFillColor = color;
        cooldownOverlay.color = color;
    }

    private void ApplyFillAmount(float fillAmount)
    {
        float clamped = Mathf.Clamp01(fillAmount);
        if (_lastAppliedFillAmount >= 0f && Mathf.Approximately(_lastAppliedFillAmount, clamped))
            return;

        _lastAppliedFillAmount = clamped;
        cooldownOverlay.fillAmount = clamped;
    }

    private void InvalidateVisualCaches()
    {
        _cooldownUIVisible = null;
        _lastDisplayedTenths = int.MinValue;
        _hasLastAppliedIcon = false;
        _lastAppliedIcon = null;
        _lastAppliedIconEnabled = false;
        _hasLastAppliedFillColor = false;
        _lastAppliedFillAmount = -1f;
    }

    private void SetCooldownVisible(bool visible)
    {
        if (_cooldownUIVisible.HasValue && _cooldownUIVisible.Value == visible)
            return;

        cooldownOverlay.gameObject.SetActive(visible);
        cooldownText.gameObject.SetActive(visible);
        _cooldownUIVisible = visible;
    }
}
