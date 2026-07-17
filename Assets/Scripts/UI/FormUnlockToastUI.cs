using TMPro;
using UnityEngine;

public sealed class FormUnlockToastUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private float displayDuration = 2.5f;

    private float _remainingTime;

    private void OnEnable()
    {
        FormUnlockEvents.OnFormUnlocked += HandleFormUnlocked;
    }

    private void OnDisable()
    {
        FormUnlockEvents.OnFormUnlocked -= HandleFormUnlocked;
        _remainingTime = 0f;

        if (root != null)
            root.SetActive(false);
    }

    private void Update()
    {
        if (_remainingTime <= 0f)
            return;

        _remainingTime -= Time.unscaledDeltaTime;
        if (_remainingTime <= 0f && root != null)
            root.SetActive(false);
    }

    private void HandleFormUnlocked(PlayerFormId formId)
    {
        if (messageText != null)
            messageText.text = string.Format(
                UiMessages.FormUnlockedFormat,
                UiMessages.GetFormName(formId));

        if (root != null)
            root.SetActive(true);

        _remainingTime = Mathf.Max(0f, displayDuration);
        if (_remainingTime <= 0f && root != null)
            root.SetActive(false);
    }
}
