public abstract class ElitePatternRuntime
{
    public bool IsFinished { get; protected set; }

    public abstract void Start(ElitePatternContext context);
    public abstract void Tick(float deltaTime);

    public virtual void Cancel()
    {
        IsFinished = true;
    }
}
