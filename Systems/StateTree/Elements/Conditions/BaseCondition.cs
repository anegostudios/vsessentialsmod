namespace Vintagestory.StateTree;

public abstract class BaseCondition(bool invert) : ICondition
{
    protected float Evaluate(float value)
    {
        return invert ? 1f - value : value;
    }

    public abstract float Evaluate();
}
