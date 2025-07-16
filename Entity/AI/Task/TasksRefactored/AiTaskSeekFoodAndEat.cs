using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Entity will try to find loose items or points of interest that it can eat, and eat them when in reach.
///
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2:<br/>
/// - executionChance default value: 0.995 => 1.0<br/>
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskSeekFoodAndEatConfig : AiTaskBaseConfig
{
    /// <summary>
    /// Entity moving speed.
    /// </summary>
    [JsonProperty] public float MoveSpeed = 0.02f;

    /// <summary>
    /// Minimum distance to target is a maximum of this value and half of entity selection box.<br/>
    /// Minimum distance is used to determine if entity reached its target.
    /// </summary>
    [JsonProperty] public float ExtraTargetDistance = 0.6f;

    /// <summary>
    /// Cooldown between search attempts for points of interest.<br/>
    /// </summary>
    [JsonProperty] public int PoiSearchCooldown = 15000;

    /// <summary>
    /// Chance for entity to seek food source without eating it.
    /// </summary>
    [JsonProperty] public float ChanceToSeekFoodWithoutEating = 0.004f;

    /// <summary>
    /// Should entity try eat loose items
    /// </summary>
    [JsonProperty] public bool EatLooseItems = true;

    /// <summary>
    /// Should entity try eat point of interest based food sources.
    /// </summary>
    [JsonProperty] public bool EatFoodSources = true;

    /// <summary>
    /// Path to sound to play when entity eat target, is prefixed by 'sounds/'.
    /// </summary>
    [JsonProperty] public AssetLocation? EatSound;

    /// <summary>
    /// Delay between entity starting to eat the target and sound played.
    /// </summary>
    [JsonProperty] public float EatTimeSoundSec = 1.125f;

    /// <summary>
    /// Time that takes entity to eat the target.
    /// </summary>
    [JsonProperty] public float EatTimeSec = 1.5f;

    /// <summary>
    /// Range of <see cref="EatSound"/>.
    /// </summary>
    [JsonProperty] public float EatSoundRange = 16;

    /// <summary>
    /// Volume of <see cref="EatSound"/>. Should be in 0 to 1 range.
    /// </summary>
    [JsonProperty] public float EatSoundVolume = 1;

    /// <summary>
    /// Entity will search for loose items in this range.
    /// </summary>
    [JsonProperty] public float LooseItemsSearchRange = 10;

    /// <summary>
    /// Entity will search for points of interest food sources in this range.
    /// </summary>
    [JsonProperty] public float PoiSearchRange = 48;

    /// <summary>
    /// Point of interest type to search for. Point of interest also should be 'IAnimalFoodSource', so changing this value might not work as intended.
    /// </summary>
    [JsonProperty] public string PoiType = "food";

    /// <summary>
    /// Cooldown before trying to attempt reaching point of interest, that entity failed to reach before.
    /// </summary>
    [JsonProperty] public int SeekPoiRetryCooldown = 60000;

    /// <summary>
    /// Maximum number of attempts to reach same point of interest
    /// </summary>
    [JsonProperty] public int SeekPoiMaxAttempts = 4;

    /// <summary>
    /// Determines pathfinding behavior, <see cref="EnumAICreatureType"/>. If not specified, value from the ai task behavior config will be used.
    /// </summary>
    [JsonProperty] public EnumAICreatureType? AiCreatureType = EnumAICreatureType.Default;

    /// <summary>
    /// Animation code for animation that is played when eating point of interest based food source.
    /// </summary>
    [JsonProperty] private string? eatAnimation = null;

    /// <summary>
    /// Animation speed of <see cref="EatAnimation"/>.
    /// </summary>
    [JsonProperty] private float eatAnimationSpeed = 1;

    /// <summary>
    /// Animation code for animation that is played when eating loose items.
    /// </summary>
    [JsonProperty] private string? eatAnimationLooseItems = null;

    /// <summary>
    /// Animation speed of <see cref="EatAnimationLooseItems"/>
    /// </summary>
    [JsonProperty] private float eatAnimationSpeedLooseItems = 1;

    /// <summary>
    /// If set to 'true' entity will consume portion from food source, replenish hunger and reset time for last eaten meal.<br/>
    /// </summary>
    [JsonProperty] public bool DoConsumePortion = true;

    /// <summary>
    /// Determines what entity can eat, <see cref="Diet"/>. Taken from entity attributes if not specified.
    /// </summary>
    [JsonProperty] public CreatureDiet? Diet;

    /// <summary>
    /// Saturation restored per portion eaten.
    /// </summary>
    [JsonProperty] public float SaturationPerPortion = 1f;



    public AnimationMetaData? EatAnimationMeta;

    public AnimationMetaData? EatAnimationMetaLooseItems;



    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        if (eatAnimation != null)
        {
            EatAnimationMeta = new AnimationMetaData()
            {
                Code = eatAnimation.ToLowerInvariant(),
                Animation = eatAnimation.ToLowerInvariant(),
                AnimationSpeed = eatAnimationSpeed
            }.Init();
        }

        if (eatAnimationLooseItems != null)
        {
            EatAnimationMetaLooseItems = new AnimationMetaData()
            {
                Code = eatAnimationLooseItems.ToLowerInvariant(),
                Animation = eatAnimationLooseItems.ToLowerInvariant(),
                AnimationSpeed = eatAnimationSpeedLooseItems
            }.Init();
        }

        Sound = Sound?.WithPathPrefix("sounds/");

        Diet ??= entity.Properties.Attributes["creatureDiet"].AsObject<CreatureDiet>();
        if (Diet == null)
        {
            entity.Api.Logger.Warning("Creature '" + entity.Code.ToShortString() + "' has SeekFoodAndEat task but no Diet specified.");
        }

        if (EatSound != null)
        {
            EatSound = EatSound.WithPathPrefixOnce("sounds/");
        }
    }
}

