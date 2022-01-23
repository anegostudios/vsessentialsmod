using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public interface IMultiBlockMonolithicSmall
    {
        Cuboidf[] MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset);
        Cuboidf[] MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset);
    }

    public interface IMultiBlockMonolithic : IMultiBlockMonolithicSmall
    {
        void MBDoParticalSelection(IWorldAccessor world, BlockPos pos, Vec3i offset);
        bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset);
        bool MBOnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset);
        void MBOnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset);
        bool MBOnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason, Vec3i offset);

        ItemStack MBOnPickBlock(IWorldAccessor world, BlockPos pos, Vec3i offset); 
        WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel, IPlayer forPlayer, Vec3i offset);
        BlockSounds GetSounds(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack);
    }


    public interface IMultiBlockModular : IMultiBlockMonolithic
    {
        void OnBlockBroken(IWorldAccessor world, BlockPos pos, Vec3i offset, IPlayer byPlayer, float dropQuantityMultiplier = 1);

        // not implemented yet
        void OnTesselation(ITerrainMeshPool mesher);
    }


    // Concept:
    // 3 Levels of modularity of multi block structures
    // Monolithic, Simple: The controller block takes care of the block shape, the other blocks are invisible and have full block hitboxes. Simply forwards all interaction and info events to the controller block
    // Monolithic, Configurable: The controller block takes care of the block shape and implements IMultiBlockMonolithic for custom hitboxes and interaction events. The other blocks are still invisible
    // Modular, Configurable: The controller block implements IMultiBlockModular, all events and block shape tesselation are forwarded to the controller block. Requires every multiblock to be a block entity for custom shape tesselation.
    public class BlockMultiblock : Block
    {
        public Vec3i Offset;

        public bool Modular;

        public Vec3i OffsetInv;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            Offset = new Vec3i(Variant["dx"].Replace("n", "-").Replace("p", "").ToInt(), Variant["dy"].Replace("n", "-").Replace("p", "").ToInt(), Variant["dz"].Replace("n", "-").Replace("p", "").ToInt());
            Modular = Variant["type"] == "modular";

            OffsetInv = -Offset;
        }

        public override BlockSounds GetSounds(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
        {
            var block = blockAccessor.GetBlock(pos.X + OffsetInv.X, pos.Y + OffsetInv.Y, pos.Z + OffsetInv.Z);
            var blockm = block as IMultiBlockMonolithic;
            if (blockm != null) return blockm.GetSounds(blockAccessor, pos, stack);

            return block.GetSounds(blockAccessor, pos, stack);
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            var block = blockAccessor.GetBlock(pos.X + OffsetInv.X, pos.Y + OffsetInv.Y, pos.Z + OffsetInv.Z);
            var blockm = block as IMultiBlockMonolithicSmall;
            if (blockm != null) return blockm.MBGetSelectionBoxes(blockAccessor, pos, OffsetInv);
            
            return block.GetSelectionBoxes(blockAccessor, pos);
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            var block = blockAccessor.GetBlock(pos.X + OffsetInv.X, pos.Y + OffsetInv.Y, pos.Z + OffsetInv.Z);
            var blockm = block as IMultiBlockMonolithicSmall;
            if (blockm != null) return blockm.MBGetCollisionBoxes(blockAccessor, pos, OffsetInv);

            return block.GetCollisionBoxes(blockAccessor, pos);
        }

        public override bool DoParticalSelection(IWorldAccessor world, BlockPos pos)
        {
            var block = world.BlockAccessor.GetBlock(pos.X + OffsetInv.X, pos.Y + OffsetInv.Y, pos.Z + OffsetInv.Z) as IMultiBlockMonolithic;
            if (block != null) block.MBDoParticalSelection(world, pos, OffsetInv);

            return true;
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            var block = world.BlockAccessor.GetBlock(pos.AddCopy(OffsetInv));
            if (block.Id == 0)
            {
                base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
                return;
            }

            if (block is IMultiBlockModular mbMono)
            {
                mbMono.OnBlockBroken(world, pos, OffsetInv, byPlayer);
                return;
            }

            block.OnBlockBroken(world, pos.AddCopy(OffsetInv), byPlayer, dropQuantityMultiplier);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            var block = world.BlockAccessor.GetBlock(pos.AddCopy(OffsetInv));
            if (block is IMultiBlockModular mbMono)
            {
                return mbMono.MBOnPickBlock(world, pos, OffsetInv);
            }

            return block.OnPickBlock(world, pos.AddCopy(OffsetInv));
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var blockSelOffsetInved = blockSel.Clone();
            blockSelOffsetInved.Position.Add(OffsetInv);
            var block = world.BlockAccessor.GetBlock(blockSelOffsetInved.Position);
            if (block is IMultiBlockModular mbMono)
            {
                return mbMono.MBOnBlockInteractStart(world, byPlayer, blockSel, OffsetInv);
            }

            return block.OnBlockInteractStart(world, byPlayer, blockSelOffsetInved);
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var blockSelOffsetInved = blockSel.Clone();
            blockSelOffsetInved.Position.Add(OffsetInv);
            var block = world.BlockAccessor.GetBlock(blockSelOffsetInved.Position);
            if (block is IMultiBlockModular mbMono)
            {
                return mbMono.MBOnBlockInteractStep(secondsUsed, world, byPlayer, blockSel, OffsetInv);
            }

            return block.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSelOffsetInved);
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var blockSelOffsetInved = blockSel.Clone();
            blockSelOffsetInved.Position.Add(OffsetInv);
            var block = world.BlockAccessor.GetBlock(blockSelOffsetInved.Position);
            if (block is IMultiBlockModular mbMono)
            {
                mbMono.MBOnBlockInteractStop(secondsUsed, world, byPlayer, blockSel, OffsetInv);
                return;
            }

            block.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSelOffsetInved);
        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason)
        {
            var blockSelOffsetInved = blockSel.Clone();
            blockSelOffsetInved.Position.Add(OffsetInv);
            var block = world.BlockAccessor.GetBlock(blockSelOffsetInved.Position);
            if (block is IMultiBlockModular mbMono)
            {
                return mbMono.MBOnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, cancelReason, OffsetInv);
            }

            return block.OnBlockInteractCancel(secondsUsed, world, byPlayer, blockSelOffsetInved, cancelReason);
        }


        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel, IPlayer forPlayer)
        {
            var blockSelOffsetInved = blockSel.Clone();
            blockSelOffsetInved.Position.Add(OffsetInv);
            var block = world.BlockAccessor.GetBlock(blockSelOffsetInved.Position);
            if (block is IMultiBlockModular mbMono)
            {
                return mbMono.GetPlacedBlockInteractionHelp(world, blockSel, forPlayer, OffsetInv);
            }

            return block.GetPlacedBlockInteractionHelp(world, blockSelOffsetInved, forPlayer);
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            var block = world.BlockAccessor.GetBlock(pos.AddCopy(OffsetInv));
            return block.GetPlacedBlockInfo(world, pos.AddCopy(OffsetInv), forPlayer);
        }
    }
}
