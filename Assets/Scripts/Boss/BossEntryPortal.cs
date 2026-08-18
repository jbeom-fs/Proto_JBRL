using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class BossEntryPortal : Portal
{
    private RestAreaController _controller;

    private void Reset()
    {
        portalColor = new Color(0.85f, 0.25f, 0.25f, 1f);
    }

    protected override void Awake()
    {
        base.Awake();
        SetLocked(true);
        SetColliderEnabled(false);
    }

    public void Bind(RestAreaController controller)
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
        return _controller != null && _controller.RequestEnterBoss(player);
    }
}
