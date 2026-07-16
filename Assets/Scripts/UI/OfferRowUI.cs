using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class OfferRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private Button buyButton;

    public RestAreaShopOffer Offer { get; private set; }
    public Button PurchaseButton => buyButton;

    public void Bind(RestAreaShopOffer offer, int level, int totalValue, int cost, bool canBuy)
    {
        Offer = offer;

        if (nameText != null)
            nameText.text = offer.DisplayName;

        if (levelText != null)
            levelText.text = string.Format(UiMessages.RestAreaOfferLevelFormat, level, totalValue);

        if (costText != null)
            costText.text = string.Format(UiMessages.RestAreaOfferCostFormat, cost);

        if (buyButton != null)
            buyButton.interactable = canBuy;

        SetActive(true);
    }

    public void SetActive(bool active)
    {
        gameObject.SetActive(active);
    }
}
