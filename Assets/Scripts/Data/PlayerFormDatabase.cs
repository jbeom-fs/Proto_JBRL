using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerFormDatabase", menuName = "JBRogLike/Player/Form Database")]
public sealed class PlayerFormDatabase : ScriptableObject
{
    [SerializeField] private PlayerFormData[] forms = System.Array.Empty<PlayerFormData>();

    private Dictionary<PlayerFormId, PlayerFormData> _formsById;

    public bool TryGet(PlayerFormId id, out PlayerFormData form)
    {
        EnsureCache();
        return _formsById.TryGetValue(id, out form);
    }

    private void OnEnable()
    {
        RebuildCache();
    }

    private void OnValidate()
    {
        RebuildCache();
        ValidateForms();
    }

    private void EnsureCache()
    {
        if (_formsById == null)
            RebuildCache();
    }

    private void RebuildCache()
    {
        if (_formsById == null)
            _formsById = new Dictionary<PlayerFormId, PlayerFormData>();
        else
            _formsById.Clear();

        if (forms == null)
            return;

        for (int i = 0; i < forms.Length; i++)
        {
            PlayerFormData form = forms[i];
            if (form == null)
                continue;

            _formsById[form.FormId] = form;
        }
    }

    private void ValidateForms()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (forms == null)
            return;

        var seen = new HashSet<PlayerFormId>();
        for (int i = 0; i < forms.Length; i++)
        {
            PlayerFormData form = forms[i];
            if (form == null)
            {
                Debug.LogWarning("[PlayerFormDatabase] Empty form at index " + i + ".", this);
                continue;
            }

            if (!seen.Add(form.FormId))
                Debug.LogWarning("[PlayerFormDatabase] Duplicate FormId: " + form.FormId + ".", this);
        }
#endif
    }
}
