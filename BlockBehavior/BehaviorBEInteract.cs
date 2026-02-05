using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent
{

    public interface ILongInteractable : IInteractable
    {
        void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling);
        bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling);
        bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling);
    }

    public interface IInteractable
    {
        bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling);
    }

    public interface IInteractableWithHelp : IInteractable
    {
        WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling);
    }


    /// <summary>
    /// Forwards interaction events to the block entity if it implements IInteractable/ILongInteractable, or the first block entity behavior that implements IInteractable/ILongInteractable
    /// </summary>
    public class BlockBehaviorBlockEntityInteract : BlockBehavior
    {
        public BlockBehaviorBlockEntityInteract(Block block) : base(block) { }

        public delegate bool BEBehaviorDelegate(IInteractable iint, ref EnumHandling handling);


        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            EnumHandling iihandling = handling;
            var result = walkInteractables(
                world,
                blockSel.Position,
                (IInteractable ii, ref EnumHandling iiihandling) => {
                    var result = ii.OnBlockInteractStart(world, byPlayer, blockSel, ref iiihandling);
                    if (iiihandling != EnumHandling.PassThrough) iihandling = iiihandling;
                    return result;
                },
                () => base.OnBlockInteractStart(world, byPlayer, blockSel, ref iihandling)
            );

            handling = iihandling;
            return result;
        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            EnumHandling iihandling = handling;
            var result = walkInteractables(
                world,
                blockSel.Position,
                (IInteractable ii, ref EnumHandling iiihandling) => {
                    if (ii is ILongInteractable iil)
                    {
                        var result = iil.OnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, ref iiihandling);
                        if (iiihandling != EnumHandling.PassThrough) iihandling = iiihandling;
                        return result;
                    }
                    iihandling = EnumHandling.PassThrough;
                    return true;
                },
                () => base.OnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, ref iihandling)
            );

            handling = iihandling;
            return result;
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            EnumHandling iihandling = handling;
            var result = walkInteractables(
                world,
                blockSel.Position,
                (IInteractable ii, ref EnumHandling iiihandling) => {
                    if (ii is ILongInteractable iil)
                    {
                        var result = iil.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel, ref iiihandling);
                        if (iiihandling != EnumHandling.PassThrough) iihandling = iiihandling;
                        return result;
                    }
                    iihandling = EnumHandling.PassThrough;
                    return true;
                },
                () => base.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel, ref iihandling)
            );

            handling = iihandling;
            return result;
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            EnumHandling iihandling = handling;
            walkInteractables(
                world,
                blockSel.Position,
                (IInteractable ii, ref EnumHandling iiihandling) => {
                    if (ii is ILongInteractable iil)
                    {
                        var result = iil.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel, ref iiihandling);
                        if (iiihandling != EnumHandling.PassThrough) iihandling = iiihandling;
                        return result;
                    }
                    iihandling = EnumHandling.PassThrough;
                    return false;
                },
                () => base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel, ref iihandling)
            );

            handling = iihandling;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel, IPlayer forPlayer, ref EnumHandling handling)
        {
            EnumHandling iihandling = handling;
            WorldInteraction[] wis = new WorldInteraction[0];

            walkInteractables(
                world,
                blockSel.Position,
                (IInteractable ii, ref EnumHandling iihandling) => {
                    if (ii is IInteractableWithHelp iil)
                    {
                        var iiwis = iil.GetPlacedBlockInteractionHelp(world, blockSel, forPlayer, ref iihandling);
                        wis = wis.Append(iiwis);
                        return false;
                    }
                    iihandling = EnumHandling.PassThrough;
                    return false;
                },
                () => base.GetPlacedBlockInteractionHelp(world, blockSel, forPlayer, ref iihandling)
            );

            handling = iihandling;
            return wis;
        }


        protected bool walkInteractables(IWorldAccessor world, BlockPos pos, BEBehaviorDelegate onBehavior, Action defaultAction)
        {
            bool executeDefault = true;
            EnumHandling iiHandling = EnumHandling.PassThrough;

            bool resultValue = false;

            var be = world.BlockAccessor.GetBlockEntity(pos);
            if (be == null) return false;
            IInteractable ii = be as IInteractable;
            if (ii != null) resultValue = onBehavior(ii, ref iiHandling);
            
            if (iiHandling == EnumHandling.PreventSubsequent) return resultValue;

            foreach (var bh in be.Behaviors)
            {
                var iiresult = false;
                iiHandling = EnumHandling.PassThrough;
                if (bh is IInteractable iibh) iiresult = onBehavior(iibh, ref iiHandling);

                if (iiHandling == EnumHandling.PreventSubsequent) return iiresult;
                if (iiHandling == EnumHandling.PreventDefault)
                {
                    executeDefault = false;
                    resultValue = iiresult;
                }
            }

            if (executeDefault) defaultAction();

            return resultValue;
        }

    }
}

