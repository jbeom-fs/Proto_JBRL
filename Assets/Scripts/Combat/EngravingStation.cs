using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class EngravingStation : MonoBehaviour
{
    private EngravingLoadoutUIController _ui;
    private PlayerInputReader _input;
    private bool _playerInRange;
    private bool _warnedMissingUI;

    public void Bind(EngravingLoadoutUIController ui)
    {
        _ui = ui;
    }

    private void Reset()
    {
        SetColliderTrigger();
    }

    private void OnValidate()
    {
        SetColliderTrigger();
    }

    private void OnDisable()
    {
        _playerInRange = false;
        _input = null;
    }

    private void Update()
    {
        if (!_playerInRange || _input == null || !_input.InteractConfirmPressedThisFrame)
            return;

        if (_ui == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_warnedMissingUI)
            {
                Debug.LogWarning("[EngravingStation] loadout UI is not bound.", this);
                _warnedMissingUI = true;
            }
#endif
            return;
        }

        if (!_ui.IsOpen)
            _ui.Open(this);
    }

    public void NotifyConsumed()
    {
        gameObject.SetActive(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        _playerInRange = true;
        _input = other.GetComponentInParent<PlayerInputReader>();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other == null)
            return;

        if (other.GetComponentInParent<PlayerController>() == null)
            return;

        _playerInRange = false;
        _input = null;
    }

    private void SetColliderTrigger()
    {
        Collider2D trigger = GetComponent<Collider2D>();
        if (trigger != null)
            trigger.isTrigger = true;
    }
}
