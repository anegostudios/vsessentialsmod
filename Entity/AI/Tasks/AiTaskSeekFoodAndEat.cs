using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class AiTaskSeekFoodAndEat : AiTaskSeekPoiBase<IAnimalFoodSource>
    {
        protected AssetLocation? eatSound;
        protected bool soundPlayed = false;

        /// <summary>
        /// How long it takes to eat, in seconds. At 75% of this time, the eatSound will play.
        /// </summary>
        [JsonProperty]
        protected float eatTime = 1.5f;

        /// <summary>
        /// If true, the creature will use up some of the food to increase its saturation. Otherwise, it will walk to the food and play the animation, but not consume any.
        /// </summary>
        [JsonProperty]
        protected bool doConsumePortion = true;

        /// <summary>
        /// If true, the creature will check dropped items and go after any that match its diet.
        /// </summary>
        [JsonProperty]
        protected bool eatLooseItems = true;

        /// <summary>
        /// Chance for entity to go to the food source (without eating) when it has a multiply behavior that isn't ready to eat
        /// If eatAnyway is true on the multiply behavior, this will never be used because that will always be ready to eat
        /// </summary>
        [JsonProperty]
        public float seekFoodAnywayChance = 0.004f;

        protected float quantityEaten;

        protected AnimationMetaData? eatAnimMeta;
        protected AnimationMetaData? eatAnimMetaLooseItems;

        public CreatureDiet Diet;
        protected EntityBehaviorMultiplyBase? bhMultiply;

        public AiTaskSeekFoodAndEat(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            entity.WatchedAttributes.SetBool("doesEat", true);

            // As of 1.23, we no longer support the pre-1.19 lack of creature diet
            Diet = entity.Properties.Attributes["creatureDiet"].AsObject<CreatureDiet>()
                ?? throw new Exception("Creature " + entity.Code.ToShortString() + " has SeekFoodAndEat task but no Diet specified");

            string? eatsoundstring = taskConfig["eatSound"].AsString(null);
            if (eatsoundstring != null) eatSound = new AssetLocation(eatsoundstring).WithPathPrefix("sounds/");

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

        protected override void SetDefaultValues()
        {
            base.SetDefaultValues();
            ExecutionChance = 0.995;
        }

        public override void AfterInitialize()
        {
            base.AfterInitialize();
            bhMultiply = entity.GetBehavior<EntityBehaviorMultiplyBase>();
        }

        public override bool ShouldExecute()
        {
            if (entity.World.Rand.NextDouble() > ExecutionChance) return false;
            if (!IsSearchTime()) return false;

            if (bhMultiply != null && !bhMultiply.ShouldEat && entity.World.Rand.NextDouble() >= seekFoodAnywayChance) return false;

            targetPoi = null;
            lastPOISearchTotalMs = entity.World.ElapsedMilliseconds;

            if (eatLooseItems)
            {
                entity.Api.ModLoader.GetModSystem<EntityPartitioning>().WalkEntities(entity.Pos.XYZ, 10, (e) =>
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
                targetPoi = pointsOfInterest.GetNearestPoi(entity.Pos.XYZ, 48, (poi) =>
                {
                    if (poi.Type != "food") return false;
                    IAnimalFoodSource? foodPoi = poi as IAnimalFoodSource;
                
                    return foodPoi != null && foodPoi.IsSuitableFor(entity, Diet) && !RecentlyFailedSeek(foodPoi);
                }) as IAnimalFoodSource;
            }

            return targetPoi != null;
        }

        private bool suitableFoodSource(ItemStack itemStack)
        {
            EnumFoodCategory cat = itemStack?.Collectible.GetNutritionProperties(world, itemStack, entity)?.FoodCategory ?? EnumFoodCategory.NoNutrition;
            var tags = itemStack?.ItemAttributes?["foodTags"].AsArray<string>() ?? [];

            return Diet.Matches(cat, tags, 0f);
        }


        public override void StartExecute()
        {
            base.StartExecute();
            soundPlayed = false;
        }

        protected override void OnArrival()
        {
            if (eatAnimMeta != null)
            {
                entity.AnimManager.StartAnimation((targetPoi is LooseItemFoodSource && eatAnimMetaLooseItems != null) ? eatAnimMetaLooseItems : eatAnimMeta);
            }

            if (bhMultiply != null && !bhMultiply.ShouldEat)
            {
                done = true;
            }
        }

        protected override void TickTargetReached()
        {
            if (targetPoi == null)
            {
                done = true;
                return;
            }

            if (targetPoi is LooseItemFoodSource foodSource && foodSource.ItemStack != null)
            {
                entity.World.SpawnCubeParticles(targetPoi.Position, foodSource.ItemStack, 0.25f, 1, 0.25f + 0.5f * (float)entity.World.Rand.NextDouble());
            }

            if (timeSinceTargetReached > eatTime * 0.75f && !soundPlayed)
            {
                soundPlayed = true;
                if (eatSound != null) entity.World.PlaySoundAt(eatSound, entity, null, true, 16, 1);
            }

            if (timeSinceTargetReached >= eatTime)
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

                done = true;
            }
        }

        protected override bool ShouldAbort()
        {
            return base.ShouldAbort() || targetPoi?.IsSuitableFor(entity, Diet) != true;
        }


        public override void FinishExecute(bool cancelled)
        {
            double prevCooldownHours = cooldownUntilTotalHours;
            base.FinishExecute(cancelled);

            // Instead of resetting the cool down to current time + delta, we add it, so that the animal can eat multiple times, to catch up on lost time
            EntityBehaviorMultiply? multiplyBehavior = entity.GetBehavior<EntityBehaviorMultiply>();
            if (multiplyBehavior != null && multiplyBehavior.PortionsLeftToEat > 0 && !multiplyBehavior.IsPregnant)
            {
                cooldownUntilTotalHours = prevCooldownHours + MinCooldownHours + entity.World.Rand.NextDouble() * (MaxCooldownHours - MinCooldownHours);
            }

            if (eatAnimMeta != null)
            {
                entity.AnimManager.StopAnimation(eatAnimMeta.Code);
            }

            if (eatAnimMetaLooseItems != null)
            {
                entity.AnimManager.StopAnimation(eatAnimMetaLooseItems.Code);
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

        public Vec3d Position => entity.Pos.XYZ;

        public string Type => "food";

        public float ConsumeOnePortion(Entity entity)
        {
            if (this.entity.Itemstack == null) return 0f;

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
