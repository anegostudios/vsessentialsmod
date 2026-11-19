using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Action that clears the entity's current target entity.
/// </summary>
public class ClearTargetEntityAction(float weight, EntityBehaviorMemory memory) : Action(weight)
{
    public override void Execute(float deltaTime)
    {
        memory.ClearTargetEntity();
    }
}
