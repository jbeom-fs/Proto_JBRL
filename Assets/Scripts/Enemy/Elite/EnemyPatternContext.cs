using UnityEngine;

public sealed class EnemyPatternContext
{
    public EnemyBrain Brain { get; private set; }
    public EnemyController Enemy { get; private set; }
    public EnemyData Data { get; private set; }
    public Transform SelfTransform { get; private set; }
    public Transform Target { get; private set; }
    public MovementHandler Movement { get; private set; }
    public ActionHandler ActionHandler { get; private set; }
    public EnemyAnimationController Animation { get; private set; }
    public Collider2D Collider { get; private set; }
    public DungeonManager DungeonManager { get; private set; }
    public ProjectileFireService ProjectileFireService { get; private set; }
    public MonoBehaviour CoroutineRunner { get; private set; }

    public bool HasLiveTarget =>
        Target != null &&
        Target.gameObject.activeInHierarchy &&
        (Brain == null || Brain.Target == null || Brain.Target.Damageable == null || Brain.Target.Damageable.IsAlive);

    public void Initialize(
        EnemyBrain brain,
        EnemyAnimationController animation,
        Collider2D collider,
        ProjectileFireService projectileFireService,
        MonoBehaviour coroutineRunner)
    {
        Brain = brain;
        Enemy = brain != null ? brain.Enemy : null;
        Data = brain != null ? brain.Data : null;
        SelfTransform = brain != null ? brain.transform : null;
        Target = brain != null ? brain.player : null;
        Movement = brain != null ? brain.Movement : null;
        ActionHandler = brain != null ? brain.Action : null;
        Animation = animation;
        Collider = collider;
        DungeonManager = brain != null ? brain.dungeonManager : null;
        ProjectileFireService = projectileFireService;
        CoroutineRunner = coroutineRunner;
    }

}
