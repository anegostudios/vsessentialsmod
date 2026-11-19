namespace Vintagestory.StateTree;

/// <summary>
/// Debug condition that always returns a constant value.
/// </summary>
public class DebugConstantCondition(bool invert, float value) : BaseCondition(invert)
{
    public override float Evaluate()
    {
        return base.Evaluate(value);
    }
}
