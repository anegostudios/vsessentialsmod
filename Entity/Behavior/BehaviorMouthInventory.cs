using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorMouthInventory : EntityBehavior
    {
        EntityAgent entityAgent;
        InventoryGeneric mouthInv;
        public List<ItemStack> acceptStacks = new List<ItemStack>();
        public long PickupCoolDownUntilMs;

        public EntityBehaviorMouthInventory(Entity entity) : base(entity)
        {
            entityAgent = entity as EntityAgent;
            mouthInv = new InventoryGeneric(1, "mouthslot-" + entity.EntityId, entity.Api, (id, inv) => new ItemSlotMouth(this, inv));
            mouthInv.SlotModified += MouthInv_SlotModified;

            entityAgent.LeftHandItemSlot = mouthInv[0];
        }

        private void MouthInv_SlotModified(int slotid)
        {
            ITreeAttribute tree = new TreeAttribute();
            entity.WatchedAttributes["mouthInv"] = tree;
            entity.WatchedAttributes.MarkPathDirty("mouthInv");
            mouthInv.ToTreeAttributes(tree);

            if (entityAgent.Api is ICoreServerAPI sapi)
            {
                sapi.Network.BroadcastEntityPacket(entity.EntityId, 1235, SerializerUtil.ToBytes((w) => tree.ToBytes(w)));
            }
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data, ref EnumHandling handled)
        {
            if (packetid == 1235)
            {
                TreeAttribute tree = new TreeAttribute();
                SerializerUtil.FromBytes(data, (r) => tree.FromBytes(r));
                mouthInv.FromTreeAttributes(tree);
            }
        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            JsonItemStack[] stacks = typeAttributes["acceptStacks"].AsObject<JsonItemStack[]>();

            foreach (var stack in stacks)
            {
                if (stack.Resolve(entity.World, "mouth inventory accept stacks"))
                {
                    acceptStacks.Add(stack.ResolvedItemstack);
                }
            }

            ITreeAttribute tree = entity.WatchedAttributes["mouthInv"] as ITreeAttribute;
            if (tree != null) mouthInv.FromTreeAttributes(tree);
        }

        public override void OnEntityDespawn(EntityDespawnReason despawn)
        {
            base.OnEntityDespawn(despawn);
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            if (entity.World.Side == EnumAppSide.Server)
            {
                mouthInv.DropAll(entity.ServerPos.XYZ);
            }

            base.OnEntityDeath(damageSourceForDeath);
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, float damage)
        {
            if (entity.World.Side == EnumAppSide.Server && entity.World.Rand.NextDouble() < 0.25)
            {
                mouthInv.DropAll(entity.ServerPos.XYZ);

                PickupCoolDownUntilMs = entity.World.ElapsedMilliseconds + 10000;
            }

            base.OnEntityReceiveDamage(damageSource, damage);
        }

        public override string PropertyName()
        {
            return "mouthslot";
        }
    }

}
