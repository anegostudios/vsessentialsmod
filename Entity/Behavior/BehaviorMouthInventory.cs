using System.Collections.Generic;
using System.IO;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Adds one-slot inventory and displays the stored item on the entity.
    /// <br/>Uses the "mouthinventory" code
    /// </summary>
    /// <example><code lang="json">
    ///"behaviors": [
    /// {
    ///     "code": "mouthinventory",
    ///     "acceptStacks": [
    ///         { "type": "item", "code": "honeycomb" },
    ///         { "type": "item", "code": "fruit-blueberry" },
    ///         { "type": "item", "code": "fruit-cranberry" },
    ///         { "type": "item", "code": "fruit-redcurrant" },
    ///         { "type": "item", "code": "fruit-whitecurrant" },
    ///         { "type": "item", "code": "fruit-blackcurrant" }
    ///     ]
    /// },
    ///],
    /// </code></example>
    [DocumentAsJson]
    [AddDocumentationProperty("acceptStacks", "The items the inventory can hold", "Vintagestory.API.Common.JsonItemStack[]", "Required", "", false)]
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

        public override void OnReceivedServerPacket(int packetId, SerializedData packetData, ref EnumHandling handled)
        {
            if (packetId == 1235)
            {
                var tree = new TreeAttribute();

                var reader = packetData.AsBinaryReader();

                tree.FromBytes(ref reader);

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

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            if (entity.World.Side == EnumAppSide.Server)
            {
                mouthInv.DropAll(entity.Pos.XYZ);
            }

            base.OnEntityDeath(damageSourceForDeath);
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (entity.World.Side == EnumAppSide.Server && entity.World.Rand.NextDouble() < 0.25)
            {
                mouthInv.DropAll(entity.Pos.XYZ);

                PickupCoolDownUntilMs = entity.World.ElapsedMilliseconds + 10000;
            }

            base.OnEntityReceiveDamage(damageSource, ref damage);
        }

        public override string PropertyName()
        {
            return "mouthslot";
        }
    }

}
