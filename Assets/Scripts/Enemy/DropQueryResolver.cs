using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct DropQueryResult
{
    public DropQueryResult(ItemData item, int tier)
    {
        Item = item;
        Tier = tier;
    }

    public ItemData Item { get; }
    public int Tier { get; }
}

public static class DropQueryResolver
{
    private const int TierCount = 3;
    private const PlayerFormId AnyForm = (PlayerFormId)(-1);

    private readonly struct CandidateKey : IEquatable<CandidateKey>
    {
        public CandidateKey(ItemType itemType, PlayerFormId form, int tier)
        {
            ItemType = itemType;
            Form = form;
            Tier = tier;
        }

        public ItemType ItemType { get; }
        public PlayerFormId Form { get; }
        public int Tier { get; }

        public bool Equals(CandidateKey other)
        {
            return ItemType == other.ItemType && Form == other.Form && Tier == other.Tier;
        }

        public override bool Equals(object obj)
        {
            return obj is CandidateKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)ItemType;
                hash = (hash * 397) ^ (int)Form;
                hash = (hash * 397) ^ Tier;
                return hash;
            }
        }
    }

    private static readonly Dictionary<CandidateKey, List<ItemData>> s_Candidates =
        new Dictionary<CandidateKey, List<ItemData>>();
    private static readonly List<ItemData> s_AllItems = new List<ItemData>(64);
    private static readonly float[] s_TierWeightBuffer = new float[TierCount];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static readonly Dictionary<CandidateKey, int> s_MissingCandidateWarnings =
        new Dictionary<CandidateKey, int>();
#endif
    private static ItemDatabase s_CachedDatabase;
    private static bool s_WarnedMissingCurrentForm;

    public static void Invalidate()
    {
        s_CachedDatabase = null;
        s_Candidates.Clear();
        s_AllItems.Clear();
    }

    public static PlayerFormId ResolveCurrentForm()
    {
        PlayerController activePlayer = PlayerController.Active;
        if (activePlayer != null &&
            activePlayer.TryGetComponent(out PlayerCombatController combat))
        {
            return combat.CurrentFormId;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!s_WarnedMissingCurrentForm)
        {
            Debug.LogWarning(
                "[DropQueryResolver] Current player form is unavailable. Falling back to Normal.");
            s_WarnedMissingCurrentForm = true;
        }
#endif
        return PlayerFormId.Normal;
    }

    public static bool TryResolve(
        ItemDatabase database,
        EnemyDropQuery query,
        PlayerFormId currentForm,
        System.Random rng,
        out DropQueryResult result)
    {
        result = default;
        if (database == null || rng == null)
            return false;

        EnsureIndex(database);
        if (!TryChooseTier(query, rng, out int tier))
            return false;

        PlayerFormId lookupForm = ResolveLookupForm(query, currentForm);
        CandidateKey key = new CandidateKey(query.itemType, lookupForm, tier);
        if (!s_Candidates.TryGetValue(key, out List<ItemData> candidates) || candidates.Count == 0)
        {
            AccumulateMissingCandidateWarning(key);
            result = new DropQueryResult(null, tier);
            return false;
        }

        ItemData item = candidates[rng.Next(candidates.Count)];
        result = new DropQueryResult(item, tier);
        return true;
    }

    public static void FlushWarnings()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        foreach (KeyValuePair<CandidateKey, int> pair in s_MissingCandidateWarnings)
        {
            CandidateKey key = pair.Key;
            string formLabel = key.Form == AnyForm ? "Any" : key.Form.ToString();
            Debug.LogWarning(
                "[DropQueryResolver] " + key.ItemType + "/" + formLabel +
                " tier " + key.Tier + " 후보 없음 → 드랍 취소 (" + pair.Value + "회)");
        }

        s_MissingCandidateWarnings.Clear();
