using UnityEngine;
using UnityEngine.UI;

public sealed class ParryStackBarUI : MonoBehaviour
{
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private Slider stackSlider;

    private void Awake()
    {
        if (combat == null)
            combat = PlayerCombatController.Active;

        if (stackSlider == null)
            stackSlider = GetComponent<Slider>();

        if (stackSlider != null)
            stackSlider.interactable = false;
    }

    private void Update()
    {
        if (combat == null)
            combat = PlayerCombatController.Active;

        if (combat == null || stackSlider == null)
            return;

        int max = combat.MaxParryStack;
        stackSlider.value = max > 0 ? (float)combat.CurrentParryStack / max : 0f;
    }
}
