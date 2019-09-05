using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
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
                if (entity.WatchedAttributes.HasAttribute("deathReason"))
                {
                    EnumDamageSource dmgSource = (EnumDamageSource)entity.WatchedAttributes.GetInt("deathReason");

                    if (dmgSource == EnumDamageSource.Fall)
                    {
                        return 0.5f;
                    }
                }

                string deathByEntityLangCode = entity.WatchedAttributes.GetString("deathByEntity");

                if (deathByEntityLangCode != null && !entity.WatchedAttributes.HasAttribute("deathByPlayer"))
                {
                    return 0.4f;
                }

                return 1f;
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
                if (!IsHarvested)
                {
                    entity.WatchedAttributes.MarkPathDirty("harvested");
                } else
                {
                    player.InventoryManager.OpenInventory(inv);
                }
                
            }
        }


        public void SetHarvested(IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            //entity.World.Logger.Debug("setharvested begin " + entity.World.Side);

            if (entity.WatchedAttributes.GetBool("harvested", false)) return;

            entity.WatchedAttributes.SetBool("harvested", true);

            if (entity.World.Side == EnumAppSide.Client) return;


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

            //entity.World.Logger.Debug("setharvested drops resolved");

            ItemStack[] resolvedDrops = todrop.ToArray();

            TreeAttribute tree = new TreeAttribute();
            for (int i = 0; i < resolvedDrops.Length; i++)
            {
                inv[i].Itemstack = resolvedDrops[i];

                //entity.World.Logger.Debug("drop {0} is {1}", i, resolvedDrops[i]?.GetName());
            }

            inv.ToTreeAttributes(tree);
            entity.WatchedAttributes["harvestableInv"] = tree;
            entity.WatchedAttributes.MarkPathDirty("harvestableInv");
            entity.WatchedAttributes.MarkPathDirty("harvested");

            //entity.World.Logger.Debug("setharvested done");
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


        public override void GetInfoText(StringBuilder infotext)
        {
            if (!entity.Alive)
            {
                if (entity.WatchedAttributes.HasAttribute("deathReason"))
                {
                    EnumDamageSource dmgSource = (EnumDamageSource)entity.WatchedAttributes.GetInt("deathReason");

                    if (dmgSource == EnumDamageSource.Fall)
                    {
                        infotext.AppendLine(Lang.Get("Looks crushed. Won't be able to harvest as much from this carcass."));
                    }
                }
                
                string deathByEntityLangCode = entity.WatchedAttributes.GetString("deathByEntity");

                if (deathByEntityLangCode != null && !entity.WatchedAttributes.HasAttribute("deathByPlayer")) {
                    infotext.AppendLine(Lang.Get("Looks eaten by another creature. Won't be able to harvest as much from this carcass."));
                }
            }

            base.GetInfoText(infotext);
        }


        

        public override string PropertyName()
        {
            return "harvestable";
        }
        
    }
}
