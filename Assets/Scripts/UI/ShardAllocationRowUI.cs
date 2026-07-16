using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ShardAllocationRowUI : MonoBehaviour
{
    [SerializeField] private PlayerFormId form;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text ownedText;
    [SerializeField] private TMP_InputField amountInput;
    [SerializeField] private Button minusButton;
    [SerializeField] private Button plusButton;
    [SerializeField] private Button maxButton;

    private ShardPaymentModalUI _owner;
    private bool _listenersRegistered;

    public PlayerFormId Form => form;
    public int Amount { get; private set; }
    public int OwnedCount { get; private set; }

    public void Initialize(ShardPaymentModalUI owner)
    {
        _owner = owner;
        if (_listenersRegistered)
            return;

        if (minusButton != null)
            minusButton.onClick.AddListener(HandleMinusClicked);
        if (plusButton != null)
            plusButton.onClick.AddListener(HandlePlusClicked);
        if (maxButton != null)
            maxButton.onClick.AddListener(HandleMaxClicked);
        if (amountInput != null)
        {
            amountInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            amountInput.onEndEdit.AddListener(HandleInputEndEdit);
        }

        _listenersRegistered = true;
    }

    public void SetOwned(ItemData shardItem, int count)
    {
        OwnedCount = Mathf.Max(0, count);
        if (iconImage != null)
        {
            iconImage.sprite = shardItem != null ? shardItem.Icon : null;
            iconImage.enabled = iconImage.sprite != null;
        }

        if (ownedText != null)
            ownedText.SetText("{0}", OwnedCount);
    }

    public void SetAmount(int amount)
    {
        Amount = Mathf.Max(0, amount);
        if (amountInput != null)
            amountInput.SetTextWithoutNotify(Amount.ToString());
    }

    private void HandleMinusClicked()
    {
        _owner?.RequestAmount(this, Amount - 1);
    }

    private void HandlePlusClicked()
    {
        _owner?.RequestAmount(this, Amount + 1);
    }

    private void HandleMaxClicked()
    {
        _owner?.RequestAmount(this, int.MaxValue);
    }

    private void HandleInputEndEdit(string value)
    {
        if (!int.TryParse(value, out int requested))
            requested = 0;

        _owner?.RequestAmount(this, requested);
    }
}
