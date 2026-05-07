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

    // ── 런타임 상태 ─────────────────────────────────────────────────

    private int                    _slotIndex;
    private PlayerCombatController _combat;
    private CombatEventChannel     _channel;
    private Sprite                 _emptySlotSprite;  // 빈 슬롯 대체 이미지 (SkillUIManager에서 주입)

    private SkillData _skill;          // 현재 슬롯에 할당된 스킬 캐시
    private float     _cooldownMax;    // 스킬 사용 시점의 최대 쿨타임
    private bool      _trackingCooldown;

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

    // ══════════════════════════════════════════════════════════════
    //  아이콘 갱신 — 무기·스킬 교체 시 SkillUIManager가 호출
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 현재 장착 무기의 슬롯 데이터를 다시 읽어 아이콘을 갱신합니다.
    /// 스킬이 없으면 emptySlotSprite를 표시하고 쿨타임 추적을 중단합니다.
    /// </summary>
    public void RefreshIcon()
    {
        _skill = null;

        if (_combat != null)
            _skill = _combat.GetSkillData(_slotIndex);

        // 스킬 아이콘 or 빈 슬롯 스프라이트 — 둘 다 없으면 Image 비활성화
        Sprite display = (_skill != null && _skill.icon != null) ? _skill.icon : _emptySlotSprite;
        iconImage.sprite  = display;
        iconImage.enabled = display != null;

        // 스킬 없음 → 쿨타임 UI 초기화
        if (_skill == null)
        {
            _trackingCooldown = false;
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
    }

    // ══════════════════════════════════════════════════════════════
    //  매 프레임 쿨타임 갱신
    // ══════════════════════════════════════════════════════════════

    private void Update()
    {
        if (!_trackingCooldown) return;

        float remaining = _combat.GetSkillCooldownRemaining(_slotIndex);

        if (remaining <= 0f)
        {
            _trackingCooldown = false;
            SetCooldownVisible(false);
            return;
        }

        // fillAmount: 1(사용 직후) → 0(완료)
        cooldownOverlay.fillAmount = _cooldownMax > 0f ? remaining / _cooldownMax : 0f;
        cooldownText.text          = $"{remaining:F1}s";
        SetCooldownVisible(true);
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────

    private void SetCooldownVisible(bool visible)
    {
        cooldownOverlay.gameObject.SetActive(visible);
        cooldownText.gameObject.SetActive(visible);
    }
}
