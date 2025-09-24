using System;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Turns this block into a multiblock, allowing it to be larger than a single block.
    /// By default, all blocks will have the same properties. If you need different properties or functionality for each block section, you will need to use a new block class.
    /// Uses the code "Multiblock".
    /// </summary>
    /// <example>
    /// <code lang="json">
    ///"behaviors": [
	///	{
	///		"name": "Multiblock",
	///		"properties": {
	///			"sizex": 1,
	///			"sizey": 3,
	///			"sizez": 1,
	///			"cposition": {
	///				"x": 0,
	///				"y": 0,
	///				"z": 0
	///			}
	///		}
	///	}
	///],
    /// </code></example>
    [DocumentAsJson]
    public class BlockBehaviorMultiblock : BlockBehavior
    {
        /// <summary>
        /// The size in blocks, in the X axis, of the multiblock. Maximum of 5.
        /// </summary>
        [DocumentAsJson("Recommended", "3")]
        int SizeX;

        /// <summary>
        /// The size in blocks, in the Y axis, of the multiblock. Maximum of 5.
        /// </summary>
        [DocumentAsJson("Recommended", "3")]
        int SizeY;

        /// <summary>
        /// The size in blocks, in the Z axis, of the multiblock. Maximum of 5.
        /// </summary>
        [DocumentAsJson("Recommended", "3")]
        int SizeZ;


        /// <summary>
        /// <!--<jsonalias>cposition</jsonalias>-->
        /// The controller position of the multiblock. This is the primary placed location of the multiblock.
        /// </summary>
        [DocumentAsJson("Recommended", "(1, 0, 1)")]
        Vec3i ControllerPositionRel;

        /// <summary>
        /// The type of the multiblock. Usually monolithic.
        /// </summary>
        string type;
               
        public BlockBehaviorMultiblock(Block block) : base(block) { }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);

            SizeX = properties["sizex"].AsInt(3);
            SizeY = properties["sizey"].AsInt(3);
            SizeZ = properties["sizez"].AsInt(3);
            type = properties["type"].AsString("monolithic");
            ControllerPositionRel = properties["cposition"].AsObject<Vec3i>(new Vec3i(1, 0, 1));
        }

        public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling, ref string failureCode)
        {
            bool blocked = false;

            IterateOverEach(blockSel.Position, (mpos) =>
            {
                if (mpos == blockSel.Position) return true;

                Block mblock = world.BlockAccessor.GetBlock(mpos);
                if (!mblock.IsReplacableBy(block))
                {
                    blocked = true;
                    return false;
                }

                return true;
            });

            if (blocked)
            {
                handling = EnumHandling.PreventDefault;
                failureCode = "notenoughspace";
                return false;
            }

            return true;
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
        {
            handling = EnumHandling.PassThrough;

            IterateOverEach(pos, (mpos) =>
            {
                if (mpos == pos) return true;

                int dx = mpos.X - pos.X;
                int dy = mpos.Y - pos.Y;
                int dz = mpos.Z - pos.Z;

                string sdx = (dx < 0 ? "n" : (dx > 0 ? "p" : "")) + Math.Abs(dx);
                string sdy = (dy < 0 ? "n" : (dy > 0 ? "p" : "")) + Math.Abs(dy);
                string sdz = (dz < 0 ? "n" : (dz > 0 ? "p" : "")) + Math.Abs(dz);

                AssetLocation loc = new AssetLocation("multiblock-" + type + "-" + sdx + "-" + sdy + "-" + sdz);
                Block block = world.GetBlock(loc);

                if (block == null) throw new IndexOutOfRangeException("Multiblocks are currently limited to 5x5x5 with the controller being in the middle of it, yours likely exceeds the limit because I could not find block with code " + loc.Path);

                world.BlockAccessor.SetBlock(block.Id, mpos);
                return true;
            });
        }


        public void IterateOverEach(BlockPos controllerPos, ActionConsumable<BlockPos> onBlock)
        {
            int x = controllerPos.X - ControllerPositionRel.X;
            int y = controllerPos.Y - ControllerPositionRel.Y;
            int z = controllerPos.Z - ControllerPositionRel.Z;
            BlockPos tmpPos = new BlockPos(controllerPos.dimension);

            for (int dx = 0; dx < SizeX; dx++)
            {
                for (int dy = 0; dy < SizeY; dy++)
                {
                    for (int dz = 0; dz < SizeZ; dz++)
                    {
                        tmpPos.Set(x + dx, y + dy, z + dz);
                        if (!onBlock(tmpPos)) return;
                    }
                }
            }
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
        {
            IterateOverEach(pos, (mpos) =>
            {
                if (mpos == pos) return true;

                Block mblock = world.BlockAccessor.GetBlock(mpos);
                if (mblock is BlockMultiblock)
                {
                    world.BlockAccessor.SetBlock(0, mpos);
                }

                return true;
            });

            // This removes the block breaking decal for trunks
            world.BlockAccessor.MarkBlockModified(pos);
        }

    }
}
