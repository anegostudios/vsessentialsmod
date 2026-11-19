namespace Vintagestory.StateTree;

public abstract class Action(float weight)
{
    public float Weight => weight;

    public virtual bool ShouldExecute() => true;
    public abstract void Execute(float deltaTime);
    public virtual bool HasFinished() => true;
    public virtual void Reset() {}
}
