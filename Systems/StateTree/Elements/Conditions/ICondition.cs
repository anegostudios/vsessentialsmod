namespace Vintagestory.StateTree;

public interface ICondition
{
    /// <summary>
    /// Evaluates the condition.
    /// </summary>
    /// <returns>A score between 0.0 and 1.0 indicating the condition's satisfaction</returns>
    public float Evaluate();
}
