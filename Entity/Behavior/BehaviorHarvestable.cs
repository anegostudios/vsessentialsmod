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

        public float AnimalWeight
        {
            get
            {
                return entity.WatchedAttributes.GetFloat("animalWeight", 1);
            }
            set
            {
                entity.WatchedAttributes.SetFloat("animalWeight", value);
            }
        }

        public double LastWeightUpdateTotalHours
        {
            get
            {
                return entity.WatchedAttributes.GetDouble("lastWeightUpdateTotalHours", 1);
            }
            set
            {
                entity.WatchedAttributes.SetDouble("lastWeightUpdateTotalHours", value);
            }
        }


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

        public bool Harvestable
        {
            get
            {
                return !entity.Alive && !IsHarvested;
            }
        }


        float baseHarvestDuration;
        public float GetHarvestDuration(Entity forEntity) 
        {
            return baseHarvestDuration * forEntity.Stats.GetBlended("animalHarvestingSpeed");
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
                inv.SlotModified += Inv_SlotModified;
                inv.OnInventoryClosed += Inv_OnInventoryClosed;
            }

            harshWinters = entity.World.Config.GetString("harshWinters").ToBool(true);
        }

        bool harshWinters;
        float accum = 0;

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            accum += deltaTime;

            if (accum > 1.5f)
            {
                if (!harshWinters)
                {
                    AnimalWeight = 1;
                    return;
                }

                accum = 0;
                double totalHours = entity.World.Calendar.TotalHours;
                double startHours = LastWeightUpdateTotalHours;
                double lastEatenTotalHours = entity.WatchedAttributes.GetDouble("lastMealEatenTotalHours", -9999);

                double fourmonthsHours = 4 * entity.World.Calendar.DaysPerMonth * entity.World.Calendar.HoursPerDay;
                double oneweekHours = 7 * entity.World.Calendar.HoursPerDay;

                // Don't simulate longer than a year
                startHours = Math.Max(startHours, totalHours - entity.World.Calendar.HoursPerDay * entity.World.Calendar.DaysPerYear);
                BlockPos pos = entity.Pos.AsBlockPos;

                float weight = AnimalWeight;

                float step = 3;

                while (startHours < totalHours - 1)
                {
                    ClimateCondition conds = entity.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.ForSuppliedDateValues, startHours);
                    if (conds == null)
                    {
                        base.OnGameTick(deltaTime);
                        return;
                    }

                    // no need to simulate every single hour
                    startHours += step;

                    double mealHourDiff = startHours - lastEatenTotalHours;

                    bool ateSomeTimeAgo = mealHourDiff < fourmonthsHours;
                    bool ateRecently = mealHourDiff < oneweekHours;

                    if (conds.Temperature <= 0 && !ateSomeTimeAgo)
                    {
                        // Loose 0.1% of weight each hour = 2.4% per day
                        weight = Math.Max(0.5f, weight - step * 0.001f);
                    } else
                    {
                        weight = Math.Min(1f, weight + step * (0.001f + (ateRecently ? 0.05f : 0)));
                    }
                }

                AnimalWeight = weight;
                LastWeightUpdateTotalHours = startHours;
            }

            base.OnGameTick(deltaTime);
        }

        private void Inv_SlotModified(int slotid)
        {
            TreeAttribute tree = new TreeAttribute();
            inv.ToTreeAttributes(tree);
            entity.WatchedAttributes["harvestableInv"] = tree;
            entity.WatchedAttributes.MarkPathDirty("harvestableInv");
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

            baseHarvestDuration = typeAttributes["duration"].AsFloat(5);   
        }


        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
        {
            bool inRange = (byEntity.World.Side == EnumAppSide.Client && byEntity.Pos.SquareDistanceTo(entity.Pos) <= 5) || (byEntity.World.Side == EnumAppSide.Server && byEntity.Pos.SquareDistanceTo(entity.Pos) <= 14);

            if (!IsHarvested || !inRange)
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


            if (!entity.Attributes.GetBool("isMechanical", false))
            {
                dropQuantityMultiplier *= byPlayer.Entity.Stats.GetBlended("animalLootDropRate");
            }


            List<ItemStack> todrop = new List<ItemStack>();
            

            for (int i = 0; i < jsonDrops.Length; i++)
            {
                if (jsonDrops[i].Tool != null && (byPlayer == null || jsonDrops[i].Tool != byPlayer.InventoryManager.ActiveTool)) continue;

                jsonDrops[i].Resolve(entity.World, "BehaviorHarvestable");

                ItemStack stack = jsonDrops[i].GetNextItemStack(this.dropQuantityMultiplier * dropQuantityMultiplier);

                if (stack == null) continue;

                if (stack.Collectible.NutritionProps != null || stack.Collectible.CombustibleProps?.SmeltedStack?.ResolvedItemstack?.Collectible?.NutritionProps != null)
                {
                    float weightedStackSize = stack.StackSize * AnimalWeight;
                    float fraction = weightedStackSize - (int)weightedStackSize;
                    stack.StackSize = (int)weightedStackSize + (entity.World.Rand.NextDouble() > fraction ? 1 : 0);
                }

                if (stack.StackSize == 0) continue;

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

            if (entity.World.Side == EnumAppSide.Server)
            {
                entity.World.BlockAccessor.GetChunkAtBlockPos(entity.ServerPos.AsBlockPos).MarkModified();
            }

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
                    if (deathByEntityLangCode.Contains("wolf"))
                    {
                        infotext.AppendLine(Lang.Get("deadcreature-eaten-wolf"));
                    } else
                    {
                        infotext.AppendLine(Lang.Get("deadcreature-eaten"));
                    }
                }
            }

            if (AnimalWeight >= 0.95f)
            {
                infotext.AppendLine(Lang.Get("creature-weight-good"));
            } else if (AnimalWeight >= 0.75f)
            {
                infotext.AppendLine(Lang.Get("creature-weight-ok"));
            }
            else if (AnimalWeight >= 0.5f)
            {
                infotext.AppendLine(Lang.Get("creature-weight-low"));
            } else
            {
                infotext.AppendLine(Lang.Get("creature-weight-starving"));
            }


            base.GetInfoText(infotext);
        }


        

        public override string PropertyName()
        {
            return "harvestable";
        }
        
    }
}
