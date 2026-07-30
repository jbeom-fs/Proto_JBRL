using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerPassiveUnlocks : MonoBehaviour
{
    private static readonly PlayerFormId[] s_FormIds =
        (PlayerFormId[])Enum.GetValues(typeof(PlayerFormId));

    [SerializeField] private PlayerFormDatabase formDatabase;

    private readonly HashSet<string> _unlockedIds =
        new HashSet<string>(StringComparer.Ordinal);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private readonly HashSet<string> _duplicateIdWarnings =
        new HashSet<string>(StringComparer.Ordinal);
#endif

    public static PlayerPassiveUnlocks Active { get; private set; }

    public event Action OnChanged;

    public bool IsUnlocked(PassiveEngravingData passive)
    {
        if (passive == null)
            return false;

        if (IsDefaultPassive(passive))
            return true;

        return !string.IsNullOrWhiteSpace(passive.unlockId) &&
               _unlockedIds.Contains(passive.unlockId);
    }

    public bool Unlock(PassiveEngravingData passive)
    {
        if (!IsCatalogPassive(passive) ||
            IsDefaultPassive(passive) ||
            string.IsNullOrWhiteSpace(passive.unlockId) ||
            !_unlockedIds.Add(passive.unlockId))
        {
            return false;
        }

        OnChanged?.Invoke();
        return true;
    }

    public bool Lock(PassiveEngravingData passive)
    {
        if (!IsCatalogPassive(passive) ||
            IsDefaultPassive(passive) ||
            string.IsNullOrWhiteSpace(passive.unlockId) ||
            !_unlockedIds.Remove(passive.unlockId))
        {
            return false;
        }

        OnChanged?.Invoke();
        return true;
    }

    public void GetUnlockedIds(List<string> output)
    {
        if (output == null)
            return;

        foreach (string unlockId in _unlockedIds)
            output.Add(unlockId);
    }

    public void GetCatalog(PlayerFormId form, List<PassiveEngravingData> output)
    {
        if (output == null || !TryGetCatalog(form, out List<PassiveEngravingData> catalog))
            return;

        for (int i = 0; i < catalog.Count; i++)
        {
            PassiveEngravingData passive = catalog[i];
            if (passive != null)
                output.Add(passive);
        }
    }

    public bool TryGetPassive(string unlockId, out PassiveEngravingData passive)
    {
        passive = null;
        if (formDatabase == null || string.IsNullOrWhiteSpace(unlockId))
            return false;

        for (int formIndex = 0; formIndex < s_FormIds.Length; formIndex++)
        {
            if (!TryGetCatalog(s_FormIds[formIndex], out List<PassiveEngravingData> catalog))
                continue;

            for (int passiveIndex = 0; passiveIndex < catalog.Count; passiveIndex++)
            {
                PassiveEngravingData candidate = catalog[passiveIndex];
                if (candidate == null ||
                    !string.Equals(candidate.unlockId, unlockId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (passive != null && passive != candidate)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (_duplicateIdWarnings.Add(unlockId))
                    {
                        Debug.LogWarning(
                            "[PlayerPassiveUnlocks] Duplicate passive unlockId in form catalogs: " +
                            unlockId + ".",
                            this);
                    }
#endif
                    passive = null;
                    return false;
                }

                passive = candidate;
            }
        }

        return passive != null;
    }

    public void Clear()
    {
        if (_unlockedIds.Count == 0)
            return;

        _unlockedIds.Clear();
        OnChanged?.Invoke();
    }

    private void OnEnable()
    {
        Active = this;
    }

    private void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    private bool IsCatalogPassive(PassiveEngravingData passive)
    {
        if (passive == null || formDatabase == null)
            return false;

        for (int formIndex = 0; formIndex < s_FormIds.Length; formIndex++)
        {
            if (!TryGetCatalog(s_FormIds[formIndex], out List<PassiveEngravingData> catalog))
                continue;

            for (int passiveIndex = 0; passiveIndex < catalog.Count; passiveIndex++)
            {
                if (catalog[passiveIndex] == passive)
                    return true;
            }
        }

        return false;
    }

    private bool IsDefaultPassive(PassiveEngravingData passive)
    {
        if (passive == null ||
            !TryGetCatalog(passive.owningForm, out List<PassiveEngravingData> catalog) ||
            catalog.Count == 0)
        {
            return false;
        }

        return catalog[0] == passive;
    }

    private bool TryGetCatalog(PlayerFormId form, out List<PassiveEngravingData> catalog)
    {
        catalog = null;
        if (formDatabase == null ||
            !formDatabase.TryGet(form, out PlayerFormData formData) ||
            formData == null ||
            formData.DefaultWeapon == null)
        {
            return false;
        }

        catalog = formData.DefaultWeapon.passiveEngravings;
        return catalog != null;
    }
}
