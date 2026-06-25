using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class TownSoulAltar : MonoBehaviour
{
    [SerializeField] private SoulAltarUIController altarUI;

    private PlayerInputReader _input;
    private bool _playerInRange;
    private bool _warnedMissingUI;

    private void Reset()
    {
        SetColliderTrigger();
    }

    private void OnValidate()
    {
        SetColliderTrigger();
    }

    private void Update()
    {
        if (!_playerInRange || _input == null || !_input.InteractConfirmPressedThisFrame)
            return;

        if (altarUI == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_warnedMissingUI)
            {
                Debug.LogWarning("[TownSoulAltar] altarUI is not assigned.", this);
                _warnedMissingUI = true;
            }
#endif
            return;
        }

        if (!altarUI.IsOpen)
            altarUI.Open();
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
