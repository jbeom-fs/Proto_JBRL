using System.Collections.Generic;
using UnityEngine;

public sealed class DropItemSpawner : MonoBehaviour
{
    public static DropItemSpawner Instance { get; private set; }

    [SerializeField] private ItemDatabase itemDatabase;
    [SerializeField] private DroppedItem droppedItemPrefab;
    [SerializeField] private float dropSpacing = 0.22f;

    private readonly List<DroppedItem> _activeDrops = new List<DroppedItem>(8);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SpawnDrops(EnemyInventory inventory, Vector3 origin)
    {
        if (inventory == null)
            return;

        IReadOnlyList<EnemyDropItem> drops = inventory.GetDropItems();
        if (drops == null || drops.Count == 0)
            return;

        if (itemDatabase == null || droppedItemPrefab == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[DropItemSpawner] ItemDatabase or DroppedItem prefab is not assigned.", this);
#endif
            return;
        }

        for (int i = 0; i < drops.Count; i++)
        {
            EnemyDropItem drop = drops[i];
            if (!itemDatabase.TryGetItem(drop.ItemCode, out ItemData item))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning("[DropItemSpawner] Unknown itemCode: '" + drop.ItemCode + "'.", this);
#endif
                continue;
            }

            Vector3 position = origin + GetOffset(i, drops.Count);
            DroppedItem droppedItem = Instantiate(droppedItemPrefab, position, Quaternion.identity);
            droppedItem.Initialize(item, drop.Amount);
            _activeDrops.Add(droppedItem);
        }
    }

    public void Unregister(DroppedItem droppedItem)
    {
        if (droppedItem == null)
            return;

        _activeDrops.Remove(droppedItem);
    }

    public void ClearAllActiveDrops()
    {
        for (int i = _activeDrops.Count - 1; i >= 0; i--)
        {
            DroppedItem droppedItem = _activeDrops[i];
            if (droppedItem != null)
                Destroy(droppedItem.gameObject);
        }

        _activeDrops.Clear();
    }

    private Vector3 GetOffset(int index, int count)
    {
        if (count <= 1)
            return Vector3.zero;

        float start = -0.5f * (count - 1) * dropSpacing;
        return new Vector3(start + index * dropSpacing, 0f, 0f);
    }
}
