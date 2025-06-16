using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

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

    /// <summary>
    /// Forwards interaction events to the block entity if it implements IInteractable/ILongInteractable, or the first block entity behavior that implements IInteractable/ILongInteractable
    /// </summary>
    public class BlockBehaviorBlockEntityInteract : BlockBehavior
    {
        public BlockBehaviorBlockEntityInteract(Block block) : base(block)
        {
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            handling = EnumHandling.PassThrough;
            IInteractable ii = getInteractable(world, blockSel.Position);
            if (ii != null) return ii.OnBlockInteractStart(world, byPlayer, blockSel, ref handling);

            return false;
        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            handling = EnumHandling.PassThrough;
            ILongInteractable ii = getInteractable(world, blockSel.Position) as ILongInteractable;
            if (ii != null) return ii.OnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, ref handling);

            return false;
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            handling = EnumHandling.PassThrough;
            ILongInteractable ii = getInteractable(world, blockSel.Position) as ILongInteractable;
            if (ii != null) return ii.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel, ref handling);

            return false;
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            handling = EnumHandling.PassThrough; 
            ILongInteractable ii = getInteractable(world, blockSel.Position) as ILongInteractable;
            if (ii != null) ii.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel, ref handling);
        }

        IInteractable getInteractable(IWorldAccessor world, BlockPos pos)
        {
            var be = world.BlockAccessor.GetBlockEntity(pos);
            IInteractable ii = be as IInteractable;
            if (ii != null) return ii;
            if (be == null) return null;

            foreach (var bh in be.Behaviors)
            {
                if (bh is IInteractable) return bh as IInteractable;
            }

            return null;

        }
    }
}

