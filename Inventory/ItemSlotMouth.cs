using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

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

        public override bool CanTakeFrom(ItemSlot sourceSlot)
        {
            return base.CanTakeFrom(sourceSlot) && mouthable(sourceSlot);
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

        

    }
}
