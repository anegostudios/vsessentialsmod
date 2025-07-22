using System;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    /// <summary>
    /// When a block with this behavior is broken. it will turn into another specified block instead of air.
    /// This block behavior uses the code "TransformBreak".
    /// </summary>
    /// <example>
    /// <code lang="json">
    ///"behaviors": [
	///	{
	///		"name": "TransformBreak",
	///		"properties": {
	///			"transformIntoBlock": "termitemound-harvested-{rock}-{size}",
	///			"withDrops": true
	///		}
	///	}
	///]
    /// </code></example>
    [DocumentAsJson]
    [AddDocumentationProperty("TransformIntoBlock", "The asset location of the block to replace this one with.", "Vintagestory.API.Common.AssetLocation", "Required", "")]
    public class BlockBehaviorTransformBreak : BlockBehavior
    {
        
        Block transformIntoBlock;
        JsonObject properties;

        /// <summary>
        /// When mined/destroyed, should this block drop its normal drops?
        /// </summary>
        [DocumentAsJson("Optional", "False")]
        bool withDrops;

        public BlockBehaviorTransformBreak(Block block) : base(block)
        {
        }

        public override void OnLoaded(ICoreAPI api)
        {
            if (!properties["transformIntoBlock"].Exists)
            {
                api.Logger.Error("Block {0}, required property transformIntoBlock does not exist", this.block.Code);
                return;
            }

            var loc = AssetLocation.Create(properties["transformIntoBlock"].AsString(), this.block.Code.Domain);
            transformIntoBlock = api.World.GetBlock(loc);
            if (transformIntoBlock == null)
            {
                api.Logger.Error("Block {0}, transformIntoBlock code '{1}' - no such block exists. Block will not transform upon breakage.", this.block.Code, loc);
                return;
            }

            withDrops = properties["withDrops"].AsBool(false);
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            this.properties= properties;
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, ref EnumHandling handling)
        {
            if (transformIntoBlock != null)
            {
                handling = EnumHandling.PreventDefault;
                world.BlockAccessor.SetBlock(transformIntoBlock.Id, pos);

                if (withDrops)
                {
                    spawnDrops(world, pos, byPlayer);
                }

                block.SpawnBlockBrokenParticles(pos, byPlayer);
            }
        }


        private void spawnDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
        {
            if (world.Side == EnumAppSide.Server && (byPlayer == null || byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative))
            {
                ItemStack[] drops = block.GetDrops(world, pos, byPlayer, 1);

                if (drops != null)
                {
                    for (int i = 0; i < drops.Length; i++)
                    {
                        if (block.SplitDropStacks)
                        {
                            for (int k = 0; k < drops[i].StackSize; k++)
                            {
                                ItemStack stack = drops[i].Clone();
                                stack.StackSize = 1;
                                world.SpawnItemEntity(stack, pos, null);
                            }
                        }
                        else
                        {
                            world.SpawnItemEntity(drops[i].Clone(), pos, null);
                        }

                    }
                }

                world.PlaySoundAt(block.Sounds?.GetBreakSound(byPlayer), pos, 0, byPlayer);
            }

            
        }
    }
}

