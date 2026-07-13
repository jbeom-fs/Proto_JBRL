using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ArenaHealthBarRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image hpFill;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private ArenaAilmentStripUI ailmentStrip;

    private EnemyController _enemy;

    public EnemyController BoundEnemy => _enemy;
    public ArenaAilmentStripUI AilmentStrip => ailmentStrip;
    public bool IsAvailable => _enemy == null && !gameObject.activeSelf;

    public void Bind(EnemyController enemy)
    {
        if (enemy == null)
            return;

        _enemy = enemy;

        if (nameText != null)
            nameText.text = enemy.DisplayName;

        if (backgroundImage != null)
            backgroundImage.enabled = true;

        ailmentStrip?.Bind(enemy);
        RefreshHp();
        gameObject.SetActive(true);
    }

    public void Release()
    {
        _enemy = null;

        if (nameText != null)
            nameText.text = string.Empty;

        ailmentStrip?.Release();
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_enemy == null || !_enemy.gameObject.activeInHierarchy || !_enemy.IsAlive)
        {
            Release();
            return;
        }

        RefreshHp();
    }

    private void RefreshHp()
    {
        if (hpFill == null || _enemy == null)
            return;

        int maxHp = _enemy.MaxHp;
        hpFill.fillAmount = maxHp > 0
            ? Mathf.Clamp01(_enemy.CurrentHp / (float)maxHp)
            : 0f;
    }
}
