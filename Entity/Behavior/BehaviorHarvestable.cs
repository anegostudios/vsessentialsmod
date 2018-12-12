using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorHarvestable : EntityBehavior
    {
        BlockDropItemStack[] jsonDrops;

        InventoryGeneric inv;
        GuiDialogCarcassContents dlg;

        public float HarvestDuration
        {
            get; protected set;
        }

        public bool IsHarvested
        {
            get
            {
                return entity.WatchedAttributes.GetBool("harvested", false);
            }
        }

        public EntityBehaviorHarvestable(Entity entity) : base(entity)
        {
            if (entity.World.Side == EnumAppSide.Client)
            {
                entity.WatchedAttributes.RegisterModifiedListener("harvestableInv", onDropsModified);
            }

            inv = new InventoryGeneric(4, "harvestableContents-" + entity.EntityId, entity.Api);
            TreeAttribute tree = entity.WatchedAttributes["harvestableInv"] as TreeAttribute;
            if (tree != null) inv.FromTreeAttributes(tree);
            inv.PutLocked = true;

            if (entity.World.Side == EnumAppSide.Server)
            {
                inv.OnInventoryClosed += Inv_OnInventoryClosed;
            }
        }

        private void Inv_OnInventoryClosed(IPlayer player)
        {
            if (inv.IsEmpty && entity.GetBehavior<EntityBehaviorDeadDecay>()!=null)
            {
                entity.GetBehavior<EntityBehaviorDeadDecay>().DecayNow();
            }
        }

        private void onDropsModified()
        {
            TreeAttribute tree = entity.WatchedAttributes["harvestableInv"] as TreeAttribute;
            if (tree != null) inv.FromTreeAttributes(tree);
        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            base.Initialize(properties, typeAttributes);

            if (entity.World.Side == EnumAppSide.Server)
            {
                jsonDrops = typeAttributes["drops"].AsObject<BlockDropItemStack[]>();
            }

            HarvestDuration = typeAttributes["duration"].AsFloat(5);   
        }


        public override void OnInteract(EntityAgent byEntity, IItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
        {
            if (!IsHarvested || byEntity.Pos.SquareDistanceTo(entity.Pos) > 5)
            {
                return;
            }

            EntityPlayer entityplr = byEntity as EntityPlayer;
            IPlayer player = entity.World.PlayerByUid(entityplr.PlayerUID);
            player.InventoryManager.OpenInventory(inv);

            if (entity.World.Side == EnumAppSide.Client)
            {
                dlg = new GuiDialogCarcassContents(inv, entity as EntityAgent, entity.Api as ICoreClientAPI);
                dlg.TryOpen();
            }
        }


        public override void OnReceivedClientPacket(IServerPlayer player, int packetid, byte[] data, ref EnumHandling handled)
        {
            if (packetid < 1000)
            {
                inv.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                handled = EnumHandling.PreventSubsequent;
                return;
            }
        }


        public void SetHarvested(IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            if (entity.WatchedAttributes.GetBool("harvested", false)) return;

            entity.WatchedAttributes.SetBool("harvested", true);
            entity.WatchedAttributes.MarkPathDirty("harvested");

            List<ItemStack> todrop = new List<ItemStack>();

            for (int i = 0; i < jsonDrops.Length; i++)
            {
                if (jsonDrops[i].Tool != null && (byPlayer == null || jsonDrops[i].Tool != byPlayer.InventoryManager.ActiveTool)) continue;

                jsonDrops[i].Resolve(entity.World, "BehaviorHarvestable");

                ItemStack stack = jsonDrops[i].GetNextItemStack(dropQuantityMultiplier);
                if (stack == null) continue;

                todrop.Add(stack);
                if (jsonDrops[i].LastDrop) break;
            }

            ItemStack[] resolvedDrops = todrop.ToArray();

            TreeAttribute tree = new TreeAttribute();
            for (int i = 0; i < resolvedDrops.Length; i++)
            {
                inv[i].Itemstack = resolvedDrops[i];
            }

            inv.ToTreeAttributes(tree);
            entity.WatchedAttributes["harvestableInv"] = tree;
            entity.WatchedAttributes.MarkPathDirty("harvestableInv");
        }


        public override string PropertyName()
        {
            return "harvetable";
        }
    }
}
