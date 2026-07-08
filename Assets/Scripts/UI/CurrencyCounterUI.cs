using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CurrencyCounterUI : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private Image iconImage;
    [SerializeField] private string currencyItemCode = "Currency";

    private ItemData _currencyItem;
    private bool _isResolved;
    private bool _isSubscribed;
    private bool _isVisible = true;
    private int _lastCount = -1;

    private void Awake()
    {
        if (countText == null)
            countText = GetComponentInChildren<TMP_Text>(true);

        if (iconImage == null)
            iconImage = GetComponentInChildren<Image>(true);

        SetVisible(false);
    }

    private void OnEnable()
    {
        SetVisible(false);
        TryResolveAndSubscribe();
    }

    private void Update()
    {
        if (_isResolved)
            return;

        TryResolveAndSubscribe();
    }

    private void OnDisable()
    {
        UnsubscribeInventory();
        _isResolved = false;
    }

    private bool TryResolveAndSubscribe()
    {
        if (inventory == null)
            inventory = ResolvePlayerInventory();

        if (inventory == null)
            return false;

        if (!inventory.TryGetDatabaseItem(currencyItemCode, out _currencyItem) || _currencyItem == null)
            return false;

        if (iconImage != null)
            iconImage.sprite = _currencyItem.Icon;

        SubscribeInventory();
        _isResolved = true;
        Refresh();
        return true;
    }

    private static PlayerInventory ResolvePlayerInventory()
    {
        PlayerController activePlayer = PlayerController.Active;
        if (activePlayer == null)
            return null;

        return activePlayer.Inventory != null
            ? activePlayer.Inventory
            : activePlayer.GetComponent<PlayerInventory>();
    }

    private void SubscribeInventory()
    {
        if (_isSubscribed || inventory == null)
            return;

        inventory.OnInventoryChanged += Refresh;
        _isSubscribed = true;
    }

    private void UnsubscribeInventory()
    {
        if (!_isSubscribed || inventory == null)
            return;

        inventory.OnInventoryChanged -= Refresh;
        _isSubscribed = false;
    }

    private void Refresh()
    {
        if (inventory == null || _currencyItem == null)
        {
            SetVisible(false);
            return;
        }

        int count = inventory.GetItemCount(_currencyItem);
        SetVisible(count > 0);

        if (count == _lastCount)
            return;

        _lastCount = count;
        if (countText != null)
            countText.SetText("{0}", count);
    }

    private void SetVisible(bool visible)
    {
        if (_isVisible == visible)
            return;

        _isVisible = visible;
        if (iconImage != null)
            iconImage.enabled = visible;
        if (countText != null)
            countText.enabled = visible;
    }
}
