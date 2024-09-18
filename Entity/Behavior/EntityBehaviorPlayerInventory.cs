using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorPlayerInventory : EntityBehaviorTexturedClothing
    {
        IPlayer Player => (entity as EntityPlayer).Player;

        public override InventoryBase Inventory => Player?.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName) as InventoryBase;

        public override string InventoryClassName => "gear";

        public override string PropertyName() => "playerinventory";

        public EntityBehaviorPlayerInventory(Entity entity) : base(entity) { }

        bool slotModifiedRegistered = false;

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
            IInventory inv = Player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
            if (inv != null)
            {
                inv.SlotModified -= Inventory_SlotModified;
            }
        }

        protected override void loadInv()
        {
            // No need to load this inv
        }
        public override void storeInv()
        {
            // No need to save this inv
        }

        float accum = 0;
        public override void OnGameTick(float deltaTime)
        {
            if (!slotModifiedRegistered)
            {
                slotModifiedRegistered = true;
                IInventory inv = Player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
                if (inv != null)
                {
                    inv.SlotModified += Inventory_SlotModified;
                }
            }

            base.OnGameTick(deltaTime);

            accum += deltaTime;
            if (accum > 1)
            {
                entity.Attributes.SetBool("hasProtectiveEyeGear", Inventory != null && Inventory.FirstOrDefault((slot) => !slot.Empty && slot.Itemstack.Collectible.Attributes?.IsTrue("eyeprotective") == true) != null);
            }
        }

        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
        {
            IInventory backPackInv = Player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);

            Dictionary<string, ItemSlot> uniqueGear = new Dictionary<string, ItemSlot>();
            for (int i = 0; backPackInv != null && i < 4; i++)
            {
                ItemSlot slot = backPackInv[i];
                if (slot.Empty) continue;
                uniqueGear["" + slot.Itemstack.Class + slot.Itemstack.Collectible.Id] = slot;
            }

            foreach (var val in uniqueGear)
            {
                entityShape = addGearToShape(entityShape, val.Value, "default", shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);
            }

            base.OnTesselation(ref entityShape, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            // Execute this one frame later so that in case right after this method some other code still returns an item (e.g. BlockMeal), it is also ditched
            Api.Event.EnqueueMainThreadTask(() =>
            {
                if (entity.Properties.Server?.Attributes?.GetBool("keepContents", false) != true)
                {
                    Player.InventoryManager.OnDeath();
                }

                if (entity.Properties.Server?.Attributes?.GetBool("dropArmorOnDeath", false) == true)
                {
                    foreach (var slot in Inventory)
                    {
                        if (slot.Empty) continue;
                        if (slot.Itemstack.ItemAttributes?["protectionModifiers"].Exists == true)
                        {
                            Api.World.SpawnItemEntity(slot.Itemstack, entity.ServerPos.XYZ);
                            slot.Itemstack = null;
                            slot.MarkDirty();
                        }
                    }
                }
            }, "dropinventoryondeath");
        }
    }
}
