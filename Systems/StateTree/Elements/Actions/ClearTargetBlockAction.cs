using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Action that clears the entity's current target block.
/// </summary>
public class ClearTargetBlockAction(float weight, EntityBehaviorMemory memory) : Action(weight)
{
    public override void Execute(float deltaTime)
    {
        memory.ClearTargetBlock();
    }
}
