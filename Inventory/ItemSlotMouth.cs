using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

#nullable disable

namespace Vintagestory.GameContent
{
    public class ItemSlotMouth : ItemSlotSurvival
    {
        EntityBehaviorMouthInventory beh;
        

        public ItemSlotMouth(EntityBehaviorMouthInventory beh, InventoryGeneric inventory) : base(inventory)
        {
            this.beh = beh;
            MaxSlotStackSize = 1;
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            return base.CanTakeFrom(sourceSlot, priority) && mouthable(sourceSlot);
        }

        public override bool CanHold(ItemSlot itemstackFromSourceSlot)
        {
            return base.CanHold(itemstackFromSourceSlot) && mouthable(itemstackFromSourceSlot);
        }

        public bool mouthable(ItemSlot sourceSlot)
        {
            if (!Empty) return false;
            if (beh.PickupCoolDownUntilMs > beh.entity.World.ElapsedMilliseconds) return false;

            for (int i = 0; i < beh.acceptStacks.Count; i++)
            {
                if (beh.acceptStacks[i].Equals(beh.entity.World, sourceSlot.Itemstack, GlobalConstants.IgnoredStackAttributes))
                {
                    return true;
                }
            }

            return beh.entity.World.Rand.NextDouble() < 0.005;
        }

        public override void OnBeforeRender(ItemRenderInfo renderInfo)
        {
            var tf = renderInfo.Transform.Clone();
            if (itemstack.Collectible.Code.Path.Contains("berry")) tf.Translation.Add(-0.15f, -0.15f, 0.5f);
            else tf.Translation.Add(0f, -0.1f, -0.12f);
            renderInfo.Transform = tf;
        }
    }
}
