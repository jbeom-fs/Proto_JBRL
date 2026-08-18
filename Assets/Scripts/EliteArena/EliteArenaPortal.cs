using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class EliteArenaPortal : Portal
{
    [SerializeField] private bool hideVisualWhenCompleted = true;

    private EliteArenaEncounterController _controller;
    private RoomInfo _room;
    private bool _bound;
    private bool _completed;
    private int _completedRoomKey;

    protected override string MissingVisualWarningMessage =>
        "SpriteRenderer.sprite is missing. Assign Elite_portal sprite on the prefab.";

    protected override string FogVisibilityWarningMessage =>
        "FogVisibilityRenderer was disabled so portal remains visible in the current room.";

    private void Reset()
    {
        portalColor = new Color(0.25f, 0.9f, 1f, 1f);
    }

    public void Bind(EliteArenaEncounterController controller, RoomInfo room)
    {
        _controller = controller;
        _room = room;
        _bound = true;
        SetLocked(false);

        bool isCompleted = IsCompletedForRoom(room);
        SetColliderOnly(!isCompleted);
        EnsureVisual();
        SetVisualVisible(!isCompleted || !hideVisualWhenCompleted);
    }

    public bool IsCompletedForRoom(RoomInfo room)
    {
        return _completed && _completedRoomKey == room.StableRoomKey;
    }

    public void MarkCompletedAndDisable(RoomInfo room)
    {
        _completed = true;
        _completedRoomKey = room.StableRoomKey;
        SetLocked(true);
        SetColliderOnly(false);

        if (hideVisualWhenCompleted)
            SetVisualVisible(false);
    }

    public override void ResetRuntimeState()
    {
        base.ResetRuntimeState();
        _bound = false;
        _completed = false;
        _completedRoomKey = 0;
        _controller = null;
    }

    protected override bool CanTrigger()
    {
        return _bound && !IsCompletedForRoom(_room);
    }

    protected override bool OnPlayerEntered(PlayerController player)
    {
        return _controller != null &&
            _controller.TryEnterArenaFromPortal(this, _room, player);
    }
}
