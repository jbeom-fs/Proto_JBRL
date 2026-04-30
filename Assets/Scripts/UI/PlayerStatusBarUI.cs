using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerStatusBarUI : MonoBehaviour
{
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private CombatEventChannel combatChannel;
    [SerializeField] private Image hpFillImage;
    [SerializeField] private Image mpFillImage;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI mpText;

    private void Awake()
    {
        if (combat == null)
            combat = FindAnyObjectByType<PlayerCombatController>();

        if (combat == null)
            Debug.LogWarning("[PlayerStatusBarUI] PlayerCombatController is missing. Initial HP/MP UI refresh will be skipped.");
        if (combatChannel == null)
            Debug.LogWarning("[PlayerStatusBarUI] CombatEventChannel is missing. HP/MP change events will not be received.");

        bool hasRequiredImages = true;
        if (hpFillImage == null)
        {
            Debug.LogError("[PlayerStatusBarUI] hpFillImage is missing.");
            hasRequiredImages = false;
        }
        if (mpFillImage == null)
        {
            Debug.LogError("[PlayerStatusBarUI] mpFillImage is missing.");
            hasRequiredImages = false;
        }

        if (!hasRequiredImages)
            enabled = false;
    }

    private void OnEnable()
    {
        if (combatChannel == null) return;

        combatChannel.OnPlayerHpChanged += UpdateHp;
        combatChannel.OnPlayerMpChanged += UpdateMp;
    }

    private void OnDisable()
    {
        if (combatChannel == null) return;

        combatChannel.OnPlayerHpChanged -= UpdateHp;
        combatChannel.OnPlayerMpChanged -= UpdateMp;
    }

    private void Start()
    {
        if (combat == null) return;

        UpdateHp(combat.CurrentHp, combat.MaxHp);
        UpdateMp(combat.CurrentMp, combat.MaxMp);
    }

    private void UpdateHp(int current, int max)
    {
        hpFillImage.fillAmount = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);

        if (hpText != null)
            hpText.text = $"{current}/{max}";
    }

    private void UpdateMp(int current, int max)
    {
        mpFillImage.fillAmount = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);

        if (mpText != null)
            mpText.text = $"{current}/{max}";
    }
}
