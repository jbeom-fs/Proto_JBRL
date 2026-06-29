using TMPro;
using UnityEngine;

public sealed class ComboCounterUI : MonoBehaviour
{
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private TMP_Text comboText;

    private bool _isVisible = true;
    private int _lastCount = -1;

    private void Awake()
    {
        if (combat == null)
            combat = PlayerCombatController.Active;

        if (comboText == null)
            comboText = GetComponent<TMP_Text>();
    }

    private void Update()
    {
        if (combat == null)
            combat = PlayerCombatController.Active;

        bool visible = combat != null && combat.IsComboBonusActive && combat.CurrentComboStack > 0;
        SetVisible(visible);
        if (!visible)
            return;

        int count = combat.CurrentComboStack;
        if (count == _lastCount)
            return;

        _lastCount = count;
        if (comboText != null)
            comboText.SetText("x{0}", count);
    }

    private void SetVisible(bool visible)
    {
        if (_isVisible == visible)
            return;

        _isVisible = visible;
        if (comboText != null)
            comboText.enabled = visible;
    }
}
