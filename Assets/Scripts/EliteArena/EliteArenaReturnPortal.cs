using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class EliteArenaReturnPortal : Portal
{
    private EliteArenaEncounterController _controller;

    private void Reset()
    {
        portalColor = new Color(0.25f, 1f, 0.45f, 1f);
    }

    public void Bind(EliteArenaEncounterController controller)
    {
        _controller = controller;
    }

    protected override bool OnPlayerEntered(PlayerController player)
    {
        return _controller != null && _controller.TryReturnFromArena(player);
    }
}
