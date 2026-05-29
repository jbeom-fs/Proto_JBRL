using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerStatusBarUI : MonoBehaviour
{
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private CombatEventChannel combatChannel;
    [SerializeField] private Slider hpSlider;
    [SerializeField] private TextMeshProUGUI hpText;

    private void Awake()
    {
        if (combat == null)
            combat = PlayerCombatController.Active;

        if (combat == null)
            Debug.LogWarning("[PlayerStatusBarUI] PlayerCombatController is missing. Initial HP UI refresh will be skipped.");
        if (combatChannel == null)
            Debug.LogWarning("[PlayerStatusBarUI] CombatEventChannel is missing. HP change events will not be received.");

        bool hasRequiredSliders = true;
        if (hpSlider == null)
        {
            Debug.LogError("[PlayerStatusBarUI] hpSlider is missing.");
            hasRequiredSliders = false;
        }

        if (!hasRequiredSliders)
            enabled = false;
    }

    private void OnEnable()
    {
        if (combatChannel != null)
        {
            combatChannel.OnPlayerHpChanged += UpdateHp;
        }
    }

    private void OnDisable()
    {
        if (combatChannel != null)
        {
            combatChannel.OnPlayerHpChanged -= UpdateHp;
        }
    }

    private void Start()
    {
        if (combat == null) return;

        UpdateHp(combat.CurrentHp, combat.MaxHp);
    }

    private void UpdateHp(int current, int max)
    {
        hpSlider.value = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);

        if (hpText != null)
            hpText.text = $"{current}/{max}";
    }

}
