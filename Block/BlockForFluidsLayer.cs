using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace Vintagestory.GameContent
{
    public class BlockForFluidsLayer : Block
    {

        public override bool ForFluidsLayer { get { return true; } }


        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            bool result = true;
            bool preventDefault = false;

            foreach (BlockBehavior behavior in BlockBehaviors)
            {
                EnumHandling handled = EnumHandling.PassThrough;
                bool behaviorResult = behavior.DoPlaceBlock(world, byPlayer, blockSel, byItemStack, ref handled);
                if (handled != EnumHandling.PassThrough)
                {
                    result &= behaviorResult;
                    preventDefault = true;
                }
                if (handled == EnumHandling.PreventSubsequent) return result;
            }
            if (preventDefault) return result;

            world.BlockAccessor.SetBlock(BlockId, blockSel.Position, BlockLayersAccess.Fluid);
            // We do not call the base.DoPlaceBlock() method because we want to place this to the liquids layer, not the solid blocks layer

            return true;
        }
    }
}
