using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    class FailedAttempt
    {
        public long LastTryMs;
        public int Count;
    }

    public class AiTaskSeekFoodAndEat : AiTaskBase
    {
        AssetLocation eatSound;

        POIRegistry porregistry;
        IAnimalFoodSource targetPoi;

        float moveSpeed = 0.02f;
        long stuckatMs = 0;
        bool nowStuck = false;

        float eatTime = 1f;

        float eatTimeNow = 0;
        bool soundPlayed = false;
        bool doConsumePortion = true;
        bool eatAnimStarted = false;
        bool playEatAnimForLooseItems = true;

        bool eatLooseItems;
        bool searchPlayerInv;

        HashSet<EnumFoodCategory> eatItemCategories = new HashSet<EnumFoodCategory>();
        HashSet<AssetLocation> eatItemCodes = new HashSet<AssetLocation>();

        float quantityEaten;

        AnimationMetaData eatAnimMeta;
        AnimationMetaData eatAnimMetaLooseItems;

        Dictionary<IAnimalFoodSource, FailedAttempt> failedSeekTargets = new Dictionary<IAnimalFoodSource, FailedAttempt>();

        float extraTargetDist;
        long lastPOISearchTotalMs;

        public AiTaskSeekFoodAndEat(EntityAgent entity) : base(entity)
        {
            porregistry = entity.Api.ModLoader.GetModSystem<POIRegistry>();

            entity.WatchedAttributes.SetBool("doesEat", true);
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            if (taskConfig["eatSound"] != null)
            {
                string eatsoundstring = taskConfig["eatSound"].AsString(null);
                if (eatsoundstring != null) eatSound = new AssetLocation(eatsoundstring).WithPathPrefix("sounds/");
            }
            
            if (taskConfig["movespeed"] != null)
            {
                moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
            }

            if (taskConfig["searchPlayerInv"] != null)
            {
                searchPlayerInv = taskConfig["searchPlayerInv"].AsBool(false);
            }

            if (taskConfig["eatTime"] != null)
            {
                eatTime = taskConfig["eatTime"].AsFloat(1.5f);
            }

            if (taskConfig["doConsumePortion"] != null)
            {
                doConsumePortion = taskConfig["doConsumePortion"].AsBool(true);
            }

            if (taskConfig["eatLooseItems"] != null)
            {
                eatLooseItems = taskConfig["eatLooseItems"].AsBool(true);
            }

            if (taskConfig["playEatAnimForLooseItems"] != null)
            {
                playEatAnimForLooseItems = taskConfig["playEatAnimForLooseItems"].AsBool(true);
            }

            if (taskConfig["eatItemCategories"] != null)
            {
                foreach (var val in taskConfig["eatItemCategories"].AsArray<EnumFoodCategory>(new EnumFoodCategory[0]))
                {
                    eatItemCategories.Add(val);
                }
            }

            if (taskConfig["eatItemCodes"] != null)
            {
                foreach (var val in taskConfig["eatItemCodes"].AsArray(new AssetLocation[0]))
                {
                    eatItemCodes.Add(val);
                }
            }

            if (taskConfig["eatAnimation"].Exists)
            {
                eatAnimMeta = new AnimationMetaData()
                {
                    Code = taskConfig["eatAnimation"].AsString()?.ToLowerInvariant(),
                    Animation = taskConfig["eatAnimation"].AsString()?.ToLowerInvariant(),
                    AnimationSpeed = taskConfig["eatAnimationSpeed"].AsFloat(1f)
                }.Init();
            }

            if (taskConfig["eatAnimationLooseItems"].Exists)
            {
                eatAnimMetaLooseItems = new AnimationMetaData()
                {
                    Code = taskConfig["eatAnimationLooseItems"].AsString()?.ToLowerInvariant(),
                    Animation = taskConfig["eatAnimationLooseItems"].AsString()?.ToLowerInvariant(),
                    AnimationSpeed = taskConfig["eatAnimationSpeedLooseItems"].AsFloat(1f)
                }.Init();
            }
        }

        public override bool ShouldExecute()
        {
            if (entity.World.Rand.NextDouble() < 0.005) return false;
            // Don't search more often than every 15 seconds
            if (lastPOISearchTotalMs + 15000 > entity.World.ElapsedMilliseconds) return false;
            if (cooldownUntilMs > entity.World.ElapsedMilliseconds) return false;
            if (cooldownUntilTotalHours > entity.World.Calendar.TotalHours) return false;
            if (whenInEmotionState != null && !entity.HasEmotionState(whenInEmotionState)) return false;
            if (whenNotInEmotionState != null && entity.HasEmotionState(whenNotInEmotionState)) return false;

            EntityBehaviorMultiplyBase bh = entity.GetBehavior<EntityBehaviorMultiplyBase>();
            if (bh != null && !bh.ShouldEat && entity.World.Rand.NextDouble() < 0.996) return false; // 0.4% chance go to the food source anyway just because (without eating anything).

            targetPoi = null;
            extraTargetDist = 0;
            lastPOISearchTotalMs = entity.World.ElapsedMilliseconds;

            entity.World.Api.ModLoader.GetModSystem<EntityPartitioning>().WalkEntities(entity.ServerPos.XYZ, 10, (e) =>
            {
                if (e is EntityItem)
                {
                    EntityItem ei = (EntityItem)e;
                    EnumFoodCategory? cat = ei.Itemstack?.Collectible?.NutritionProps?.FoodCategory;
                    if (cat != null && eatItemCategories.Contains((EnumFoodCategory)cat))
                    {
                        targetPoi = new LooseItemFoodSource(ei);
                        return false;
                    }

                    AssetLocation code = ei.Itemstack?.Collectible?.Code;
                    if (code != null && eatItemCodes.Contains(code))
                    {
                        targetPoi = new LooseItemFoodSource(ei);
                        return false;
                    }
                }

                if (searchPlayerInv && e is EntityPlayer eplr)
                {
                    if (eplr.Player.InventoryManager.Find(slot => slot.Inventory is InventoryBasePlayer && !slot.Empty && eatItemCodes.Contains(slot.Itemstack.Collectible.Code)))
                    {
                        targetPoi = new PlayerPoi(eplr);
                    }
                }

                return true;
            });

            if (targetPoi == null)
            {   
                targetPoi = porregistry.GetNearestPoi(entity.ServerPos.XYZ, 48, (poi) =>
                {
                    if (poi.Type != "food") return false;
                    IAnimalFoodSource foodPoi;

                    if ((foodPoi = poi as IAnimalFoodSource)?.IsSuitableFor(entity) == true)
                    {
                        FailedAttempt attempt;
                        failedSeekTargets.TryGetValue(foodPoi, out attempt);
                        if (attempt == null || (attempt.Count < 4 || attempt.LastTryMs < world.ElapsedMilliseconds - 60000))
                        {
                            return true;
                        }
                    }

                    return false;
                }) as IAnimalFoodSource;
            }
            
            /*if (targetPoi != null)
            {
                if (targetPoi is BlockEntity || targetPoi is Block)
                {
                    Block block = entity.World.BlockAccessor.GetBlock(targetPoi.Position.AsBlockPos);
                    Cuboidf[] collboxes = block.GetCollisionBoxes(entity.World.BlockAccessor, targetPoi.Position.AsBlockPos);
                    if (collboxes != null && collboxes.Length != 0 && collboxes[0].Y2 > 0.3f)
                    {
                        extraTargetDist = 0.15f;
                    }
                }
            }*/

            return targetPoi != null;
        }



        public float MinDistanceToTarget()
        {
            return Math.Max(extraTargetDist + 0.6f, entity.CollisionBox.XSize / 2 + 0.05f);
        }

        public override void StartExecute()
        {
            base.StartExecute();
            stuckatMs = -9999;
            nowStuck = false;
            soundPlayed = false;
            eatTimeNow = 0;
            pathTraverser.NavigateTo(targetPoi.Position, moveSpeed, MinDistanceToTarget() - 0.1f, OnGoalReached, OnStuck, false, 1000, true);
            eatAnimStarted = false;
        }

        public override bool ContinueExecute(float dt)
        {
            Vec3d pos = targetPoi.Position;

            pathTraverser.CurrentTarget.X = pos.X;
            pathTraverser.CurrentTarget.Y = pos.Y;
            pathTraverser.CurrentTarget.Z = pos.Z;

            Cuboidd targetBox = entity.CollisionBox.ToDouble().Translate(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            double distance = targetBox.ShortestDistanceFrom(pos);          

            float minDist = MinDistanceToTarget();

            if (distance <= minDist)
            {
                pathTraverser.Stop();
                if (animMeta != null)
                {
                    entity.AnimManager.StopAnimation(animMeta.Code);
                }

                EntityBehaviorMultiplyBase bh = entity.GetBehavior<EntityBehaviorMultiplyBase>();
                if (bh != null && !bh.ShouldEat)
                {
                    return false;
                }

                if (targetPoi.IsSuitableFor(entity) != true) return false;
                
                if (eatAnimMeta != null && !eatAnimStarted)
                {
                    entity.AnimManager.StartAnimation((targetPoi is LooseItemFoodSource && eatAnimMetaLooseItems != null) ? eatAnimMetaLooseItems : eatAnimMeta);                        

                    eatAnimStarted = true;
                }

                eatTimeNow += dt;

                if (targetPoi is LooseItemFoodSource foodSource)
                {
                    entity.World.SpawnCubeParticles(entity.ServerPos.XYZ, foodSource.ItemStack, 0.25f, 1, 0.25f + 0.5f * (float)entity.World.Rand.NextDouble());
                }
                

                if (eatTimeNow > eatTime * 0.75f && !soundPlayed)
                {
                    soundPlayed = true;
                    if (eatSound != null) entity.World.PlaySoundAt(eatSound, entity, null, true, 16, 1);
                }


                if (eatTimeNow >= eatTime)
                {
                    ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
                    if (tree == null) entity.WatchedAttributes["hunger"] = tree = new TreeAttribute();

                    if (doConsumePortion)
                    {
                        float sat = targetPoi.ConsumeOnePortion();
                        quantityEaten += sat;
                        tree.SetFloat("saturation", sat + tree.GetFloat("saturation", 0));
                        entity.WatchedAttributes.SetDouble("lastMealEatenTotalHours", entity.World.Calendar.TotalHours);
                        entity.WatchedAttributes.MarkPathDirty("hunger");
                    }
                    else quantityEaten = 1;

                    failedSeekTargets.Remove(targetPoi);

                    return false;
                }
            } else
            {
                if (!pathTraverser.Active)
                {
                    float rndx = (float)entity.World.Rand.NextDouble() * 0.3f - 0.15f;
                    float rndz = (float)entity.World.Rand.NextDouble() * 0.3f - 0.15f;
                    if (!pathTraverser.NavigateTo(targetPoi.Position.AddCopy(rndx, 0, rndz), moveSpeed, MinDistanceToTarget() - 0.15f, OnGoalReached, OnStuck, false, 500, true))
                    {
                        return false;
                    }
                }
            }


            if (nowStuck && entity.World.ElapsedMilliseconds > stuckatMs + eatTime * 1000)
            {
                return false;
            }


            return true;
        }


        float GetSaturation()
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (tree == null) entity.WatchedAttributes["hunger"] = tree = new TreeAttribute();

            return tree.GetFloat("saturation");
        }


        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);
            pathTraverser.Stop();

            if (eatAnimMeta != null)
            {
                entity.AnimManager.StopAnimation(eatAnimMeta.Code);
            }

            if (cancelled)
            {
                cooldownUntilTotalHours = 0;
            }

            if (quantityEaten < 1)
            {
                cooldownUntilTotalHours = 0;
            } else
            {
                quantityEaten = 0;
            }
        }



        private void OnStuck()
        {
            stuckatMs = entity.World.ElapsedMilliseconds;
            nowStuck = true;

            FailedAttempt attempt = null;
            failedSeekTargets.TryGetValue(targetPoi, out attempt);
            if (attempt == null)
            {
                failedSeekTargets[targetPoi] = attempt = new FailedAttempt();
            }

            attempt.Count++;
            attempt.LastTryMs = world.ElapsedMilliseconds;
            
        }

        private void OnGoalReached()
        {
            pathTraverser.Active = true;
            failedSeekTargets.Remove(targetPoi);
        }


    }

    public class PlayerPoi : IAnimalFoodSource
    {
        EntityPlayer plr;
        Vec3d pos = new Vec3d();

        public PlayerPoi(EntityPlayer plr)
        {
            this.plr = plr;
        }

        public Vec3d Position
        {
            get
            {
                pos.Set(plr.Pos.X, plr.Pos.Y, plr.Pos.Z);
                return pos;
            }
        }

        public string Type => "food";

        public float ConsumeOnePortion()
        {
            return 0;
        }

        public bool IsSuitableFor(Entity entity)
        {
            return false;
        }
    }


    public class LooseItemFoodSource : IAnimalFoodSource
    {
        EntityItem entity;

        public LooseItemFoodSource(EntityItem entity)
        {
            this.entity = entity;
        }

        public ItemStack ItemStack => entity.Itemstack;

        public Vec3d Position => entity.ServerPos.XYZ;

        public string Type => "food";

        public float ConsumeOnePortion()
        {
            entity.Itemstack.StackSize--;
            if (entity.Itemstack.StackSize <= 0) entity.Die();
            return 1f;
        }

        public bool IsSuitableFor(Entity entity)
        {
            return true;
        }
    }

}
