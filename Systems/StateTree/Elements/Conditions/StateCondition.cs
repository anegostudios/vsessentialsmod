using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Condition that compares the entity's current state with the specified state.
/// </summary>
public class StateCondition(
    bool invert,
    EntityBehaviorStates states,
    int stateIndex
) : BaseCondition(invert)
{
    public override float Evaluate()
    {
        int currentStateIndex = states.GetCurrentStateIndex();
        return base.Evaluate(currentStateIndex != -1 && currentStateIndex == stateIndex ? 1f : 0f);
    }
}
