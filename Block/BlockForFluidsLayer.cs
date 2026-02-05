using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class BlockForFluidsLayer : Block, ICoolingMedium
    {
        public float InsideDamage;
        public EnumDamageType DamageType;

        public override bool ForFluidsLayer { get { return true; } }

        protected float coolingMediumTemperature;

        // Default implementation for sub-classes which implement IBlockFlowing; otherwise unused (e.g. not used by Lake Ice)
        public bool IsStill { get { return PushVector == null; } }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            InsideDamage = Attributes?["insideDamage"].AsFloat(0) ?? 0;
            DamageType = (EnumDamageType)Enum.Parse(typeof(EnumDamageType), Attributes?["damageType"].AsString("Fire") ?? "Fire");

            coolingMediumTemperature = Attributes["coolingMediumTemperature"].AsInt(GlobalConstants.CollectibleDefaultTemperature);
        }

        public override void OnEntityInside(IWorldAccessor world, Entity entity, BlockPos pos)
        {
            base.OnEntityInside(world, entity, pos);

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


        public virtual void CoolNow(ItemSlot slot, Vec3d pos, float dt, bool playSizzle = true)
        {
            CollectibleBehaviorQuenchable.CoolToTemperature(api.World, slot, pos, dt, coolingMediumTemperature, playSizzle);
        }

        public virtual bool CanCool(ItemSlot slot, Vec3d pos) => true;


        // Default implementation for sub-classes which implement IBlockFlowing; otherwise unused (e.g. not used by Lake Ice)
        public virtual FastVec3f GetPushVector(BlockPos pos)
        {
            return new FastVec3f(PushVector);
        }

        // Default implementation for sub-classes which implement IBlockFlowing; otherwise unused (e.g. not used by Lake Ice)
        public virtual float FlowRate(BlockPos pos)
        {
            return 1f;
        }
    }
}
