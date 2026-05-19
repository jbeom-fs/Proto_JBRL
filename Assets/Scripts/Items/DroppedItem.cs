using UnityEngine;

public sealed class DroppedItem : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Collider2D pickupCollider;
    [SerializeField] private string eliteKeyItemCode = "elite_key";
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 5;

    private string _itemCode;
    private int _amount;
    private ItemType _itemType;
    private bool _initialized;

    public void Initialize(ItemData itemData, int amount)
    {
        if (itemData == null)
        {
            _initialized = false;
            return;
        }

        _itemCode = itemData.ItemCode;
        _amount = Mathf.Max(1, amount);
        _itemType = itemData.ItemType;
        _initialized = true;

        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = itemData.Icon;
            spriteRenderer.sortingLayerName = sortingLayerName;
            spriteRenderer.sortingOrder = sortingOrder;
            spriteRenderer.enabled = itemData.Icon != null;
        }

        if (itemData.Icon == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[DroppedItem] Item '" + _itemCode + "' has no icon.", this);
#endif
        }

        if (pickupCollider != null)
            pickupCollider.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!_initialized)
            return;

        if (!other.TryGetComponent<PlayerEliteKeyInventory>(out var keyInventory))
            return;

        if (_itemType == ItemType.Key && _itemCode == eliteKeyItemCode)
        {
            keyInventory.GrantEliteKey();
            DropItemSpawner.Instance?.Unregister(this);
            Destroy(gameObject);
        }
    }
}
