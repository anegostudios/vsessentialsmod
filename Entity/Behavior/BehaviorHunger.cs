using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent
{
    public interface IMountedHungerProvider
    {
        float OnMountedHungerTick(EntityAgent mountedEntity, float deltaTime);
    }

    /// <summary>
    /// Adds hunger to entity. Allows it to eat and die from starvation.
    /// <br/>Uses the "hunger" code
    /// </summary>
    /// <example><code lang="json">
    ///"behaviors": [
    /// {
    ///     "code": "hunger",
    ///     "currentsaturation": 1500.0,
    ///     "maxsaturation": 1500.0
    /// },
    ///],
    /// </code></example>
    [DocumentAsJson]
    public class EntityBehaviorHunger : EntityBehavior
    {
        ITreeAttribute hungerTree;
        EntityAgent entityAgent;

        float hungerCounter;
        float movementPenalty;
        float accumGameSpeedMul;

        long listenerId;

        public float SaturationLossDelayFruit
        {
            get { return hungerTree.GetFloat("saturationlossdelayfruit"); }
            set { hungerTree.SetFloat("saturationlossdelayfruit", value); entity.WatchedAttributes.MarkPathDirty("hunger"); }
        }

        public float SaturationLossDelayVegetable
        {
            get { return hungerTree.GetFloat("saturationlossdelayvegetable"); }
            set { hungerTree.SetFloat("saturationlossdelayvegetable", value); entity.WatchedAttributes.MarkPathDirty("hunger"); }
        }

        public float SaturationLossDelayProtein
        {
            get { return hungerTree.GetFloat("saturationlossdelayprotein"); }
            set { hungerTree.SetFloat("saturationlossdelayprotein", value); entity.WatchedAttributes.MarkPathDirty("hunger"); }
        }

        public float SaturationLossDelayGrain
        {
            get { return hungerTree.GetFloat("saturationlossdelaygrain"); }
            set { hungerTree.SetFloat("saturationlossdelaygrain", value); entity.WatchedAttributes.MarkPathDirty("hunger"); }
        }

        public float SaturationLossDelayDairy
        {
            get { return hungerTree.GetFloat("saturationlossdelaydairy"); }
            set { hungerTree.SetFloat("saturationlossdelaydairy", value); entity.WatchedAttributes.MarkPathDirty("hunger"); }
        }

        /// <summary>
        /// <!--<jsonalias>CurrentSaturation</jsonalias>-->
        /// The entity will have this much saturation upon spawn if this is set in JSON. Otherwise, this is the current saturation of the entity
        /// </summary>
        [DocumentAsJson("Optional", "1500")]
        public float Saturation
        {
            get { return hungerTree.GetFloat("currentsaturation"); }
            set { hungerTree.SetFloat("currentsaturation", value); entity.WatchedAttributes.MarkPathDirty("hunger"); }
        }

        /// <summary>
        /// Max possible saturation the entity can have in general
        /// </summary>
        [DocumentAsJson("Optional", "1500")]
        public float MaxSaturation
        {
            get { return hungerTree.GetFloat("maxsaturation"); }
            set { hungerTree.SetFloat("maxsaturation", value); entity.WatchedAttributes.MarkPathDirty("hunger"); }
        }

        /// <summary>
        /// <!--<jsonalias>SatLossPerSec</jsonalias>-->
        /// How many satiety points will be consumed per second of hunger.
        /// </summary>
        [DocumentAsJson("Optional", "0.24")]
        protected virtual float SatietyLossPerSecond { get; set; } = 0.24f;

        /// <summary>
        /// <!--<jsonalias>SatLossPerMoveSec</jsonalias>-->
        /// How many satiety points will be consumed per second of movement.
        /// </summary>
        [DocumentAsJson("Optional", "0.72")]
        protected virtual float SatietyLossPerMovementSecond { get; set; } = 0.72f;

        /// <summary>
        /// <!--<jsonalias>SatLossPerSwimSec</jsonalias>-->
        /// How many satiety points will be consumed per second of swimming.
        /// </summary>
        [DocumentAsJson("Optional", "0.72")]
        protected virtual float SatietyLossPerSwimSecond { get; set; } = 0.72f;

        /// <summary>
        /// <!--<jsonalias>SatLossPerSprintSec</jsonalias>-->
        /// How many satiety points will be consumed per second of sprinting.
        /// </summary>
        [DocumentAsJson("Optional", "2.22")]
        protected virtual float SatietyLossPerSprintSecond { get; set; } = 2.22f;

        /// <summary>
        /// <!--<jsonalias>SatLossPerJump</jsonalias>-->
        /// How many satiety points will be consumed per second of sprinting.
        /// </summary>
        [DocumentAsJson("Optional", "0.1")]
        protected virtual float SatietyLossPerJump { get; set; } = 0.1f;

        public float FruitLevel
        {
            get { return hungerTree.GetFloat("fruitLevel"); }
            set { hungerTree.SetFloat("fruitLevel", value); entity.WatchedAttributes.MarkPathDirty("hunger"); }
        }

        public float VegetableLevel
        {
            get { return hungerTree.GetFloat("vegetableLevel"); }
            set { hungerTree.SetFloat("vegetableLevel", value); entity.WatchedAttributes.MarkPathDirty("hunger"); }
        }

        public float ProteinLevel
        {
            get { return hungerTree.GetFloat("proteinLevel"); }
            set { hungerTree.SetFloat("proteinLevel", value); entity.WatchedAttributes.MarkPathDirty("hunger"); }
        }

        public float GrainLevel
        {
            get { return hungerTree.GetFloat("grainLevel"); }
            set { hungerTree.SetFloat("grainLevel", value); entity.WatchedAttributes.MarkPathDirty("hunger"); }
        }

        public float DairyLevel
        {
            get { return hungerTree.GetFloat("dairyLevel"); }
            set { hungerTree.SetFloat("dairyLevel", value); entity.WatchedAttributes.MarkPathDirty("hunger"); }
        }



        public EntityBehaviorHunger(Entity entity) : base(entity)
        {
            entityAgent = entity as EntityAgent;
        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            hungerTree = entity.WatchedAttributes.GetTreeAttribute("hunger");

            if (hungerTree == null)
            {
                entity.WatchedAttributes.SetAttribute("hunger", hungerTree = new TreeAttribute());

                Saturation = typeAttributes["currentsaturation"].AsFloat(1500);
                MaxSaturation = typeAttributes["maxsaturation"].AsFloat(1500);

                SaturationLossDelayFruit = 0;
                SaturationLossDelayVegetable = 0;
                SaturationLossDelayGrain = 0;
                SaturationLossDelayProtein = 0;
                SaturationLossDelayDairy = 0;

                FruitLevel = 0;
                VegetableLevel = 0;
                GrainLevel = 0;
                ProteinLevel = 0;
                DairyLevel = 0;
            }

            SatietyLossPerSecond = typeAttributes["satLossPerSec"].AsFloat(SatietyLossPerSecond);
            SatietyLossPerMovementSecond = typeAttributes["satLossPerMoveSec"].AsFloat(SatietyLossPerMovementSecond);
            SatietyLossPerSwimSecond = typeAttributes["satLossPerSwimSec"].AsFloat(SatietyLossPerSwimSecond);
            SatietyLossPerSprintSecond = typeAttributes["satLossPerSprintSec"].AsFloat(SatietyLossPerSprintSecond);
            SatietyLossPerJump = typeAttributes["satLossPerJump"].AsFloat(SatietyLossPerJump);

            listenerId = entity.World.RegisterGameTickListener(SlowTick, 6000);

            UpdateNutrientHealthBoost();
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);

            entity.World.UnregisterGameTickListener(listenerId);
        }

        public override void DidAttack(DamageSource source, EntityAgent targetEntity, ref EnumHandling handled)
        {
            ConsumeSaturation(3f);
        }

        public override void OnEntityJump()
        {
            movementPenalty += SatietyLossPerJump;
        }


        /// <summary>
        /// Consumes some of the entities saturation or shortens the delay before saturation is reduced
        /// </summary>
        /// <param name="amount"></param>
        public virtual void ConsumeSaturation(float amount)
        {
            ReduceSaturation(amount / 10f);
        }

        public override void OnEntityReceiveSaturation(float saturation, EnumFoodCategory foodCat = EnumFoodCategory.Unknown, float saturationLossDelay = 10, float nutritionGainMultiplier = 1f)
        {
            float maxsat = MaxSaturation;
            bool full = Saturation >= maxsat;

            Saturation = Math.Min(maxsat, Saturation + saturation);

            switch (foodCat)
            {
                case EnumFoodCategory.Fruit:
                    if (!full) FruitLevel = Math.Min(maxsat, FruitLevel + saturation / 2.5f * nutritionGainMultiplier);
                    SaturationLossDelayFruit = Math.Max(SaturationLossDelayFruit, saturationLossDelay);
                    break;

                case EnumFoodCategory.Vegetable:
                    if (!full) VegetableLevel = Math.Min(maxsat, VegetableLevel + saturation / 2.5f * nutritionGainMultiplier);
                    SaturationLossDelayVegetable = Math.Max(SaturationLossDelayVegetable, saturationLossDelay);
                    break;

                case EnumFoodCategory.Protein:
                    if (!full) ProteinLevel = Math.Min(maxsat, ProteinLevel + saturation / 2.5f * nutritionGainMultiplier);
                    SaturationLossDelayProtein = Math.Max(SaturationLossDelayProtein, saturationLossDelay);
                    break;

                case EnumFoodCategory.Grain:
                    if (!full) GrainLevel = Math.Min(maxsat, GrainLevel + saturation / 2.5f * nutritionGainMultiplier);
                    SaturationLossDelayGrain = Math.Max(SaturationLossDelayGrain, saturationLossDelay);
                    break;

                case EnumFoodCategory.Dairy:
                    if (!full) DairyLevel = Math.Min(maxsat, DairyLevel + saturation / 2.5f * nutritionGainMultiplier);
                    SaturationLossDelayDairy = Math.Max(SaturationLossDelayDairy, saturationLossDelay);
                    break;
            }

            UpdateNutrientHealthBoost();


        }

        public override void OnGameTick(float deltaTime)
        {
            detox(deltaTime);

            var world = entity.World;
            if (entity is EntityPlayer plr)
            {
                if (world.PlayerByUid(plr.PlayerUID).WorldData.CurrentGameMode is EnumGameMode.Creative or EnumGameMode.Spectator) return;
            }

            var gameSpeedMul = world.Calendar.SpeedOfTime * world.Calendar.CalendarSpeedMul / 30;

            var controls = entityAgent?.Controls;
            var mountedHungerProviders = entityAgent?.MountedOn?.Entity?.GetInterfaces<IMountedHungerProvider>();
            if (mountedHungerProviders == null && controls != null && !controls.FloorSitting)
            {
                if (controls.TriesToMove & controls.Sprint)
                {
                    // Check if the player is trying to sprint
                    movementPenalty += SatietyLossPerSprintSecond * deltaTime * gameSpeedMul;
                }
                else if (controls.TriesToMove || controls.Up || controls.Down)
                {
                    // If not, are they moving normal speed, factoring in whether they're swimming
                    if (entity.Swimming) movementPenalty += SatietyLossPerSwimSecond * deltaTime * gameSpeedMul;
                    else movementPenalty += SatietyLossPerMovementSecond * deltaTime * gameSpeedMul;
                }
            }

            if (mountedHungerProviders != null)
            {
                foreach (var provider in mountedHungerProviders)
                {
                    movementPenalty += provider.OnMountedHungerTick(entityAgent, deltaTime * gameSpeedMul);
                }
            }

            if (hungerCounter < 0)
            {
                hungerCounter = 0f; // Just in case the value is somehow negative, including from mods
                accumGameSpeedMul = 1f;
            }

            accumGameSpeedMul *= hungerCounter;            // We normalize the total time passed with the multiplier
            accumGameSpeedMul += deltaTime * gameSpeedMul; // Add the extra adjusted amount of time passed
            hungerCounter += deltaTime;                    // We add the actual delta time passed to the counter
            accumGameSpeedMul /= hungerCounter;            // And reduce the multiplier back down from the total seconds

            // Once every 10s when awake or 1s when players are sleeping through the night
            int secondsPassed = gameSpeedMul > 2 ? 1 : 10;
            if (hungerCounter > secondsPassed)
            {
                // First how much satiety to drain for the total seconds, given the accumulated game speed multiplier
                var satietyDrain = SatietyLossPerSecond * hungerCounter * accumGameSpeedMul;
                // Second we apply the total penalty we've gathered for all types of movement actions
                satietyDrain += movementPenalty;

                ReduceSaturation(satietyDrain / 10f); // Divided by 10 to offset the 10x multiplication inside the method

                hungerCounter = 0;
                movementPenalty = 0;
                accumGameSpeedMul = 1;
            }
        }

        float detoxCounter = 0f;
        float detoxAccumGameSpeedMul = 0;

        private void detox(float dt)
        {
            var gameSpeedMul = entity.Api.World.Calendar.SpeedOfTime * entity.Api.World.Calendar.CalendarSpeedMul / 30;
            detoxAccumGameSpeedMul *= detoxCounter;      // We normalize the total time passed with the multiplier
            detoxAccumGameSpeedMul += dt * gameSpeedMul; // Add the extra adjusted amount of time passed
            detoxCounter += dt;                     // We add the actual delta time passed to the counter
            detoxAccumGameSpeedMul /= detoxCounter;      // And reduce the multiplier back down from the total seconds

            if (detoxCounter > 1)
            {
                if (detoxAccumGameSpeedMul == 0)  detoxAccumGameSpeedMul = 1; // To make sure sobering up still happens with time paused

                float intox = entity.WatchedAttributes.GetFloat("intoxication");
                if (intox > 0)
                {
                    entity.WatchedAttributes.SetFloat("intoxication", Math.Max(0, intox - 0.005f * detoxAccumGameSpeedMul));
                }

                float psyche = entity.WatchedAttributes.GetFloat("psychedelic");
                if (psyche > 0)
                {
                    entity.WatchedAttributes.SetFloat("psychedelic", Math.Max(0, psyche - 0.005f * detoxAccumGameSpeedMul));
                }

                detoxCounter = 0;
                detoxAccumGameSpeedMul = 1;
            }
        }

        private bool ReduceSaturation(float satLossMultiplier)
        {
            bool isondelay = false;

            satLossMultiplier *= GlobalConstants.HungerSpeedModifier;
            satLossMultiplier *= entity.Stats.GetBlended("hungerrate");

            if (SaturationLossDelayFruit > 0)
            {
                SaturationLossDelayFruit -= 10 * satLossMultiplier;
                isondelay = true;
            }
            else
            {
                FruitLevel = Math.Max(0, FruitLevel - Math.Max(0.5f, 0.001f * FruitLevel) * satLossMultiplier * 0.25f);
            }

            if (SaturationLossDelayVegetable > 0)
            {
                SaturationLossDelayVegetable -= 10 * satLossMultiplier;
                isondelay = true;
            }
            else
            {
                VegetableLevel = Math.Max(0, VegetableLevel - Math.Max(0.5f, 0.001f * VegetableLevel) * satLossMultiplier * 0.25f);
            }

            if (SaturationLossDelayProtein > 0)
            {
                SaturationLossDelayProtein -= 10 * satLossMultiplier;
                isondelay = true;
            }
            else
            {
                ProteinLevel = Math.Max(0, ProteinLevel - Math.Max(0.5f, 0.001f * ProteinLevel) * satLossMultiplier * 0.25f);
            }

            if (SaturationLossDelayGrain > 0)
            {
                SaturationLossDelayGrain -= 10 * satLossMultiplier;
                isondelay = true;
            }
            else
            {
                GrainLevel = Math.Max(0, GrainLevel - Math.Max(0.5f, 0.001f * GrainLevel) * satLossMultiplier * 0.25f);
            }

            if (SaturationLossDelayDairy > 0)
            {
                SaturationLossDelayDairy -= 10 * satLossMultiplier;
                isondelay = true;
            }
            else
            {
                DairyLevel = Math.Max(0, DairyLevel - Math.Max(0.5f, 0.001f * DairyLevel) * satLossMultiplier * 0.25f / 2);
            }

            UpdateNutrientHealthBoost();

            if (isondelay) return true;

            float prevSaturation = Saturation;

            if (prevSaturation > 0)
            {
                Saturation = Math.Max(0, prevSaturation - satLossMultiplier * 10);
            }

            return false;
        }



        public void UpdateNutrientHealthBoost()
        {
            float fruitRel = FruitLevel / MaxSaturation;
            float grainRel = GrainLevel / MaxSaturation;
            float vegetableRel = VegetableLevel / MaxSaturation;
            float proteinRel = ProteinLevel / MaxSaturation;
            float dairyRel = DairyLevel / MaxSaturation;

            EntityBehaviorHealth bh = entity.GetBehavior<EntityBehaviorHealth>();

            float healthGain = 2.5f * (fruitRel + grainRel + vegetableRel + proteinRel + dairyRel);

            bh.SetMaxHealthModifiers("nutrientHealthMod", healthGain);
        }



        private void SlowTick(float dt)
        {
            if (entity is EntityPlayer)
            {
                EntityPlayer plr = (EntityPlayer)entity;
                if (entity.World.PlayerByUid(plr.PlayerUID).WorldData.CurrentGameMode == EnumGameMode.Creative) return;
            }

            bool harshWinters = entity.World.Config.GetString("harshWinters").ToBool(true);

            float temperature = entity.World.BlockAccessor.GetClimateAt(entity.Pos.AsBlockPos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, entity.World.Calendar.TotalDays).Temperature;
            if (temperature >= 2 || !harshWinters)
            {
                entity.Stats.Remove("hungerrate", "resistcold");
            }
            else
            {
                float diff = GameMath.Clamp(2 - temperature, 0, 10);

                Room room = entity.World.Api.ModLoader.GetModSystem<RoomRegistry>().GetRoomForPosition(entity.Pos.AsBlockPos);

                entity.Stats.Set("hungerrate", "resistcold", room.ExitCount == 0 ? 0 : diff / 40f, true);
            }


            if (Saturation <= 0)
            {
                entity.ReceiveDamage(new DamageSource() { Source = EnumDamageSource.Internal, Type = EnumDamageType.Hunger }, 0.125f);
            }
        }

        public override string PropertyName()
        {
            return "hunger";
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (damageSource.Type != EnumDamageType.Heal || damageSource.Source != EnumDamageSource.Revive) return;

            if (entity.Attributes.GetBool("noSatietyRestoreOnRevive"))
            {
                entity.Attributes.RemoveAttribute("noSatietyRestoreOnRevive");
                return;
            }

            SaturationLossDelayFruit = 60;
            SaturationLossDelayVegetable = 60;
            SaturationLossDelayProtein = 60;
            SaturationLossDelayGrain = 60;
            SaturationLossDelayDairy = 60;

            float satietyRate = entity.World.Config.GetString("playerReviveSatiety").ToFloat(0.5f);
            Saturation = MaxSaturation * satietyRate;
            VegetableLevel *= satietyRate;
            ProteinLevel *= satietyRate;
            FruitLevel *= satietyRate;
            DairyLevel *= satietyRate;
            GrainLevel *= satietyRate;
        }
    }

}
