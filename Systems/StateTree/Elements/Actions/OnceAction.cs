namespace Vintagestory.StateTree;

/// <summary>
/// Action that ensures its enclosed action is executed only once until the action has been reset (i.e. the containing
/// node was deselected).
/// </summary>
public class OnceAction(float weight, Action action) : Action(weight)
{
    private bool hasExecuted = false;

    public override bool ShouldExecute() => action.ShouldExecute();

    public override void Execute(float deltaTime)
    {
        if (hasExecuted) return;
        action.Execute(deltaTime);
        hasExecuted = true;
    }

    public override void Reset()
    {
        action.Reset();
        hasExecuted = false;
    }
}
