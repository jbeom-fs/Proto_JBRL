using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "RestAreaShopTable", menuName = "JBRogLike/Rest Area/Shop Table")]
public sealed class RestAreaShopTable : ScriptableObject
{
    [SerializeField] private RestAreaShopOffer[] entries = Array.Empty<RestAreaShopOffer>();

    public IReadOnlyList<RestAreaShopOffer> Entries => entries ?? Array.Empty<RestAreaShopOffer>();

    public static int GetCost(RestAreaShopOffer offer, int currentLevel)
    {
        if (offer == null)
            return 0;

        return Mathf.Max(0, offer.BaseCost) * (Mathf.Max(0, currentLevel) + 1);
    }
}

[Serializable]
public sealed class RestAreaShopOffer
{
    [SerializeField] private string displayName;
    [SerializeField] private ItemEffectType effectType;
    [Min(1)]
    [SerializeField] private int perLevelValue = 1;
    [Min(0)]
    [SerializeField] private int baseCost = 10;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? effectType.ToString() : displayName;
    public ItemEffectType EffectType => effectType;
    public int PerLevelValue => perLevelValue;
    public int BaseCost => baseCost;
}
