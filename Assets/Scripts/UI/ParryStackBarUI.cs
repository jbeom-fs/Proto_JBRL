using UnityEngine;
using UnityEngine.UI;

public sealed class ParryStackBarUI : MonoBehaviour
{
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private Slider stackSlider;

    private Graphic[] _graphics;
    private bool _isVisible = true;

    private void Awake()
    {
        if (combat == null)
            combat = PlayerCombatController.Active;

        if (stackSlider == null)
            stackSlider = GetComponent<Slider>();

        _graphics = GetComponentsInChildren<Graphic>(true);

        if (stackSlider != null)
            stackSlider.interactable = false;
    }

    private void Update()
    {
        if (combat == null)
            combat = PlayerCombatController.Active;

        if (combat == null || stackSlider == null)
            return;

        bool visible = combat.CurrentBasicAttackMode == PlayerBasicAttackMode.Parry;
        SetVisible(visible);
        if (!visible)
            return;

        int max = combat.MaxParryStack;
        stackSlider.value = max > 0 ? (float)combat.CurrentParryStack / max : 0f;
    }

    private void SetVisible(bool visible)
    {
        if (_isVisible == visible)
            return;

        _isVisible = visible;
        if (stackSlider != null)
            stackSlider.enabled = visible;

        if (_graphics == null)
            return;

        for (int i = 0; i < _graphics.Length; i++)
        {
            if (_graphics[i] != null)
                _graphics[i].enabled = visible;
        }
    }
}
