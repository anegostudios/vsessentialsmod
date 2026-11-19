using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Action that sets the entity's current target block.
/// </summary>
public class TargetBlockAction(
    float weight,
    Entity entity,
    EntityBehaviorMemory memory,
    TargetBlockActionMode mode,
    string blockCode,
    BlockPos minPos,
    BlockPos maxPos
) : Action(weight)
{
    private static BlockPos up = new(0, 1, 0);

    public override void Execute(float deltaTime)
    {
        entity.World.BlockAccessor.SearchBlocks(entity.Pos.AsBlockPos + minPos, entity.Pos.AsBlockPos + maxPos, OnBlock);
    }

    private bool OnBlock(Block block, BlockPos position)
    {
        switch (mode)
        {
            case TargetBlockActionMode.ByCode:
                if (!block.WildCardMatch(blockCode))
                {
                    return true;
                }
                break;
            case TargetBlockActionMode.Liquid:
                if (!block.IsLiquid())
                {
                    return true;
                }
                break;
            case TargetBlockActionMode.Ground:
                // TODO: this probably needs to be smarter about accommodating the entity's CollisionBox so that it works for the "move to block" action
                if (block.IsLiquid() || entity.World.BlockAccessor.GetBlockId(position + up) > 0)
                {
                    return true;
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        memory.SetTargetBlock(block, position);
        return false;
    }
}
