using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public interface IMultiBlockColSelBoxes
    {
        Cuboidf[] MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset);
        Cuboidf[] MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset);
    }

    public interface IMultiBlockActivate
    {
        void MBActivate(IWorldAccessor world, Caller caller, BlockSelection blockSel, ITreeAttribute activationArgs, Vec3i offset);
    }

    public interface IMultiBlockInteract
    {
        bool MBDoParticalSelection(IWorldAccessor world, BlockPos pos, Vec3i offset);
        bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset);
        bool MBOnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset);
        void MBOnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset);
        bool MBOnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason, Vec3i offset);

        ItemStack MBOnPickBlock(IWorldAccessor world, BlockPos pos, Vec3i offset);
        WorldInteraction[] MBGetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel, IPlayer forPlayer, Vec3i offset);
        BlockSounds MBGetSounds(IBlockAccessor blockAccessor, BlockSelection blockSel, ItemStack stack, Vec3i offset);
    }

    public interface IMultiBlockBlockBreaking
    {
        void MBOnBlockBroken(IWorldAccessor world, BlockPos pos, Vec3i offset, IPlayer byPlayer, float dropQuantityMultiplier = 1);
        int MBGetRandomColor(ICoreClientAPI capi, BlockPos pos, BlockFacing facing, int rndIndex, Vec3i offsetInv);
        int MBGetColorWithoutTint(ICoreClientAPI capi, BlockPos pos, Vec3i offsetInv);
        float MBOnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter, Vec3i offsetInv);
    }

    public interface IMultiBlockBlockProperties
    {
        bool MBCanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi attachmentArea, Vec3i offsetInv);
        float MBGetLiquidBarrierHeightOnSide(BlockFacing face, BlockPos pos, Vec3i offsetInv);
        int MBGetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type, Vec3i offsetInv);
        JsonObject MBGetAttributes(IBlockAccessor blockAccessor, BlockPos pos);
    }


    // Concept:
    // 3 Levels of modularity of multi block structures
    // Monolithic, Simple: The controller block takes care of the block shape, the other blocks are invisible and have full block hitboxes. Simply forwards all interaction and info events to the controller block
    // Monolithic, Configurable: The controller block takes care of the block shape and implements IMultiBlockMonolithic for custom hitboxes and interaction events. The other blocks are still invisible
    // Modular, Configurable: The controller block implements IMultiBlockModular, most events are forwarded to the controller block.
    public class BlockMultiblock : Block
    {
        public Vec3i Offset;
        public Vec3i OffsetInv;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            Offset = new Vec3i(Variant["dx"].Replace("n", "-").Replace("p", "").ToInt(), Variant["dy"].Replace("n", "-").Replace("p", "").ToInt(), Variant["dz"].Replace("n", "-").Replace("p", "").ToInt());

            OffsetInv = -Offset;
        }


        public delegate T BlockCallDelegateInterface<T, K>(K block);
        public delegate T BlockCallDelegateBlock<T>(Block block);
        public delegate void BlockCallDelegateInterface<K>(K block);
        public delegate void BlockCallDelegateBlock(Block block);

        T Handle<T, K>(IBlockAccessor ba, int x, int y, int z, BlockCallDelegateInterface<T, K> onImplementsInterface, BlockCallDelegateBlock<T> onIsMultiblock, BlockCallDelegateBlock<T> onOtherwise) where K : class
        {
            var block = ba.GetBlock(x, y, z);
            var blockInf = block as K;

            if (blockInf == null) blockInf = block.GetBehavior(typeof(K), true) as K;
            if (blockInf != null)
            {
                return onImplementsInterface(blockInf);
            }

            // This is to prevent endless recursion in situations where blocks become incorrectly arranged
            if (block is BlockMultiblock) return onIsMultiblock(block);

            return onOtherwise(block);
        }

        void Handle<K>(IBlockAccessor ba, int x, int y, int z, BlockCallDelegateInterface<K> onImplementsInterface, BlockCallDelegateBlock onIsMultiblock, BlockCallDelegateBlock onOtherwise) where K : class
        {
            var block = ba.GetBlock(x, y, z);
            var blockInf = block as K;

            if (blockInf == null) blockInf = block.GetBehavior(typeof(K), true) as K;
            if (blockInf != null)
            {
                onImplementsInterface(blockInf);
                return;
            }

            // This is to prevent endless recursion in situations where blocks become incorrectly arranged
            if (block is BlockMultiblock)
            {
                onIsMultiblock(block);
                return;
            }

            onOtherwise(block);
        }


        public override void Activate(IWorldAccessor world, Caller caller, BlockSelection blockSel, ITreeAttribute activationArgs = null)
        {
            var bsOffseted = blockSel.Clone();
            bsOffseted.Position.Add(OffsetInv);

            Handle<IMultiBlockActivate>(
                world.BlockAccessor,
                bsOffseted.Position.X, bsOffseted.Position.InternalY, bsOffseted.Position.Z,
                (inf) => inf.MBActivate(world, caller, bsOffseted, activationArgs, OffsetInv),
                (block) => base.Activate(world, caller, bsOffseted, activationArgs),
                (block) => block.Activate(world, caller, bsOffseted, activationArgs)
            );
        }

        public override BlockSounds GetSounds(IBlockAccessor ba, BlockSelection blockSel, ItemStack stack = null)
        {
            return Handle<BlockSounds, IMultiBlockInteract>(
                ba,
                blockSel.Position.X + OffsetInv.X, blockSel.Position.InternalY + OffsetInv.Y, blockSel.Position.Z + OffsetInv.Z,
                (inf) => inf.MBGetSounds(ba, blockSel, stack, OffsetInv),
                (block) => base.GetSounds(ba, blockSel.AddPosCopy(OffsetInv), stack),
                (block) => block.GetSounds(ba, blockSel.AddPosCopy(OffsetInv), stack)
            );
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor ba, BlockPos pos)
        {
            return Handle<Cuboidf[], IMultiBlockColSelBoxes>(
               ba,
               pos.X + OffsetInv.X, pos.InternalY + OffsetInv.Y, pos.Z + OffsetInv.Z,
               (inf) => inf.MBGetSelectionBoxes(ba, pos, OffsetInv),
               (block) => new Cuboidf[] { Cuboidf.Default() },
               (block) => block.Id == 0 ? new Cuboidf[] { Cuboidf.Default() } : block.GetSelectionBoxes(ba, pos.AddCopy(OffsetInv))
           );
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor ba, BlockPos pos)
        {
            return Handle<Cuboidf[], IMultiBlockColSelBoxes>(
               ba,
               pos.X + OffsetInv.X, pos.InternalY + OffsetInv.Y, pos.Z + OffsetInv.Z,
               (inf) => inf.MBGetCollisionBoxes(ba, pos, OffsetInv),
               (block) => new Cuboidf[] { Cuboidf.Default() },
               (block) => block.GetCollisionBoxes(ba, pos.AddCopy(OffsetInv))
           );
        }

        public override bool DoParticalSelection(IWorldAccessor world, BlockPos pos)
        {
            return Handle<bool, IMultiBlockInteract>(
                world.BlockAccessor,
                pos.X + OffsetInv.X, pos.InternalY + OffsetInv.Y, pos.Z + OffsetInv.Z,
                (inf) => inf.MBDoParticalSelection(world, pos, OffsetInv),
                (block) => base.DoParticalSelection(world, pos.AddCopy(OffsetInv)),
                (block) => block.DoParticalSelection(world, pos.AddCopy(OffsetInv))
            );
        }

        public override float OnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
        {
            var bsOffseted = blockSel.Clone();
            bsOffseted.Position.Add(OffsetInv);

            return Handle<float, IMultiBlockBlockBreaking>(
                  api.World.BlockAccessor,
                  bsOffseted.Position.X, bsOffseted.Position.InternalY, bsOffseted.Position.Z,
                  (inf) => inf.MBOnGettingBroken(player, blockSel, itemslot, remainingResistance, dt, counter, OffsetInv),
                  (block) => base.OnGettingBroken(player, bsOffseted, itemslot, remainingResistance, dt, counter),
                  (block) => {
                      if (api is ICoreClientAPI capi)
                      {
                          capi.World.CloneBlockDamage(blockSel.Position, blockSel.Position.AddCopy(OffsetInv));
                      }
                      return block.OnGettingBroken(player, bsOffseted, itemslot, remainingResistance, dt, counter);
                  }
              );;


        }


        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            var block = world.BlockAccessor.GetBlock(pos.AddCopy(OffsetInv));
            if (block.Id == 0)
            {
                base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
                return;
            }

            var blockInf = block as IMultiBlockBlockBreaking;
            if (blockInf == null) blockInf = block.GetBehavior(typeof(IMultiBlockBlockBreaking), true) as IMultiBlockBlockBreaking;

            if (blockInf != null)
            {
                blockInf.MBOnBlockBroken(world, pos, OffsetInv, byPlayer);
                return;
            }

            // Prevent Stack overflow
            if (block is BlockMultiblock) return;

            block.OnBlockBroken(world, pos.AddCopy(OffsetInv), byPlayer, dropQuantityMultiplier);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            return Handle<ItemStack, IMultiBlockInteract>(
                world.BlockAccessor,
                pos.X + OffsetInv.X, pos.Y + OffsetInv.Y, pos.Z + OffsetInv.Z,
                (inf) => inf.MBOnPickBlock(world, pos, OffsetInv),
                (block) => base.OnPickBlock(world, pos.AddCopy(OffsetInv)),
                (block) => block.OnPickBlock(world, pos.AddCopy(OffsetInv))
            );
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var bsOffseted = blockSel.Clone();
            bsOffseted.Position.Add(OffsetInv);

            return Handle<bool, IMultiBlockInteract>(
                world.BlockAccessor,
                bsOffseted.Position.X, bsOffseted.Position.InternalY, bsOffseted.Position.Z,
                (inf) => inf.MBOnBlockInteractStart(world, byPlayer, blockSel, OffsetInv),
                (block) => base.OnBlockInteractStart(world, byPlayer, bsOffseted),
                (block) => block.OnBlockInteractStart(world, byPlayer, bsOffseted)
            );
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var bsOffseted = blockSel.Clone();
            bsOffseted.Position.Add(OffsetInv);

            return Handle<bool, IMultiBlockInteract>(
                world.BlockAccessor,
                bsOffseted.Position.X, bsOffseted.Position.InternalY, bsOffseted.Position.Z,
                (inf) => inf.MBOnBlockInteractStep(secondsUsed, world, byPlayer, blockSel, OffsetInv),
                (block) => base.OnBlockInteractStep(secondsUsed, world, byPlayer, bsOffseted),
                (block) => block.OnBlockInteractStep(secondsUsed, world, byPlayer, bsOffseted)
            );
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var bsOffseted = blockSel.Clone();
            bsOffseted.Position.Add(OffsetInv);

            Handle<IMultiBlockInteract>(
                world.BlockAccessor,
                bsOffseted.Position.X, bsOffseted.Position.InternalY, bsOffseted.Position.Z,
                (inf) => inf.MBOnBlockInteractStop(secondsUsed, world, byPlayer, blockSel, OffsetInv),
                (block) => base.OnBlockInteractStop(secondsUsed, world, byPlayer, bsOffseted),
                (block) => block.OnBlockInteractStop(secondsUsed, world, byPlayer, bsOffseted)
            );
        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason)
        {
            var bsOffseted = blockSel.Clone();
            bsOffseted.Position.Add(OffsetInv);

            return Handle<bool, IMultiBlockInteract>(
                world.BlockAccessor,
                bsOffseted.Position.X, bsOffseted.Position.InternalY, bsOffseted.Position.Z,
                (inf) => inf.MBOnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, cancelReason, OffsetInv),
                (block) => base.OnBlockInteractCancel(secondsUsed, world, byPlayer, bsOffseted, cancelReason),
                (block) => block.OnBlockInteractCancel(secondsUsed, world, byPlayer, bsOffseted, cancelReason)
            );
        }


        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel, IPlayer forPlayer)
        {
            var bsOffseted = blockSel.Clone();
            bsOffseted.Position.Add(OffsetInv);

            return Handle<WorldInteraction[], IMultiBlockInteract>(
                world.BlockAccessor,
                bsOffseted.Position.X, bsOffseted.Position.InternalY, bsOffseted.Position.Z,
                (inf) => inf.MBGetPlacedBlockInteractionHelp(world, blockSel, forPlayer, OffsetInv),
                (block) => base.GetPlacedBlockInteractionHelp(world, bsOffseted, forPlayer),
                (block) => block.GetPlacedBlockInteractionHelp(world, bsOffseted, forPlayer)
            );
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            BlockPos mainPos = pos.AddCopy(OffsetInv);
            var block = world.BlockAccessor.GetBlock(mainPos);

            // Prevent Stack overflow
            if (block is BlockMultiblock) return "";

            return block.GetPlacedBlockInfo(world, mainPos, forPlayer);
        }

        public override int GetRandomColor(ICoreClientAPI capi, BlockPos pos, BlockFacing facing, int rndIndex = -1)
        {
            var world = capi.World;
            return Handle<int, IMultiBlockBlockBreaking>(
                world.BlockAccessor,
                pos.X + OffsetInv.X, pos.InternalY + OffsetInv.Y, pos.Z + OffsetInv.Z,
                (inf) => inf.MBGetRandomColor(capi, pos, facing, rndIndex, OffsetInv),
                (block) => base.GetRandomColor(capi, pos, facing, rndIndex),
                (block) => block.GetRandomColor(capi, pos, facing, rndIndex)
            );
        }

        public override int GetColorWithoutTint(ICoreClientAPI capi, BlockPos pos)
        {
            var world = capi.World;
            return Handle<int, IMultiBlockBlockBreaking>(
                world.BlockAccessor,
                pos.X + OffsetInv.X, pos.InternalY + OffsetInv.Y, pos.Z + OffsetInv.Z,
                (inf) => inf.MBGetColorWithoutTint(capi, pos, OffsetInv),
                (block) => base.GetColorWithoutTint(capi, pos),
                (block) => block.GetColorWithoutTint(capi, pos)
            );
        }


        public override bool CanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi attachmentArea = null)
        {
            return Handle<bool, IMultiBlockBlockProperties>(
                blockAccessor,
                pos.X + OffsetInv.X, pos.InternalY + OffsetInv.Y, pos.Z + OffsetInv.Z,
                (inf) => inf.MBCanAttachBlockAt(blockAccessor, block, pos, blockFace, attachmentArea, OffsetInv),
                (nblock) => base.CanAttachBlockAt(blockAccessor, block, pos, blockFace, attachmentArea),
                (nblock) => nblock.CanAttachBlockAt(blockAccessor, block, pos, blockFace, attachmentArea)
            );
        }


        public override JsonObject GetAttributes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return Handle<JsonObject, IMultiBlockBlockProperties>(
                blockAccessor,
                pos.X + OffsetInv.X, pos.InternalY + OffsetInv.Y, pos.Z + OffsetInv.Z,
                (inf) => inf.MBGetAttributes(blockAccessor, pos),
                (nblock) => base.GetAttributes(blockAccessor, pos),
                (nblock) => nblock.GetAttributes(blockAccessor, pos)
            );
        }


        public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type)
        {
            var blockAccessor = api.World.BlockAccessor;

            return Handle<int, IMultiBlockBlockProperties>(
                blockAccessor,
                pos.X + OffsetInv.X, pos.InternalY + OffsetInv.Y, pos.Z + OffsetInv.Z,
                (inf) => inf.MBGetRetention(pos, facing, type, OffsetInv),
                (nblock) => base.GetRetention(pos, facing, EnumRetentionType.Heat),
                (nblock) => nblock.GetRetention(pos, facing, EnumRetentionType.Heat)
            );
        }

        public override float GetLiquidBarrierHeightOnSide(BlockFacing face, BlockPos pos)
        {
            var blockAccessor = api.World.BlockAccessor;

            return Handle<float, IMultiBlockBlockProperties>(
                blockAccessor,
                pos.X + OffsetInv.X, pos.InternalY + OffsetInv.Y, pos.Z + OffsetInv.Z,
                (inf) => inf.MBGetLiquidBarrierHeightOnSide(face, pos, OffsetInv),
                (nblock) => base.GetLiquidBarrierHeightOnSide(face, pos),
                (nblock) => nblock.GetLiquidBarrierHeightOnSide(face, pos)
            );
        }

        public override T GetBlockEntity<T>(BlockPos position)
        {
            var block = api.World.BlockAccessor.GetBlock(position.AddCopy(OffsetInv));

            // Prevent endless recursion stack overflow, should we ever end up in a corrupted world situation
            if (block is BlockMultiblock) return base.GetBlockEntity<T>(position);

            return block.GetBlockEntity<T>(position.AddCopy(OffsetInv));
        }

        public override T GetBlockEntity<T>(BlockSelection blockSel)
        {
            var block = api.World.BlockAccessor.GetBlock(blockSel.Position.AddCopy(OffsetInv));

            // Prevent endless recursion stack overflow, should we ever end up in a corrupted world situation
            if (block is BlockMultiblock) return base.GetBlockEntity<T>(blockSel);

            var bs = blockSel.Clone();
            bs.Position.Add(OffsetInv);

            return block.GetBlockEntity<T>(bs);
        }

        public override AssetLocation GetRotatedBlockCode(int angle)
        {
            int angleIndex = ((angle / 90) % 4 + 4) % 4;
            if (angleIndex == 0) return Code;

            Vec3i offsetNew;
            switch (angleIndex)
            {
                case 1:
                    offsetNew = new Vec3i(-Offset.Z, Offset.Y, Offset.X);
                    break;
                case 2:
                    offsetNew = new Vec3i(-Offset.X, Offset.Y, -Offset.Z);
                    break;
                case 3:
                    offsetNew = new Vec3i(Offset.Z, Offset.Y, -Offset.X);
                    break;
                default:
                    offsetNew = null;
                    break;
            }

            return new AssetLocation(Code.Domain, "multiblock-monolithic" + OffsetToString(offsetNew.X) + OffsetToString(offsetNew.Y) + OffsetToString(offsetNew.Z));
        }

        private string OffsetToString(int x)
        {
            if (x == 0) return "-0";
            if (x < 0) return "-n" + (-x).ToString();
            return "-p" + x.ToString();
        }
    }
}
