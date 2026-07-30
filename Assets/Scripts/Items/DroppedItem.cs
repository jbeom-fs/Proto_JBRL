using UnityEngine;

public sealed class DroppedItem : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Collider2D pickupCollider;
    [SerializeField] private float iconWorldSize = 0.5f;
    [SerializeField] private string sortingLayerName = "Loot";
    [SerializeField] private int sortingOrder = 0;

    private ItemData _itemData;
    private string _itemCode;
    private int _amount;
    private bool _initialized;
    private CircleCollider2D _circlePickupCollider;
    private float _baseCircleRadius;
    private Vector2 _baseCircleOffset;
    private float _baseColliderScale = 1f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool _passiveDropWarningLogged;
#endif

    private void Awake()
    {
        CachePickupColliderBaseline();
    }

    public void Initialize(ItemData itemData, int amount)
    {
        if (itemData == null)
        {
            _initialized = false;
            return;
        }

        _itemData = itemData;
        _itemCode = itemData.ItemCode;
        _amount = Mathf.Max(1, amount);
        _initialized = true;

        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = itemData.Icon;
            spriteRenderer.sortingLayerName = sortingLayerName;
            spriteRenderer.sortingOrder = sortingOrder;
            spriteRenderer.enabled = itemData.Icon != null;
            NormalizeIconWorldSize();
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

        if (!other.TryGetComponent<PlayerInventory>(out var inventory))
            return;

        if (_itemData.ItemType == ItemType.PassiveEngraving)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_passiveDropWarningLogged)
            {
                _passiveDropWarningLogged = true;
                Debug.LogWarning(
                    "[DroppedItem] Passive engraving drops cannot be picked up: " + _itemCode + ".",
                    this);
            }
#endif

            return;
        }

        if (_itemData.ItemType == ItemType.Engraving)
        {
            EngravingData engraving = _itemData.Engraving;
            if (engraving == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning("[DroppedItem] Engraving item '" + _itemCode + "' has no EngravingData ref.", this);
#endif
                DropItemSpawner.Instance?.Unregister(this);
                Destroy(gameObject);
                return;
            }

            // 각인은 설계상 항상 수량 1(슬롯 토큰, 스택 개념 없음).
            // 드랍 엔트리 _amount는 의도적으로 무시 — 1개만 풀 적재.
            // (엔트리 max>1 오작성은 EnemyDropDatabase.OnValidate가 경고로 잡음.)
            EngravingLoadout loadout = EngravingLoadout.Active;
            if (loadout == null || !loadout.AddToPool(engraving.owningForm, engraving))
                return;

            DropItemSpawner.Instance?.Unregister(this);
            Destroy(gameObject);
            return;
        }

        bool wasUnownedSoul = _itemData.ItemType == ItemType.Soul &&
                              !inventory.OwnsSoulForm(_itemData.SoulFormId);

        if (!inventory.AddItem(_itemData, _amount))
            return;

        if (wasUnownedSoul)
            FormUnlockEvents.RaiseFormUnlocked(_itemData.SoulFormId);

        DropItemSpawner.Instance?.Unregister(this);
        Destroy(gameObject);
    }

    private void NormalizeIconWorldSize()
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null || iconWorldSize <= 0f)
            return;

        Vector2 size = spriteRenderer.sprite.bounds.size;
        float longest = Mathf.Max(size.x, size.y);
        float scale = longest > 0.0001f ? iconWorldSize / longest : 1f;
        spriteRenderer.transform.localScale = new Vector3(scale, scale, 1f);

        if (spriteRenderer.transform == transform)
            RestoreCirclePickupColliderWorldSize();
    }

    private void CachePickupColliderBaseline()
    {
        _circlePickupCollider = pickupCollider as CircleCollider2D;
        if (_circlePickupCollider == null)
            return;

        _baseCircleRadius = _circlePickupCollider.radius;
        _baseCircleOffset = _circlePickupCollider.offset;
        _baseColliderScale = GetColliderScale(_circlePickupCollider);
    }

    private void RestoreCirclePickupColliderWorldSize()
    {
        if (_circlePickupCollider == null)
            CachePickupColliderBaseline();
        if (_circlePickupCollider == null)
            return;

        float currentScale = GetColliderScale(_circlePickupCollider);
        if (currentScale <= 0.0001f)
            return;

        float ratio = _baseColliderScale / currentScale;
        _circlePickupCollider.radius = _baseCircleRadius * ratio;
        _circlePickupCollider.offset = _baseCircleOffset * ratio;
    }

    private static float GetColliderScale(Collider2D collider)
    {
        if (collider == null)
            return 1f;

        Vector3 scale = collider.transform.lossyScale;
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
    }
}
