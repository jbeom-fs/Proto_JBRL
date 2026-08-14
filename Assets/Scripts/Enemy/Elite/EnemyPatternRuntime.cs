public abstract class EnemyPatternRuntime
{
    public bool IsFinished { get; protected set; }

    public abstract void Start(EnemyPatternContext context);
    public abstract void Tick(float deltaTime);

    public virtual void Cancel()
    {
        IsFinished = true;
    }
}
