using System;

public static class FormUnlockEvents
{
    public static event Action<PlayerFormId> OnFormUnlocked;

    public static void RaiseFormUnlocked(PlayerFormId formId)
    {
        OnFormUnlocked?.Invoke(formId);
    }
}