public class AiTaskSeekFoodAndEatR : AiTaskBaseR
{
    protected struct FailedAttempt
    {
        public long LastTryMs;
        public int Count;
    }

    private AiTaskSeekFoodAndEatConfig Config => GetConfig<AiTaskSeekFoodAndEatConfig>();


    protected long lastPOISearchTotalMs;
    protected long stuckAtMs = 0;
    protected bool stuck = false;
    protected float currentEatTime = 0;
    protected Dictionary<IAnimalFoodSource, FailedAttempt> failedSeekTargets = new();
    protected bool soundPlayed = false;
    protected bool eatAnimationStarted = false;
    protected float quantityEaten = 0;

    protected POIRegistry poiRegistry;
    protected IAnimalFoodSource? targetPoi;
    protected EntityBehaviorMultiplyBase? multiplyBaseBehavior;

    #region Variables to reduce heap allocations cause we dont use structs
    private readonly Cuboidd cuboidBuffer = new();
    #endregion


    public AiTaskSeekFoodAndEatR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        poiRegistry = entity.Api.ModLoader.GetModSystem<POIRegistry>() ?? throw new ArgumentException("Could not find POIRegistry modsystem");

        baseConfig = LoadConfig<AiTaskSeekFoodAndEatConfig>(entity, taskConfig, aiConfig);

