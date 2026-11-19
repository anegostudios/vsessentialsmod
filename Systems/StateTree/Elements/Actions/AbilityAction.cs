using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Action that executes an ability.
/// </summary>
public class AbilityAction(float weight, Ability ability) : Action(weight)
{
    public override void Execute(float deltaTime)
    {
        ability.Execute();
    }
}
