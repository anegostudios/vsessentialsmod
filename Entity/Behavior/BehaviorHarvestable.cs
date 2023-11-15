using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
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
    public class ModSystemSyncHarvestableDropsToClient : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            foreach (var etype in api.World.EntityTypes)
            {
                foreach (var bh in etype.Server.BehaviorsAsJsonObj)
                {
                    if (bh["code"].AsString() == "harvestable")
                    {
                        if (etype.Attributes == null)
                        {
                            etype.Attributes = new JsonObject(JToken.Parse("{}"));
                        }
                        etype.Attributes.Token["harvestableDrops"] = bh["drops"].Token;
                    }
                }
            }
        }

    }

    public class EntityBehaviorHarvestable : EntityBehavior
    {
        const float minimumWeight = 0.5f;
        protected BlockDropItemStack[] jsonDrops;

        protected InventoryGeneric inv;
        protected GuiDialogCreatureContents dlg;

        bool GotCrushed
        {
            get
            {
                return
                    (entity.WatchedAttributes.HasAttribute("deathReason") && (EnumDamageSource)entity.WatchedAttributes.GetInt("deathReason") == EnumDamageSource.Fall) ||
                    (entity.WatchedAttributes.HasAttribute("deathDamageType") && (EnumDamageType)entity.WatchedAttributes.GetInt("deathDamageType") == EnumDamageType.Crushing)
                ;
            }
        }

        bool GotElectrocuted
        {
            get
            {
                return entity.WatchedAttributes.HasAttribute("deathDamageType") && (EnumDamageType)entity.WatchedAttributes.GetInt("deathDamageType") == EnumDamageType.Electricity;
            }
        }

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
                if (GotCrushed)
                {
                    return 0.5f;
                }

                string deathByEntityCode = entity.WatchedAttributes.GetString("deathByEntity");

                if (deathByEntityCode != null && !entity.WatchedAttributes.HasAttribute("deathByPlayer"))
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
            return baseHarvestDuration * forEntity.Stats.GetBlended("animalHarvestingTime");
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

            harshWinters = entity.World.Config.GetString("harshWinters").ToBool(true);
        }

        public override void AfterInitialized(bool onSpawn)
        {
            if (onSpawn)
            {
                LastWeightUpdateTotalHours = Math.Max(1, entity.World.Calendar.TotalHours - 24 * 7);   // Let it do 1 week's worth of fast-forwarding to reflect recent weather conditions
                AnimalWeight = fixedWeight ? 1.0f : 0.66f + 0.2f * (float)entity.World.Rand.NextDouble();
            }
            else if (fixedWeight) AnimalWeight = 1.0f;
        }

        bool harshWinters;
        bool fixedWeight;
        float accum = 0;

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            accum += deltaTime;

            if (accum > 1.5f)
            {
                accum = 0;
                if (!harshWinters || fixedWeight)
                {
                    AnimalWeight = 1;
                    return;
                }

                double totalHours = entity.World.Calendar.TotalHours;
                double startHours = LastWeightUpdateTotalHours;
                double hoursPerDay = entity.World.Calendar.HoursPerDay;

                // Don't simulate longer than a month per tick
                totalHours = Math.Min(totalHours, startHours + hoursPerDay * entity.World.Calendar.DaysPerMonth);

                if (startHours < totalHours - 1)
                {
                    double lastEatenTotalHours = entity.WatchedAttributes.GetDouble("lastMealEatenTotalHours", -9999);
                    double fourmonthsHours = 4 * entity.World.Calendar.DaysPerMonth * hoursPerDay;
                    double oneweekHours = 7 * hoursPerDay;
                    BlockPos pos = entity.Pos.AsBlockPos;
                    float weight = AnimalWeight;
                    float previousweight = weight;

                    float step = 3;
                    float baseTemperature = 0f;
                    ClimateCondition conds = null;

                    do
                    {
                        // no need to simulate every single hour
                        startHours += step;

                        double mealHourDiff = startHours - lastEatenTotalHours;
                        if (mealHourDiff < 0) mealHourDiff = fourmonthsHours;  // Can't count meals eaten in the future  (that's possible in the fast-forwarding simulation)
                        bool ateSomeTimeAgo = mealHourDiff < fourmonthsHours;

                        if (!ateSomeTimeAgo)
                        {
                            if (weight <= minimumWeight)   // No point doing the costly processing if the weight is already at the minimum
                            {
                                startHours = totalHours;
                                break;
                            }

                            if (conds == null)
                            {
                                conds = entity.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, startHours / hoursPerDay);

                                if (conds == null)
                                {
                                    base.OnGameTick(deltaTime);
                                    return;
                                }
                                baseTemperature = conds.WorldGenTemperature;
                            }
                            else
                            {
                                conds.Temperature = baseTemperature;  // Keep resetting the field we are interested in, because it can be modified by the OnGetClimate event
                                entity.World.BlockAccessor.GetClimateAt(pos, conds, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, startHours / hoursPerDay);
                            }

                            if (conds.Temperature <= 0)
                            {
                                // Loose 0.1% of weight each hour = 2.4% per day
                                weight = Math.Max(minimumWeight, weight - step * 0.001f);
                            }
                        }
                        else
                        {
                            bool ateRecently = mealHourDiff < oneweekHours;
                            weight = Math.Min(1f, weight + step * (0.001f + (ateRecently ? 0.05f : 0)));
                        }
                    } while (startHours < totalHours - 1);
                    if (weight != previousweight) AnimalWeight = weight;
                }

                LastWeightUpdateTotalHours = startHours;
            }

            //base.OnGameTick(deltaTime);    // Currently commented out because (a) it does nothing; (b) we have server-side return paths which do not reach this line
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
            if (inv.Empty && entity.GetBehavior<EntityBehaviorDeadDecay>()!=null)
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
            inv = new InventoryGeneric(typeAttributes["quantitySlots"].AsInt(4), "harvestableContents-" + entity.EntityId, entity.Api);
            TreeAttribute tree = entity.WatchedAttributes["harvestableInv"] as TreeAttribute;
            if (tree != null) inv.FromTreeAttributes(tree);
            inv.PutLocked = true;

            if (entity.World.Side == EnumAppSide.Server)
            {
                inv.SlotModified += Inv_SlotModified;
                inv.OnInventoryClosed += Inv_OnInventoryClosed;
            }

            base.Initialize(properties, typeAttributes);

            if (entity.World.Side == EnumAppSide.Server)
            {
                jsonDrops = typeAttributes["drops"].AsObject<BlockDropItemStack[]>();
            }

            baseHarvestDuration = typeAttributes["duration"].AsFloat(5);

            fixedWeight = typeAttributes["fixedweight"].AsBool(false);
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
                dlg = new GuiDialogCreatureContents(inv, entity as EntityAgent, entity.Api as ICoreClientAPI, "carcasscontents");
                if (dlg.TryOpen())
                {
                    (entity.World.Api as ICoreClientAPI).Network.SendPacketClient(inv.Open(player));
                }

                dlg.OnClosed += () =>
                {
                    dlg.Dispose();
                    dlg = null;
                };
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
                BlockDropItemStack dstack = jsonDrops[i];
                if (dstack.Tool != null && (byPlayer == null || dstack.Tool != byPlayer.InventoryManager.ActiveTool)) continue;

                dstack.Resolve(entity.World, "BehaviorHarvestable ", entity.Code);

                float extraMul = 1f;
                if (dstack.DropModbyStat != null)
                {
                    // If the stat does not exist, then GetBlended returns 1 \o/
                    extraMul = byPlayer?.Entity?.Stats.GetBlended(dstack.DropModbyStat) ?? 0;
                }

                ItemStack stack = dstack.GetNextItemStack(this.dropQuantityMultiplier * dropQuantityMultiplier * extraMul);

                if (stack == null) continue;

                if (stack.Collectible.NutritionProps != null || stack.Collectible.CombustibleProps?.SmeltedStack?.ResolvedItemstack?.Collectible?.NutritionProps != null)
                {
                    float weightedStackSize = stack.StackSize * AnimalWeight;
                    stack.StackSize = GameMath.RoundRandom(entity.World.Rand, weightedStackSize);
                }

                if (stack.StackSize == 0) continue;

                if (stack.Collectible is IResolvableCollectible irc)
                {
                    var slot = new DummySlot(stack);
                    irc.Resolve(slot, entity.World);
                    stack = slot.Itemstack;
                }

                todrop.Add(stack);
                if (dstack.LastDrop) break;
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
            entity.WatchedAttributes.MarkPathDirty("harvested");

            if (entity.World.Side == EnumAppSide.Server)
            {
                entity.World.BlockAccessor.GetChunkAtBlockPos(entity.ServerPos.AsBlockPos).MarkModified();
            }
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
                        HotKeyCode = "shift",
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
                if (GotCrushed)
                {
                    infotext.AppendLine(Lang.Get("Looks crushed. Won't be able to harvest as much from this carcass."));
                }
                if (GotElectrocuted)
                {
                    infotext.AppendLine(Lang.Get("Looks partially charred, perhaps due to a lightning strike."));
                }
                
                string deathByEntityCode = entity.WatchedAttributes.GetString("deathByEntity");

                if (deathByEntityCode != null && !entity.WatchedAttributes.HasAttribute("deathByPlayer")) {

                    string code = "deadcreature-killed";

                    var props = entity.World.GetEntityType(new AssetLocation(deathByEntityCode));
                    if (props != null && props.Attributes?["killedByInfoText"].Exists == true)
                    {
                        code = props.Attributes["killedByInfoText"].AsString();
                    }

                    infotext.AppendLine(Lang.Get(code));
                }
            }

            if (!fixedWeight)
            {
                if (AnimalWeight >= 0.95f)
                {
                    infotext.AppendLine(Lang.Get("creature-weight-good"));
                }
                else if (AnimalWeight >= 0.75f)
                {
                    infotext.AppendLine(Lang.Get("creature-weight-ok"));
                }
                else if (AnimalWeight >= 0.5f)
                {
                    infotext.AppendLine(Lang.Get("creature-weight-low"));
                }
                else
                {
                    infotext.AppendLine(Lang.Get("creature-weight-starving"));
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
