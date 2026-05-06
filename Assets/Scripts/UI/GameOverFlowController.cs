using System.Collections;
using UnityEngine;

public class GameOverFlowController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private CombatEventChannel combatChannel;
    [SerializeField] private GameOverUIController gameOverUI;
    [SerializeField] private MonoBehaviour restartHandler;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float deathUiDelay = 1f;

    private Coroutine _showRoutine;
    private IGameOverRestartHandler _restartHandler;
    private bool _flowStarted;

    private void Awake()
    {
        if (gameOverUI == null)
            gameOverUI = GetComponent<GameOverUIController>();

        _restartHandler = restartHandler as IGameOverRestartHandler;
        if (_restartHandler == null)
            _restartHandler = GetComponent<IGameOverRestartHandler>();

        if (gameOverUI != null)
            gameOverUI.SetConfirmAction(ConfirmGameOver);
    }

    private void OnEnable()
    {
        if (combatChannel != null)
            combatChannel.OnPlayerDied += HandlePlayerDied;
    }

    private void OnDisable()
    {
        if (combatChannel != null)
            combatChannel.OnPlayerDied -= HandlePlayerDied;

        if (_showRoutine != null)
        {
            StopCoroutine(_showRoutine);
            _showRoutine = null;
        }
    }

    private void HandlePlayerDied(PlayerCombatController player)
    {
        if (_flowStarted)
            return;

        _flowStarted = true;

        if (_showRoutine != null)
            StopCoroutine(_showRoutine);

        _showRoutine = StartCoroutine(ShowAfterDeathDelay());
    }

    private IEnumerator ShowAfterDeathDelay()
    {
        if (deathUiDelay > 0f)
            yield return new WaitForSeconds(deathUiDelay);

        gameOverUI?.Show();
        _showRoutine = null;
    }

    private void ConfirmGameOver()
    {
        gameOverUI?.HideImmediate();
        _restartHandler?.RestartAfterGameOver();
    }
}
