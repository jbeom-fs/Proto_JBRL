using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class BossExitPortal : Portal
{
    private BossEncounterController _controller;

    private void Reset()
    {
        portalColor = new Color(0.95f, 0.75f, 0.25f, 1f);
    }

    protected override void Awake()
    {
        base.Awake();
        SetLocked(true);
        SetColliderEnabled(false);
    }

    public void Bind(BossEncounterController controller)
    {
        _controller = controller;
    }

    public override void ResetRuntimeState()
    {
        base.ResetRuntimeState();
        _controller = null;
        SetLocked(true);
        gameObject.SetActive(false);
    }

    protected override bool OnPlayerEntered(PlayerController player)
    {
        return _controller != null && _controller.RequestProceed(player);
    }
}
