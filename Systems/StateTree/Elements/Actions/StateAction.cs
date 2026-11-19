using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Action that sets the entity's state to the specified index.
/// </summary>
/// <param name="entityStates">The entity's States behavior</param>
/// <param name="stateIndex">The index of the state to set</param>
public class StateAction(float weight, EntityBehaviorStates entityStates, int stateIndex) : Action(weight)
{
    public override void Execute(float deltaTime) => entityStates.SetCurrentStateIndex(stateIndex);
}
