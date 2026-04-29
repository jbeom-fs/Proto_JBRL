// ═══════════════════════════════════════════════════════════════════
//  SkillUIManager.cs
//  책임: 스킬 슬롯 4개 초기화 · 던전 입장/층 변경 시 자동 갱신
//
//  자동 갱신 타이밍:
//    1. Start()        — 씬 시작 시 (Inspector에 미리 장착된 무기 반영)
//    2. OnFloorChanged — 층 이동 후 (무기·스킬 교체 대비)
//
//  수동 갱신:
//    무기를 런타임에 교체하는 코드가 생기면
//    EquipWeapon() 이후 skillUIManager.RefreshAllSlots() 를 호출하세요.
//
//  빈 슬롯 정책:
//    emptySlotSprite 를 Manager 1곳에서 관리합니다.
//    추후 UI 테마가 늘어날 경우 UIConfig ScriptableObject 로 분리하세요.
// ═══════════════════════════════════════════════════════════════════

using UnityEngine;

public class SkillUIManager : MonoBehaviour
{
    // ── Inspector 필드 ───────────────────────────────────────────────

    [Header("Dependencies")]
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private CombatEventChannel     combatChannel;
    [SerializeField] private DungeonEventChannel    dungeonChannel;

    [Header("빈 슬롯 이미지 (스킬이 없는 슬롯에 표시)")]
    [SerializeField] private Sprite emptySlotSprite;

    [Header("슬롯 UI (0=Q, 1=W, 2=E, 3=R 순서로 연결)")]
    [SerializeField] private SkillSlotUI[] slots = new SkillSlotUI[4];

    // ══════════════════════════════════════════════════════════════
    //  초기화
    // ══════════════════════════════════════════════════════════════

    private void Awake()
    {
        for (int i = 0; i < slots.Length; i++)
            slots[i]?.Initialize(i, combat, combatChannel, emptySlotSprite);
    }

    private void Start()
    {
        // 씬 시작 시 무기가 Inspector에 미리 세팅돼 있으므로 즉시 갱신
        RefreshAllSlots();
    }

    // ══════════════════════════════════════════════════════════════
    //  던전 이벤트 구독 — 층 변경 시 슬롯 갱신
    // ══════════════════════════════════════════════════════════════

    private void OnEnable()
    {
        if (dungeonChannel != null)
            dungeonChannel.OnFloorChanged += HandleFloorChanged;
    }

    private void OnDisable()
    {
        if (dungeonChannel != null)
            dungeonChannel.OnFloorChanged -= HandleFloorChanged;
    }

    private void HandleFloorChanged(int prevFloor, int newFloor) => RefreshAllSlots();

    // ══════════════════════════════════════════════════════════════
    //  공개 갱신 진입점
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 모든 슬롯의 아이콘을 현재 무기 데이터 기준으로 갱신합니다.
    /// 무기를 런타임에 교체할 때 호출하세요.
    /// </summary>
    public void RefreshAllSlots()
    {
        foreach (var slot in slots)
            slot?.RefreshIcon();
    }
}
