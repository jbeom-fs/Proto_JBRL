using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class DungeonEntryStation : MonoBehaviour
{
    [SerializeField] private FormSelectScreenUI sceneUI;

    private PlayerController _player;
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

    private void OnDisable()
    {
        _player = null;
        _input = null;
        _playerInRange = false;
    }

    private void Update()
    {
        if (!_playerInRange || _player == null || _input == null || !_input.InteractConfirmPressedThisFrame)
            return;

        if (sceneUI == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_warnedMissingUI)
            {
                Debug.LogWarning("[DungeonEntryStation] sceneUI is not assigned.", this);
                _warnedMissingUI = true;
            }
#endif
            return;
        }

        if (!sceneUI.IsOpen)
            sceneUI.Open(_player);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        _player = player;
        _playerInRange = true;
        _input = other.GetComponentInParent<PlayerInputReader>();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other == null || other.GetComponentInParent<PlayerController>() != _player)
            return;

        _player = null;
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
