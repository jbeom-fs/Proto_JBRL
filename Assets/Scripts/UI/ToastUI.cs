using TMPro;
using UnityEngine;

public sealed class ToastUI : MonoBehaviour
{
    public static ToastUI Instance { get; private set; }

    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text messageText;

    private float _remainingTime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnDisable()
    {
        _remainingTime = 0f;

        if (root != null)
            root.SetActive(false);
    }

    private void Update()
    {
        if (_remainingTime <= 0f)
            return;

        _remainingTime -= Time.unscaledDeltaTime;
        if (_remainingTime <= 0f && root != null)
            root.SetActive(false);
    }

    public void Show(string message, float duration)
    {
        if (duration <= 0f)
        {
            _remainingTime = 0f;
            if (root != null)
                root.SetActive(false);
            return;
        }

        if (messageText != null)
            messageText.text = message;

        if (root != null)
            root.SetActive(true);

        _remainingTime = duration;
    }
}