        entity.WatchedAttributes.SetBool("doesEat", true);
    }

    public override void AfterInitialize()
    {
        base.AfterInitialize();
        multiplyBaseBehavior = entity.GetBehavior<EntityBehaviorMultiplyBase>();
    }

    public override bool ShouldExecute()
    {
        if (lastPOISearchTotalMs + Config.PoiSearchCooldown > entity.World.ElapsedMilliseconds) return false;

        if (!PreconditionsSatisficed()) return false;

        if (multiplyBaseBehavior != null && !multiplyBaseBehavior.ShouldEat && entity.World.Rand.NextDouble() >= Config.ChanceToSeekFoodWithoutEating) return false;

        targetPoi = null;
        lastPOISearchTotalMs = entity.World.ElapsedMilliseconds;

        if (Config.EatLooseItems)
        {
            partitionUtil.WalkEntities(entity.ServerPos.XYZ, Config.LooseItemsSearchRange, target =>
            {
                if (target is EntityItem itemEntity && SuitableFoodSource(itemEntity.Itemstack))
                {
                    targetPoi = new LooseItemFoodSource(itemEntity);
                    return false;   // Stop the walk when food found
                }

                return true;
            }, EnumEntitySearchType.Inanimate);
        }

        if (targetPoi == null && Config.EatFoodSources)
        {
            targetPoi = poiRegistry.GetNearestPoi(entity.ServerPos.XYZ, Config.PoiSearchRange, poi =>
            {
                if (poi.Type != Config.PoiType) return false;

                IAnimalFoodSource? foodPoi = poi as IAnimalFoodSource;

                if (foodPoi?.IsSuitableFor(entity, Config.Diet) == true)
                {
                    bool found = failedSeekTargets.TryGetValue(foodPoi, out FailedAttempt attempt);
                    if (!found || attempt.Count < Config.SeekPoiMaxAttempts || attempt.LastTryMs < world.ElapsedMilliseconds - Config.SeekPoiRetryCooldown)
                    {
                        return true;
                    }
                }

                return false;
            }) as IAnimalFoodSource;
        }

        return targetPoi != null;
    }

    public override void StartExecute()
    {
        if (targetPoi == null)
        {
            stopTask = true;
            return;
        }

        base.StartExecute();

        stuckAtMs = long.MinValue;
        stuck = false;
        soundPlayed = false;
        currentEatTime = 0;
        pathTraverser.NavigateTo_Async(targetPoi.Position, Config.MoveSpeed, MinDistanceToTarget() - 0.1f, OnGoalReached, OnStuck, null, 1000, 1, Config.AiCreatureType);
        eatAnimationStarted = false;
    }

    public override bool ContinueExecute(float dt)
    {
        if (!base.ContinueExecute(dt)) return false;

        if (targetPoi == null) return false;

        FastVec3d pos = new FastVec3d().Set(targetPoi.Position);
        pathTraverser.CurrentTarget.X = pos.X;
        pathTraverser.CurrentTarget.Y = pos.Y;
        pathTraverser.CurrentTarget.Z = pos.Z;

        cuboidBuffer.Set(entity.SelectionBox.ToDouble().Translate(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z));

        double distance = cuboidBuffer.ShortestDistanceFrom(pos.X, pos.Y, pos.Z);
        float minDistance = MinDistanceToTarget();

        if (distance <= minDistance)
        {
            if (!EatTheTarget(dt))
            {
                return false;
            }
        }
        else
        {
            if (!pathTraverser.Active)
            {
                float randomX = (float)Rand.NextDouble() * 0.3f - 0.15f;
                float randomZ = (float)Rand.NextDouble() * 0.3f - 0.15f;
                if (!pathTraverser.NavigateTo(targetPoi.Position.AddCopy(randomX, 0, randomZ), Config.MoveSpeed, minDistance - 0.15f, OnGoalReached, OnStuck, null, false, 500, 1, Config.AiCreatureType))
                {
                    return false;
                }
            }
        }

        if (stuck && entity.World.ElapsedMilliseconds > stuckAtMs + Config.EatTimeSec * 1000)
        {
            return false;
        }

        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        double longCooldown = cooldownUntilTotalHours;

        base.FinishExecute(cancelled);

        // don't call base method, we set the cool down manually
        // call base method anyway to do other stuff from it, but reset the cooldowns
        // Instead of resetting the cool down to current time + delta, we add it, so that the animal can eat multiple times, to catch up on lost time
        EntityBehaviorMultiply multiplyBehavior = entity.GetBehavior<EntityBehaviorMultiply>();
        if (multiplyBehavior != null && multiplyBehavior.PortionsLeftToEat > 0 && !multiplyBehavior.IsPregnant)
        {
            cooldownUntilTotalHours = longCooldown + Config.MinCooldownHours + entity.World.Rand.NextDouble() * (Config.MaxCooldownHours - Config.MinCooldownHours);
        }
        else
        {
            cooldownUntilTotalHours = entity.Api.World.Calendar.TotalHours + Config.MinCooldownHours + entity.World.Rand.NextDouble() * (Config.MaxCooldownHours - Config.MinCooldownHours);
        }

        pathTraverser.Stop();

        if (Config.EatAnimationMeta != null)
        {
            entity.AnimManager.StopAnimation(Config.EatAnimationMeta.Code);
        }

        if (cancelled)
        {
            cooldownUntilTotalHours = 0;
        }

        if (quantityEaten < 1)
        {
            cooldownUntilTotalHours = 0;
        }
        else
        {
            quantityEaten = 0;
        }
    }

    public override bool CanContinueExecute()
    {
        return pathTraverser.Ready;
    }

    protected virtual float MinDistanceToTarget()
    {
        return Math.Max(Config.ExtraTargetDistance, entity.SelectionBox.XSize / 2 + 0.05f);
    }

    protected virtual bool SuitableFoodSource(ItemStack itemStack) => Config.Diet?.Matches(itemStack) ?? true;

    protected virtual void OnStuck()
    {
        if (targetPoi == null) return;

        stuckAtMs = entity.World.ElapsedMilliseconds;
        stuck = true;

        failedSeekTargets.TryGetValue(targetPoi, out FailedAttempt attempt);
        attempt.Count++;
        attempt.LastTryMs = world.ElapsedMilliseconds;
        failedSeekTargets[targetPoi] = attempt;
    }

    protected virtual void OnGoalReached()
    {
        if (targetPoi == null) return;
        pathTraverser.Active = true;
        failedSeekTargets.Remove(targetPoi);
    }

    protected virtual bool EatTheTarget(float dt)
    {
        if (targetPoi == null) return false;

        pathTraverser.Stop();
        if (Config.AnimationMeta != null)
        {
            entity.AnimManager.StopAnimation(Config.AnimationMeta.Code);
        }

        if (multiplyBaseBehavior != null && !multiplyBaseBehavior.ShouldEat)
        {
            return false;
        }

        if (!targetPoi.IsSuitableFor(entity, Config.Diet)) return false;

        if (Config.EatAnimationMeta != null && !eatAnimationStarted)
        {
            entity.AnimManager.StartAnimation((targetPoi is LooseItemFoodSource && Config.EatAnimationMetaLooseItems != null) ? Config.EatAnimationMetaLooseItems : Config.EatAnimationMeta);

            eatAnimationStarted = true;
        }

        currentEatTime += dt;

        if (targetPoi is LooseItemFoodSource foodSource)
        {
            entity.World.SpawnCubeParticles(targetPoi.Position, foodSource.ItemStack, 0.25f, 1, 0.25f + 0.5f * (float)entity.World.Rand.NextDouble());
        }


        if (currentEatTime > Config.EatTimeSoundSec && !soundPlayed)
        {
            soundPlayed = true;
            if (Config.EatSound != null) entity.World.PlaySoundAt(Config.EatSound, entity, null, true, Config.EatSoundRange, Config.EatSoundVolume);
        }


        if (currentEatTime >= Config.EatTimeSec)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (tree == null) entity.WatchedAttributes["hunger"] = tree = new TreeAttribute();

            if (Config.DoConsumePortion)
            {
                float portion = targetPoi.ConsumeOnePortion(entity);
                float saturation = portion * Config.SaturationPerPortion;
                quantityEaten += portion;
                tree.SetFloat("saturation", saturation + tree.GetFloat("saturation", 0));
                entity.WatchedAttributes.SetDouble("lastMealEatenTotalHours", entity.World.Calendar.TotalHours);
                entity.WatchedAttributes.MarkPathDirty("hunger");
            }
            else
            {
                quantityEaten = 1;
            }

            failedSeekTargets.Remove(targetPoi);

            return false;
        }

        return true;
    }
}