using UnityEngine;

public class DungeonPortal : MonoBehaviour
{
    [SerializeField] private TownDungeonTransitionManager transitionManager;
    [SerializeField] private bool enterOnPlayerTrigger = true;

    public void EnterDungeon()
    {
        if (transitionManager != null)
            transitionManager.EnterDungeon();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!enterOnPlayerTrigger || transitionManager == null)
            return;

        if (other.TryGetComponent<PlayerController>(out _))
            transitionManager.EnterDungeon();
    }
}
