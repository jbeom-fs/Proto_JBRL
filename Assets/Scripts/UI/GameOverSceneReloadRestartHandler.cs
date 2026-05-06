using UnityEngine.SceneManagement;

public class GameOverSceneReloadRestartHandler : UnityEngine.MonoBehaviour, IGameOverRestartHandler
{
    public void RestartAfterGameOver()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.name);
    }
}