#endif
    }

    private static void EnsureIndex(ItemDatabase database)
    {
        if (ReferenceEquals(s_CachedDatabase, database))
            return;

        s_CachedDatabase = database;
        s_Candidates.Clear();
        s_AllItems.Clear();
        database.GetAllItems(s_AllItems);

        for (int i = 0; i < s_AllItems.Count; i++)
        {
            ItemData item = s_AllItems[i];
            if (item == null || string.IsNullOrWhiteSpace(item.ItemCode))
                continue;

            int tier = GetTier(item);
            if ((uint)tier >= TierCount)
                continue;

            if (IsEngravingLike(item.ItemType))
            {
                if (!TryGetOwningForm(item, out PlayerFormId owningForm))
                    continue;

                AddCandidate(new CandidateKey(item.ItemType, owningForm, tier), item);
                AddCandidate(new CandidateKey(item.ItemType, AnyForm, tier), item);
            }
            else
            {
                AddCandidate(new CandidateKey(item.ItemType, AnyForm, tier), item);
            }
        }

        s_AllItems.Clear();
    }

    private static void AddCandidate(CandidateKey key, ItemData item)
    {
        if (!s_Candidates.TryGetValue(key, out List<ItemData> candidates))
        {
            candidates = new List<ItemData>();
            s_Candidates.Add(key, candidates);
        }

        candidates.Add(item);
    }

    private static bool TryChooseTier(
        EnemyDropQuery query,
        System.Random rng,
        out int selectedTier)
    {
        s_TierWeightBuffer[0] = query.tierWeight0;
        s_TierWeightBuffer[1] = query.tierWeight1;
        s_TierWeightBuffer[2] = query.tierWeight2;
        return TryChooseWeightedIndex(s_TierWeightBuffer, rng, out selectedTier);
    }

    internal static bool TryChooseWeightedIndex(
        float[] weights,
        System.Random rng,
        out int selectedIndex)
    {
        selectedIndex = -1;
        if (weights == null || weights.Length == 0 || rng == null)
            return false;

        double totalWeight = 0d;
        for (int i = 0; i < weights.Length; i++)
            totalWeight += GetUsableWeight(weights[i]);

        if (totalWeight <= 0d)
            return false;

        double roll = rng.NextDouble() * totalWeight;
        double accumulated = 0d;
        int lastWeightedIndex = -1;
        for (int i = 0; i < weights.Length; i++)
        {
            double weight = GetUsableWeight(weights[i]);
            if (weight <= 0d)
                continue;

            accumulated += weight;
            lastWeightedIndex = i;
            if (roll < accumulated)
            {
                selectedIndex = i;
                return true;
            }
        }

        selectedIndex = lastWeightedIndex;
        return selectedIndex >= 0;
    }

    private static double GetUsableWeight(float weight)
    {
        if (!(weight > 0f) || float.IsNaN(weight) || float.IsInfinity(weight))
            return 0d;

        return weight;
    }

    private static PlayerFormId ResolveLookupForm(EnemyDropQuery query, PlayerFormId currentForm)
    {
        if (!IsEngravingLike(query.itemType) || query.formScope == DropFormScope.Any)
            return AnyForm;

        return query.formScope == DropFormScope.Specific
            ? query.specificForm
            : currentForm;
    }

    private static bool IsEngravingLike(ItemType itemType)
    {
        return itemType == ItemType.Engraving || itemType == ItemType.PassiveEngraving;
    }

    private static bool TryGetOwningForm(ItemData item, out PlayerFormId form)
    {
        if (item.ItemType == ItemType.Engraving && item.Engraving != null)
        {
            form = item.Engraving.owningForm;
            return true;
        }

        if (item.ItemType == ItemType.PassiveEngraving && item.PassiveEngraving != null)
        {
            form = item.PassiveEngraving.owningForm;
            return true;
        }

        form = default;
        return false;
    }

    // 각인류 = SO의 grade가 단일 진실. 그 외 = ItemData.rarity.
    public static int GetTier(ItemData item)
    {
        if (item == null)
            return -1;

        if (item.ItemType == ItemType.Engraving)
            return item.Engraving != null ? (int)item.Engraving.grade : -1;

        if (item.ItemType == ItemType.PassiveEngraving)
            return item.PassiveEngraving != null ? (int)item.PassiveEngraving.grade : -1;

        return (int)item.Rarity;
    }

    private static void AccumulateMissingCandidateWarning(CandidateKey key)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (s_MissingCandidateWarnings.TryGetValue(key, out int count))
            s_MissingCandidateWarnings[key] = count + 1;
        else
            s_MissingCandidateWarnings.Add(key, 1);
#endif
    }
}
