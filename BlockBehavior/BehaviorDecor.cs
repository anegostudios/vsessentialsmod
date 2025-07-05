using System;
using System.Xml.Linq;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Allows this block to be placed on the side of another block, as a decorational shape/texture. Uses the code "decor".
    /// </summary>
    /// <example>
    /// <code lang="json">
    ///"behaviors": [
	///	{
	///		"name": "Decor",
	///		"properties": {
	///			"sides": [ "north", "east", "south", "west", "up", "down" ],
	///			"notFullFace": true,
	///			"thickness": 0.0
	///		}
	///	}
	///],
    /// </code></example>
    [AddDocumentationProperty("Sides", "A list of sides that this decor block can be placed on.", "System.String[]", "Required", "")]
    [AddDocumentationProperty("DrawIfCulled", "If true, do not cull even if parent face was culled (used e.g. for medium carpet, which stick out beyond the parent face)", "System.Boolean", "Optional", "False")]
    [AddDocumentationProperty("AlternateZOffset", "If true, alternates z-offset vertexflag by 1 in odd/even XZ positions to reduce z-fighting (used e.g. for medium carpets overlaying neighbours)", "System.Boolean", "Optional", "False")]
    [AddDocumentationProperty("NotFullFace", "If true, this decor is NOT (at least) a full opaque face so that the parent block face still needs to be drawn", "System.Boolean", "Optional", "False")]
    [AddDocumentationProperty("Removable", "If true, this decor is removable using the players hands, without breaking the parent block", "System.Boolean", "Optional", "False")]
    [AddDocumentationProperty("Thickness", "The thickness of this decor block. Used to adjust selection box of the parent block.", "System.Single", "Optional", "0.03125")]
    [DocumentAsJson]
    public class BlockBehaviorDecor : BlockBehavior
    {
        BlockFacing[] sides;

        /// <summary>
        /// If true, this decor supplies its own different models for NSEWUD placement, if false the code will auto-rotate the model.
        /// </summary>
        [DocumentAsJson("Optional", "False")]
        bool sidedVariants;

        /// <summary>
        /// If true, this decor will automatically pick a variant based on rotation.
        /// </summary>
        [DocumentAsJson("Optional", "False")]
        bool nwOrientable;

        public BlockBehaviorDecor(Block block) : base(block)
        {
            block.decorBehaviorFlags = DecorFlags.IsDecor;
        }

        public override void Initialize(JsonObject properties)
        {
            string[] sidenames = properties["sides"].AsArray<string>(Array.Empty<string>());
            sides = new BlockFacing[sidenames.Length];
            for (int i = 0; i < sidenames.Length; i++)
            {
                if (sidenames[i] == null) continue;
                sides[i] = BlockFacing.FromFirstLetter(sidenames[i][0]);
            }

            sidedVariants = properties["sidedVariants"].AsBool();
            nwOrientable = properties["nwOrientable"].AsBool();
            if (properties["drawIfCulled"].AsBool(false)) block.decorBehaviorFlags |= DecorFlags.DrawIfCulled;
            if (properties["alternateZOffset"].AsBool(false)) block.decorBehaviorFlags |= DecorFlags.AlternateZOffset;
            if (properties["notFullFace"].AsBool(false)) block.decorBehaviorFlags |= DecorFlags.NotFullFace;
            if (properties["removable"].AsBool(false)) block.decorBehaviorFlags |= DecorFlags.Removable;
            if (sidedVariants) block.decorBehaviorFlags |= DecorFlags.HasSidedVariants;
            block.DecorThickness = properties["thickness"].AsFloat(0.03125f);

            base.Initialize(properties);
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref EnumHandling handling, ref string failureCode)
        {
            handling = EnumHandling.PreventDefault;
            
            for (int i = 0; i < sides.Length; i++)
            {
                if (sides[i] == blockSel.Face)
                {
                    BlockPos pos = blockSel.Position.AddCopy(blockSel.Face.Opposite);

                    Block blockToPlace;
                    if (sidedVariants)
                    {
                        blockToPlace = world.BlockAccessor.GetBlock(block.CodeWithParts(blockSel.Face.Opposite.Code));
                        if (blockToPlace == null)
                        {
                            failureCode = "decorvariantnotfound";
                            return false;
                        }
                    }
                    else if (nwOrientable)
                    {
                        BlockFacing suggested = Block.SuggestedHVOrientation(byPlayer, blockSel)[0];
                        string code = suggested.Axis == EnumAxis.X ? "we" : "ns";
                        blockToPlace = world.BlockAccessor.GetBlock(block.CodeWithParts(code));
                        if (blockToPlace == null)
                        {
                            failureCode = "decorvariantnotfound";
                            return false;
                        }
                    }
                    else
                    {
                        blockToPlace = this.block;
                    }

                    Block attachingBlock = world.BlockAccessor.GetBlock(pos);

                    var iad = attachingBlock.GetInterface<IAcceptsDecor>(world, pos);
                    if (iad != null && iad.CanAccept(blockToPlace))
                    {
                        if (byPlayer.WorldData.CurrentGameMode == EnumGameMode.Survival)
                        {
                            var decorId = iad.GetDecor(blockSel.Face);
                            if (decorId > 0)
                            {
                                var decor = world.BlockAccessor.GetBlock(decorId);
                                var itemStack = new ItemStack(decor.Id, decor.ItemClass, 1, new TreeAttribute(), world);
                                world.SpawnItemEntity(itemStack, pos.AddCopy(blockSel.Face).ToVec3d());
                            }
                        }
                        iad.SetDecor(blockToPlace, blockSel.Face);
                        return true;
                    }

                    var mat = attachingBlock.GetBlockMaterial(world.BlockAccessor, pos);
                    if (!attachingBlock.CanAttachBlockAt(world.BlockAccessor, blockToPlace, pos, blockSel.Face) || mat == EnumBlockMaterial.Snow || mat == EnumBlockMaterial.Ice)
                    {
                        failureCode = "decorrequiressolid";
                        return false;
                    }

                    DecorBits decorPosition = new DecorBits(blockSel.Face);
                    Block decorBlock = world.BlockAccessor.GetDecor(pos, decorPosition);
                    if (world.BlockAccessor.SetDecor(blockToPlace, pos, decorPosition))
                    {
                        if (byPlayer.WorldData.CurrentGameMode == EnumGameMode.Survival && decorBlock != null && (decorBlock.decorBehaviorFlags & DecorFlags.Removable) != 0)
                        {
                            ItemStack itemStack = decorBlock.OnPickBlock(world, pos);
                            world.SpawnItemEntity(itemStack, pos.AddCopy(blockSel.Face).ToVec3d());
                        }
                        return true;
                    }
                    
                    failureCode = "existingdecorinplace";
                    return false;
                }
            }

            failureCode = "cannotplacedecorhere";
            return false;
        }


        public override AssetLocation GetRotatedBlockCode(int angle, ref EnumHandling handled)
        {
            if (nwOrientable)
            {
                handled = EnumHandling.PreventDefault;

                string[] angles = { "ns", "we" };
                int index = angle / 90;
                if (block.LastCodePart() == "we") index++;
                return block.CodeWithParts(angles[index % 2]);
            }
            return base.GetRotatedBlockCode(angle, ref handled);
        }
    }
}
