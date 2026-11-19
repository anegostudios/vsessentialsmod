namespace Vintagestory.StateTree;

/// <summary>
/// Action that runs another state tree.
/// </summary>
/// <param name="weight"></param>
public class StateTreeAction(float weight, StateTree tree) : Action(weight)
{
    public override void Execute(float deltaTime)
    {
        tree.Tick(deltaTime);
    }
}
