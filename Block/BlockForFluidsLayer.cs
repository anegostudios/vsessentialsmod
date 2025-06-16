using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class BlockForFluidsLayer : Block
    {
        public float InsideDamage;
        public EnumDamageType DamageType;

        public override bool ForFluidsLayer { get { return true; } }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            InsideDamage = Attributes?["insideDamage"].AsFloat(0) ?? 0;
            DamageType = (EnumDamageType)Enum.Parse(typeof(EnumDamageType), Attributes?["damageType"].AsString("Fire") ?? "Fire");
        }

        public override void OnEntityInside(IWorldAccessor world, Entity entity, BlockPos pos)
        {
            if (InsideDamage > 0 && world.Side == EnumAppSide.Server)
            {
                entity.ReceiveDamage(new DamageSource() { Type = DamageType, Source = EnumDamageSource.Block, SourceBlock = this, SourcePos = pos.ToVec3d() }, InsideDamage);
            }
        }


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
