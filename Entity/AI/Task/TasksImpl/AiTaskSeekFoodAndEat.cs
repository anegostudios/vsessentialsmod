using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

#nullable disable

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
        

        bool eatLooseItems;
        float quantityEaten;

        AnimationMetaData eatAnimMeta;
        AnimationMetaData eatAnimMetaLooseItems;

        Dictionary<IAnimalFoodSource, FailedAttempt> failedSeekTargets = new Dictionary<IAnimalFoodSource, FailedAttempt>();

        float extraTargetDist;
        long lastPOISearchTotalMs;

        public CreatureDiet Diet;
        EntityBehaviorMultiplyBase bhMultiply;

        ICoreAPI api;

        public AiTaskSeekFoodAndEat(EntityAgent entity) : base(entity)
        {
            api = entity.Api;
            porregistry = api.ModLoader.GetModSystem<POIRegistry>();

            entity.WatchedAttributes.SetBool("doesEat", true);
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            string eatsoundstring = taskConfig["eatSound"].AsString(null);
            if (eatsoundstring != null) eatSound = new AssetLocation(eatsoundstring).WithPathPrefix("sounds/");

            moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
            eatTime = taskConfig["eatTime"].AsFloat(1.5f);
            doConsumePortion = taskConfig["doConsumePortion"].AsBool(true);
            eatLooseItems = taskConfig["eatLooseItems"].AsBool(true);
            
            Diet = entity.Properties.Attributes["creatureDiet"].AsObject<CreatureDiet>();
            if (Diet == null) api.Logger.Warning("Creature " + entity.Code.ToShortString() + " has SeekFoodAndEat task but no Diet specified");

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

        public override void AfterInitialize()
        {
            bhMultiply = entity.GetBehavior<EntityBehaviorMultiplyBase>();
        }

        public override bool ShouldExecute()
        {
            if (entity.World.Rand.NextDouble() < 0.005) return false;
            // Don't search more often than every 15 seconds
            if (lastPOISearchTotalMs + 15000 > entity.World.ElapsedMilliseconds) return false;
            if (cooldownUntilMs > entity.World.ElapsedMilliseconds) return false;
            if (cooldownUntilTotalHours > entity.World.Calendar.TotalHours) return false;
            if (!PreconditionsSatisifed()) return false;

            if (bhMultiply != null && !bhMultiply.ShouldEat && entity.World.Rand.NextDouble() < 0.996) return false; // 0.4% chance go to the food source anyway just because (without eating anything).
            if (Diet == null) return false;   // Deals with mods which have not properly updated for 1.19, check this condition last because always passes in vanilla

            targetPoi = null;
            extraTargetDist = 0;
            lastPOISearchTotalMs = entity.World.ElapsedMilliseconds;

            if (eatLooseItems)
            {
                api.ModLoader.GetModSystem<EntityPartitioning>().WalkEntities(entity.ServerPos.XYZ, 10, (e) =>
                {
                    if (e is EntityItem eitem && suitableFoodSource(eitem.Itemstack))
                    {
                        targetPoi = new LooseItemFoodSource(eitem);
                        return false;   // Stop the walk when food found
                    }

                    return true;
                }, EnumEntitySearchType.Inanimate);
            }

            if (targetPoi == null)
            {
                targetPoi = porregistry.GetNearestPoi(entity.ServerPos.XYZ, 48, (poi) =>
                {
                    if (poi.Type != "food") return false;
                    IAnimalFoodSource foodPoi;

                    if ((foodPoi = poi as IAnimalFoodSource)?.IsSuitableFor(entity, Diet) == true)
                    {
                        failedSeekTargets.TryGetValue(foodPoi, out FailedAttempt attempt);
                        if (attempt == null || (attempt.Count < 4 || attempt.LastTryMs < world.ElapsedMilliseconds - 60000))
                        {
                            return true;
                        }
                    }

                    return false;
                }) as IAnimalFoodSource;
            }

            return targetPoi != null;
        }

        private bool suitableFoodSource(ItemStack itemStack)
        {
            EnumFoodCategory cat = itemStack?.Collectible?.NutritionProps?.FoodCategory ?? EnumFoodCategory.NoNutrition;
            var attr = itemStack?.ItemAttributes;
            var tags = attr["foodTags"].AsArray<string>();

            return Diet.Matches(cat, tags, 0f);
        }

        public float MinDistanceToTarget()
        {
            return Math.Max(extraTargetDist + 0.6f, entity.SelectionBox.XSize / 2 + 0.05f);
        }


        public override void StartExecute()
        {
            base.StartExecute();
            stuckatMs = -9999;
            nowStuck = false;
            soundPlayed = false;
            eatTimeNow = 0;
            pathTraverser.NavigateTo_Async(targetPoi.Position, moveSpeed, MinDistanceToTarget() - 0.1f, OnGoalReached, OnStuck, null, 1000, 1);
            eatAnimStarted = false;
        }

        public override bool CanContinueExecute()
        {
            return pathTraverser.Ready;
        }

        public override bool ContinueExecute(float dt)
        {
            Vec3d pos = targetPoi.Position;

            pathTraverser.CurrentTarget.X = pos.X;
            pathTraverser.CurrentTarget.Y = pos.Y;
            pathTraverser.CurrentTarget.Z = pos.Z;

            Cuboidd targetBox = entity.SelectionBox.ToDouble().Translate(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            double distance = targetBox.ShortestDistanceFrom(pos);          

            float minDist = MinDistanceToTarget();

            if (distance <= minDist)
            {
                pathTraverser.Stop();
                if (animMeta != null)
                {
                    entity.AnimManager.StopAnimation(animMeta.Code);
                }

                if (bhMultiply != null && !bhMultiply.ShouldEat)
                {
                    return false;
                }

                if (targetPoi.IsSuitableFor(entity, Diet) != true) return false;
                
                if (eatAnimMeta != null && !eatAnimStarted)
                {
                    entity.AnimManager.StartAnimation((targetPoi is LooseItemFoodSource && eatAnimMetaLooseItems != null) ? eatAnimMetaLooseItems : eatAnimMeta);                        

                    eatAnimStarted = true;
                }

                eatTimeNow += dt;

                if (targetPoi is LooseItemFoodSource foodSource)
                {
                    entity.World.SpawnCubeParticles(targetPoi.Position, foodSource.ItemStack, 0.25f, 1, 0.25f + 0.5f * (float)entity.World.Rand.NextDouble());
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
                        float sat = targetPoi.ConsumeOnePortion(entity);
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
                    if (!pathTraverser.NavigateTo(targetPoi.Position.AddCopy(rndx, 0, rndz), moveSpeed, minDist - 0.15f, OnGoalReached, OnStuck, null, false, 500, 1))
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



        public override void FinishExecute(bool cancelled)
        {
            // don't call base method, we set the cool down manually
            // Instead of resetting the cool down to current time + delta, we add it, so that the animal can eat multiple times, to catch up on lost time 
            var bh = entity.GetBehavior<EntityBehaviorMultiply>();
            if (bh != null && bh.PortionsLeftToEat > 0 && !bh.IsPregnant)
            {
                cooldownUntilTotalHours += mincooldownHours + entity.World.Rand.NextDouble() * (maxcooldownHours - mincooldownHours);
            } else
            {
                cooldownUntilTotalHours = api.World.Calendar.TotalHours + mincooldownHours + entity.World.Rand.NextDouble() * (maxcooldownHours - mincooldownHours);
            }

            pathTraverser.Stop();


            if (eatAnimMeta != null)
            {
                entity.AnimManager.StopAnimation(eatAnimMeta.Code);
            }

            if (animMeta != null)
            {
                entity.AnimManager.StopAnimation(animMeta.Code);
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

            failedSeekTargets.TryGetValue(targetPoi, out FailedAttempt attempt);
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

        public float ConsumeOnePortion(Entity entity)
        {
            return 0;
        }

        public bool IsSuitableFor(Entity entity, CreatureDiet diet)
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

        public float ConsumeOnePortion(Entity entity)
        {
            this.entity.Itemstack.StackSize--;
            if (this.entity.Itemstack.StackSize <= 0) this.entity.Die();
            return this.entity.Itemstack.StackSize >= 0 ? 1f : 0f;
        }

        public bool IsSuitableFor(Entity entity, CreatureDiet diet)
        {
            return true;
        }
    }

}
