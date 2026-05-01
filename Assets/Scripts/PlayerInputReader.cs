// ═══════════════════════════════════════════════════════════════════
//  PlayerInputReader.cs
//  Application Layer — 플레이어 입력 단일 처리 지점
//
//  책임:
//    • Keyboard.current 를 이 컴포넌트에서만 읽음
//    • 매 프레임 입력 상태를 프로퍼티로 노출
//    • 다른 컴포넌트(PlayerController, PlayerCombatController,
//      SkillRangePreviewer)는 이 컴포넌트를 통해 입력을 소비
//
//  키 매핑:
//    방향키     → MoveInput
//    Z          → WasStairPressed
//    F10        → WasOpenDoorPressed
//    Space      → WasBasicAttackPressed, IsBasicAttackHeld
//    Q/W/E/R    → WasSkillPressed(0~3), IsSkillHeld(0~3)
//
//  실행 순서:
//    DefaultExecutionOrder(-10) 으로 PlayerController(0) 보다
//    먼저 Update 를 실행해 같은 프레임 내 플래그가 항상 최신 상태.
// ═══════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-10)]
public class PlayerInputReader : MonoBehaviour
{
    // ── 이동 ─────────────────────────────────────────────────────────
    /// <summary>방향키 입력 벡터. isPressed 기반 (누르는 동안 유지).</summary>
    public Vector2 MoveInput { get; private set; }

    // ── 단발 액션 플래그 (wasPressedThisFrame 기반, 매 프레임 갱신) ──
    public bool WasStairPressed        { get; private set; }
    public bool WasOpenDoorPressed     { get; private set; }
    public bool WasBasicAttackPressed  { get; private set; }

    // ── 스킬 단발 / 홀드 ─────────────────────────────────────────────
    private readonly bool[] _wasSkillPressed = new bool[4];

    /// <summary>슬롯 index(0~3) 이번 프레임에 눌렸는지.</summary>
    public bool WasSkillPressed(int slot) => (uint)slot < 4u && _wasSkillPressed[slot];

    /// <summary>Space 키를 현재 누르고 있는지. SkillRangePreviewer 기본 공격 범위 미리보기용.</summary>
    public bool IsBasicAttackHeld
    {
        get
        {
            var kb = Keyboard.current;
            return kb != null && kb.spaceKey.isPressed;
        }
    }

    /// <summary>슬롯 index(0~3) 현재 누르고 있는지. SkillRangePreviewer 홀드 감지용.</summary>
    public bool IsSkillHeld(int slot)
    {
        var kb = Keyboard.current;
        if (kb == null) return false;
        return slot switch
        {
            0 => kb.qKey.isPressed,
            1 => kb.wKey.isPressed,
            2 => kb.eKey.isPressed,
            3 => kb.rKey.isPressed,
            _ => false
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  매 프레임 처리 — 다른 컴포넌트의 Update 보다 먼저 실행됨
    // ══════════════════════════════════════════════════════════════

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
        {
            ClearAllFlags();
            return;
        }

        // 이동 (isPressed — 누르는 동안 유지)
        float x = 0f, y = 0f;
        if (kb.upArrowKey.isPressed)    y =  1f;
        if (kb.downArrowKey.isPressed)  y = -1f;
        if (kb.leftArrowKey.isPressed)  x = -1f;
        if (kb.rightArrowKey.isPressed) x =  1f;
        MoveInput = new Vector2(x, y);

        // 단발 액션 (wasPressedThisFrame)
        WasStairPressed       = kb.zKey.wasPressedThisFrame;
        WasOpenDoorPressed    = kb.f10Key.wasPressedThisFrame;
        WasBasicAttackPressed = kb.spaceKey.wasPressedThisFrame;

        // 스킬 Q/W/E/R
        _wasSkillPressed[0] = kb.qKey.wasPressedThisFrame;
        _wasSkillPressed[1] = kb.wKey.wasPressedThisFrame;
        _wasSkillPressed[2] = kb.eKey.wasPressedThisFrame;
        _wasSkillPressed[3] = kb.rKey.wasPressedThisFrame;
    }

    private void ClearAllFlags()
    {
        MoveInput             = Vector2.zero;
        WasStairPressed       = false;
        WasOpenDoorPressed    = false;
        WasBasicAttackPressed = false;
        _wasSkillPressed[0] = _wasSkillPressed[1] = _wasSkillPressed[2] = _wasSkillPressed[3] = false;
    }
}
