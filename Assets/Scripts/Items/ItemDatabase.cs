using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ItemDatabase", menuName = "JBRogLike/Items/Item Database")]
public sealed class ItemDatabase : ScriptableObject
{
    [SerializeField] private List<ItemData> items = new List<ItemData>();

    private readonly Dictionary<string, ItemData> _itemsByCode = new Dictionary<string, ItemData>();
    private readonly Dictionary<PlayerFormId, ItemData> _soulsByForm = new Dictionary<PlayerFormId, ItemData>();
    private bool _cacheBuilt;

    public bool TryGetItem(string itemCode, out ItemData item)
    {
        EnsureCache();
        return _itemsByCode.TryGetValue(itemCode, out item);
    }

    public bool TryGetSoulByForm(PlayerFormId formId, out ItemData soul)
    {
        EnsureCache();
        return _soulsByForm.TryGetValue(formId, out soul);
    }

    public void GetItemCodes(List<string> output)
    {
        if (output == null)
            return;

        EnsureCache();
        foreach (string itemCode in _itemsByCode.Keys)
            output.Add(itemCode);
    }

    private void OnEnable()
    {
        RebuildCache();
    }

    private void OnValidate()
    {
        RebuildCache();
        ValidateItems();
    }

    private void EnsureCache()
    {
        if (!_cacheBuilt)
            RebuildCache();
    }

    private void RebuildCache()
    {
        _itemsByCode.Clear();
        _soulsByForm.Clear();
        _cacheBuilt = true;

        if (items == null) return;

        for (int i = 0; i < items.Count; i++)
        {
            ItemData item = items[i];
            if (item == null || string.IsNullOrWhiteSpace(item.ItemCode))
                continue;
            if (_itemsByCode.ContainsKey(item.ItemCode))
                continue;

            _itemsByCode.Add(item.ItemCode, item);

            if (item.ItemType == ItemType.Soul && !_soulsByForm.ContainsKey(item.SoulFormId))
                _soulsByForm.Add(item.SoulFormId, item);
        }
    }

    private void ValidateItems()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (items == null) return;

        var seen = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            ItemData item = items[i];
            if (item == null)
                continue;

            string code = item.ItemCode;
            if (string.IsNullOrWhiteSpace(code))
            {
                Debug.LogWarning("[ItemDatabase] Empty itemCode at index " + i + ".", this);
                continue;
            }

            if (code.Trim() != code)
                Debug.LogWarning("[ItemDatabase] itemCode has leading/trailing whitespace: '" + code + "'.", this);

            if (!seen.Add(code))
                Debug.LogWarning("[ItemDatabase] Duplicate itemCode: '" + code + "'.", this);
        }

        var seenSoulForms = new HashSet<PlayerFormId>();
        for (int i = 0; i < items.Count; i++)
        {
            ItemData item = items[i];
            if (item == null || item.ItemType != ItemType.Soul)
                continue;

            if (!seenSoulForms.Add(item.SoulFormId))
                Debug.LogWarning("[ItemDatabase] Duplicate soul form: '" + item.SoulFormId + "'.", this);
        }
#endif
    }
}
