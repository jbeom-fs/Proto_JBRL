using System;
using UnityEngine;

public sealed class PlayerEliteKeyInventory : MonoBehaviour
{
    public bool HasEliteKey { get; private set; }

    public event Action<bool> EliteKeyChanged;

    public void GrantEliteKey()
    {
        if (HasEliteKey)
            return;

        HasEliteKey = true;
        EliteKeyChanged?.Invoke(true);
    }

    public void ResetEliteKey()
        => ClearEliteKey();

    public bool TryConsumeEliteKey()
    {
        if (!HasEliteKey)
            return false;

        ClearEliteKey();
        return true;
    }

    public void ClearEliteKey()
    {
        if (!HasEliteKey)
            return;

        HasEliteKey = false;
        EliteKeyChanged?.Invoke(false);
    }
}
