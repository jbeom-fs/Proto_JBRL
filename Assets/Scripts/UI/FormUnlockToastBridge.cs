using UnityEngine;

public sealed class FormUnlockToastBridge : MonoBehaviour
{
    [SerializeField] private float displayDuration = 2.5f;

    private void OnEnable()
    {
        FormUnlockEvents.OnFormUnlocked += HandleFormUnlocked;
    }

    private void OnDisable()
    {
        FormUnlockEvents.OnFormUnlocked -= HandleFormUnlocked;
    }

    private void HandleFormUnlocked(PlayerFormId formId)
    {
        if (ToastUI.Instance == null)
            return;

        ToastUI.Instance.Show(
            string.Format(UiMessages.FormUnlockedFormat, UiMessages.GetFormName(formId)),
            displayDuration);
    }
}
