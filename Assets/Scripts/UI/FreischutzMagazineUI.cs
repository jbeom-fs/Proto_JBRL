using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class FreischutzMagazineUI : MonoBehaviour
{
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private Sprite bulletSprite;
    [SerializeField] private Sprite bulletEmptySprite;
    [SerializeField] private RectTransform cellContainer;
    [SerializeField] private Image cellTemplate;
    [SerializeField] private TMP_Text magazineText;
    [SerializeField, Min(0f)] private float cellSpacing = 2f;

    private readonly List<Image> _cells = new();
    private int _lastCurrent = -1;
    private int _lastMax = -1;
    private bool _lastReloading;
    private bool _isVisible = true;

    private void Awake()
    {
        if (combat == null)
            combat = PlayerCombatController.Active;

        if (magazineText == null)
            magazineText = GetComponent<TMP_Text>();

        if (cellTemplate != null)
        {
            if (cellContainer == null)
                cellContainer = cellTemplate.transform.parent as RectTransform;
            cellTemplate.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (combat == null)
            combat = PlayerCombatController.Active;

        bool visible = combat != null &&
                       combat.CurrentBasicAttackMode == PlayerBasicAttackMode.Bullet &&
                       combat.MaxBullet > 0;
        SetVisible(visible);
        if (!visible)
            return;

        int current = combat.CurrentBullet;
        int max = combat.MaxBullet;
        bool reloading = combat.IsReloading;
        if (current == _lastCurrent && max == _lastMax && reloading == _lastReloading)
            return;

        _lastCurrent = current;
        _lastMax = max;
        _lastReloading = reloading;
        EnsureCells(max);
        UpdateCells(current, max);
        UpdateText(current, max, reloading);
    }

    private void EnsureCells(int max)
    {
        int safeMax = Mathf.Max(0, max);
        if (cellTemplate == null || cellContainer == null)
            return;

        cellTemplate.gameObject.SetActive(false);
        while (_cells.Count < safeMax)
        {
            Image cell = Instantiate(cellTemplate, cellContainer);
            cell.gameObject.SetActive(false);
            _cells.Add(cell);
        }

        for (int i = 0; i < _cells.Count; i++)
        {
            if (_cells[i] != null)
                _cells[i].gameObject.SetActive(i < safeMax);
        }
    }

    private void UpdateCells(int current, int max)
    {
        int safeMax = Mathf.Max(0, max);
        int safeCurrent = Mathf.Clamp(current, 0, safeMax);
        for (int i = 0; i < _cells.Count; i++)
        {
            Image cell = _cells[i];
            if (cell == null)
                continue;

            bool active = i < safeMax;
            cell.gameObject.SetActive(active);
            if (!active)
                continue;

            cell.sprite = i < safeCurrent ? bulletSprite : bulletEmptySprite;
            cell.enabled = cell.sprite != null;
            LayoutCell(cell.rectTransform, i, safeMax);
        }
    }

    private void LayoutCell(RectTransform rectTransform, int index, int count)
    {
        if (rectTransform == null || cellTemplate == null)
            return;

        RectTransform templateRect = cellTemplate.rectTransform;
        Vector2 size = templateRect != null ? templateRect.sizeDelta : rectTransform.sizeDelta;
        float step = size.x + cellSpacing;
        float x = (index * step) - (step * Mathf.Max(0, count - 1) * 0.5f);
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = new Vector2(x, 0f);
        rectTransform.sizeDelta = size;
    }

    private void UpdateText(int current, int max, bool reloading)
    {
        if (magazineText == null)
            return;

        if (reloading)
            magazineText.SetText("Reloading... {0}/{1}", current, max);
        else
            magazineText.SetText("{0}/{1}", current, max);
    }

    private void SetVisible(bool visible)
    {
        if (_isVisible == visible)
            return;

        _isVisible = visible;
        if (magazineText != null)
            magazineText.enabled = visible;

        if (cellContainer != null)
            cellContainer.gameObject.SetActive(visible);
    }
}
