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
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorHarvestable : EntityBehavior
    {
        protected BlockDropItemStack[] jsonDrops;

        protected InventoryGeneric inv;
        protected GuiDialogCarcassContents dlg;

        protected float dropQuantityMultiplier
        {
            get
            {
                return entity.WatchedAttributes.GetFloat("dropQuantityMultiplier", 1);
            }
            set 
            {
                entity.WatchedAttributes.SetFloat("dropQuantityMultiplier", value);
            }
        }


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
            if (tree != null)
            {
                inv.FromTreeAttributes(tree);
            }

            // Maybe fixes meat reappearing non nun full harvested animals? (reported by Its Ragnar! on discord)
            entity.World.BlockAccessor.GetChunkAtBlockPos(entity.ServerPos.XYZ.AsBlockPos)?.MarkModified();
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


        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
        {
            if (!IsHarvested || byEntity.Pos.SquareDistanceTo(entity.Pos) > 5)
            {
                return;
            }

            EntityPlayer entityplr = byEntity as EntityPlayer;
            IPlayer player = entity.World.PlayerByUid(entityplr.PlayerUID);
            player.InventoryManager.OpenInventory(inv);
            
            if (entity.World.Side == EnumAppSide.Client && dlg == null)
            {
                dlg = new GuiDialogCarcassContents(inv, entity as EntityAgent, entity.Api as ICoreClientAPI);
                if (dlg.TryOpen())
                {
                    (entity.World.Api as ICoreClientAPI).Network.SendPacketClient(inv.Open(player));
                }
                
                dlg.OnClosed += () => dlg = null;
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
            if (packetid == 1012)
            {
                player.InventoryManager.OpenInventory(inv);
            }
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            base.OnEntityDeath(damageSourceForDeath);

            DamageSource dmgSource = entity.DespawnReason.damageSourceForDeath;

            if (dmgSource != null && !(dmgSource.SourceEntity is EntityPlayer))
            {
                dropQuantityMultiplier *= 0.5f;
            }
            if (dmgSource != null && dmgSource.Source == EnumDamageSource.Fall)
            {
                dropQuantityMultiplier *= 0.5f;
            }
        }


        public void SetHarvested(IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            if (entity.World.Side == EnumAppSide.Client || entity.WatchedAttributes.GetBool("harvested", false)) return;

           // entity.World.Logger.VerboseDebug("setharvested begin");

            entity.WatchedAttributes.SetBool("harvested", true);

            List<ItemStack> todrop = new List<ItemStack>();
            

            for (int i = 0; i < jsonDrops.Length; i++)
            {
                if (jsonDrops[i].Tool != null && (byPlayer == null || jsonDrops[i].Tool != byPlayer.InventoryManager.ActiveTool)) continue;

                jsonDrops[i].Resolve(entity.World, "BehaviorHarvestable");

                ItemStack stack = jsonDrops[i].GetNextItemStack(this.dropQuantityMultiplier * dropQuantityMultiplier);
                if (stack == null) continue;

                todrop.Add(stack);
                if (jsonDrops[i].LastDrop) break;
            }

           // entity.World.Logger.VerboseDebug("setharvested drops resolved");

            ItemStack[] resolvedDrops = todrop.ToArray();

            TreeAttribute tree = new TreeAttribute();
            for (int i = 0; i < resolvedDrops.Length; i++)
            {
                inv[i].Itemstack = resolvedDrops[i];
            }

            inv.ToTreeAttributes(tree);
            entity.WatchedAttributes["harvestableInv"] = tree;
            entity.WatchedAttributes.MarkPathDirty("harvestableInv");
            entity.WatchedAttributes.MarkPathDirty("harvested");

            //entity.World.Logger.VerboseDebug("setharvested done");
        }


        WorldInteraction[] interactions = null;

        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled)
        {
            interactions = ObjectCacheUtil.GetOrCreate(world.Api, "harvestableEntityInteractions", () =>
            {
                List<ItemStack> knifeStacklist = new List<ItemStack>();

                foreach (Item item in world.Items)
                {
                    if (item.Code == null) continue;

                    if (item.Tool == EnumTool.Knife)
                    {
                        knifeStacklist.Add(new ItemStack(item));
                    }
                }

                return new WorldInteraction[] {
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-creature-harvest",
                        MouseButton = EnumMouseButton.Right,
                        HotKeyCode = "sneak",
                        Itemstacks = knifeStacklist.ToArray()
                    }
                };
            });

            return !entity.Alive && !IsHarvested ? interactions : null;
        }


        public override string PropertyName()
        {
            return "harvestable";
        }
        
    }
}
