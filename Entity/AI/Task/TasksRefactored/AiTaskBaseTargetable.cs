using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using VSEssentialsMod.Entity.AI.Task;

namespace Vintagestory.GameContent;

/// <summary>
/// Base parameters off all ai tasks that can target an entity that can be specified in JSON.<br/>
/// Parameters' names are not case sensitive.<br/><br/>
/// 
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2:<br/>
/// - EntityTags default value: ["player"] => []<br/>
/// - creatureHostility: "off" => "NeverHostile"<br/>
/// - dont forget to set 'UseFearReductionFactor' to true for tasks that needs it<br/>
/// - SneakRangeReduction default value: 0.6 => 1.0<br/>
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskBaseTargetableConfig : AiTaskBaseConfig
{
    /// <summary>
    /// Entities are stored in two different lists: 'creatures' and 'inanimate'. This parameters defines which type of entity should be targeted. Case sensitive.
    /// </summary>
    [JsonProperty] public EnumEntitySearchType SearchType = EnumEntitySearchType.Creatures;

    /// <summary>
    /// Parameter used by other tasks to determine range around entity to look for targets in.
    /// </summary>
    [JsonProperty] public float SeekingRange = 25;

    /// <summary>
    /// Minimum creature weight to be considered as targetable. As a fraction of this creature weight. If less or equal to 0 this check is ignored.
    /// </summary>
    [JsonProperty] public float MinTargetWeight = 0;

    /// <summary>
    /// Maximum creature weight to be considered as targetable. As a fraction of this creature weight. If less or equal to 0 this check is ignored.
    /// </summary>
    [JsonProperty] public float MaxTargetWeight = 0;

    /// <summary>
    /// List of tags entity should have to be targetable.
    /// This list is split into groups of tags. By default entity should have all tags from at least one group to be targetable.
    /// If 'ReverseTagsCheck' parameter is set to true, entity should have at least one tag from each group to be targetable.
    /// </summary>
    [JsonProperty] private List<List<string>>? entityTags = [];

    /// <summary>
    /// List of tags entity should have to be not targetable.
    /// This list is split into groups of tags. By default entity should have all tags from at least one group to be not targetable.
    /// If 'ReverseSkipTagsCheck' parameter is set to true, entity should have at least one tag from each group to be not targetable.
    /// </summary>
    [JsonProperty] private List<List<string>>? skipEntityTags = [];

    /// <summary>
    /// If set to true will change how 'EntityTags' check works. See 'EntityTags' description.
    /// </summary>
    [JsonProperty] public bool ReverseTagsCheck = false;

    /// <summary>
    /// If set to true will change how 'SkipEntityCodes' check works. See 'SkipEntityCodes' description.
    /// </summary>
    [JsonProperty] public bool ReverseSkipTagsCheck = false;

    /// <summary>
    /// Serves as a list of exceptions from tags and weight checks. Applied to an entity if it failed this checks.'
    /// If no tags specified, 'player' will be added to this list, so dont forget to put an empty array for entityCodes.
    /// </summary>
    [JsonProperty] private string[]? entityCodes = [];

    /// <summary>
    /// Applied to all entities that passed all previous checks. If entity is in this list, it is not targetable.
    /// </summary>
    [JsonProperty] public AssetLocation[] SkipEntityCodes = [];

    /// <summary>
    /// Is used by other AI tasks inherited from this class.<br/>
    /// In fleeEntity task it affects detection range.<br/>
    /// In meleeAttack tasks it affects detection range and whether this entity is fully tamed and should attack player<br/>
    /// In seekEntity it affects whether this entity is fully tamed and should seek player
    /// </summary>
    [JsonProperty] public float TamingGenerations = 10;

    /// <summary>
    /// Is set to 'true' seeking range will be reduced proportionally to taming generation. At <see cref="TamingGenerations"/> this factor will be equal to 0.
    /// </summary>
    [JsonProperty] public bool UseFearReductionFactor = false;

    /// <summary>
    /// If set to false seeking range of this AI task won't be affected by 'animalSeekingRange' player stat.
    /// </summary>
    [JsonProperty] public bool SeekingRangeAffectedByPlayerStat = true;

    /// <summary>
    /// Determines how much detection range will be reduced when targeted player is sneaking.
    /// </summary>
    [JsonProperty] public float SneakRangeReduction = 1.0f;

    /// <summary>
    /// If specified this emotion state will be triggered when the target is selected.
    /// </summary>
    [JsonProperty] public string? TriggerEmotionState = null;

    /// <summary>
    /// If 'FriendlyTarget' is set to false and 'AggressiveTargeting' is set to true for this AI task type, this parameter will determine if creature will target players.<br/>
    /// if set to 'neverHostile' players wont be targeted.<br/>
    /// if set to 'passive' player will be targeted only if creature is in 'aggressiveondamage' or 'aggressivearoundentities' emotion state.<br/>
    /// Case sensitive.
    /// </summary>
    [JsonProperty] public EnumCreatureHostility CreatureHostility = EnumCreatureHostility.Aggressive;

    /// <summary>
    /// If set to false dead entities will also be targeted
    /// </summary>
    [JsonProperty] public bool IgnoreDeadEntities = true;

    /// <summary>
    /// Affects 'CreatureHostility' parameter. If set to true creature hostility check will be ignored.
    /// </summary>
    [JsonProperty] public bool FriendlyTarget = false;

    /// <summary>
    /// Is used by other AI tasks inherited from this class.<br/>
    /// Affects whether task will target entity that was the last to deal damage to this entity.
    /// </summary>
    [JsonProperty] public bool RetaliateAttacks = true;

    /// <summary>
    /// If set to true, only entities with same herd id will be targeted. If this entity's herd id is '0', herd id check will be ignored. Creatures spawned using items from creative menu always have herd id equal to 0.
    /// </summary>
    [JsonProperty] public bool TargetEntitiesWithSameHerdId = false;

    /// <summary>
    /// If set to true, only entities with different herd id will be targeted. If this entity's herd id is '0', herd id check will be ignored. Creatures spawned using items from creative menu always have herd id equal to 0.
    /// </summary>
    [JsonProperty] public bool TargetEntitiesWithDifferentHerdId = false;

    /// <summary>
    /// If set to true all entities with 'IsInteractable' flag set to false will be ignored.
    /// </summary>
    [JsonProperty] public bool TargetOnlyInteractableEntities = true;

    /// <summary>
    /// Defines how light level of a target affects detection range. If value is [0,0,32,32] or unset then check is ignored.<br/>
    /// Should consist of 4 integer numbers, each next should be greater or equal to the previous, should be greater or equal to 0 and less or equal to 32.<br/>
    /// If light level is less or equal to 1st number or higher or equal to 4th, then detection range will be 0, so target wont be detected.<br/>
    /// Between 1st and 2nd detection range will rise linearly from 0 to maximum detection range.<br/>
    /// Between 2nd and 3rd including detection range will be at maximum.<br/>
    /// Between 3rd and 4th detection range will decrease linearly from maximum to 0.<br/>
    /// </summary>
    [JsonProperty] public int[] TargetLightLevels = [0, 0, 32, 32];

    /// <summary>
    /// Light level type that will be used for <see cref="TargetLightLevels"/>. See <see cref="EnumLightLevelType"/> for description of each light level type. Case sensitive.<br/>
    /// </summary>
    [JsonProperty] public EnumLightLevelType TargetLightLevelType = EnumLightLevelType.MaxTimeOfDayLight;

    /// <summary>
    /// Cooldown for target search check. This check is expensive, so it is better to have a cooldown for better performance.<br/>
    /// This cooldown results in some delay though.<br/>
    /// Not all targetable tasks use this cooldown.
    /// </summary>
    [JsonProperty] public int TargetSearchCooldownMs = 2000;

    /// <summary>
    /// If set to true, game mode check will be ignored.
    /// </summary>
    [JsonProperty] public bool TargetPlayerInAllGameModes = false;


    public EntityTagRule[] EntityTags = [];

    public EntityTagRule[] SkipEntityTags = [];

    public string[] TargetEntityCodesBeginsWith = [];

    public string[] TargetEntityCodesExact = [];

    public string TargetEntityFirstLetters = "";



    public bool NoEntityCodes => TargetEntityCodesExact.Length == 0 && TargetEntityCodesBeginsWith.Length == 0;
    public bool NoTags => EntityTags.Length == 0 && SkipEntityTags.Length == 0;
    public bool TargetEverything => NoEntityCodes && NoTags && NoEntityWeight;
    public bool NoEntityWeight => MaxTargetWeight <= 0 && MinTargetWeight <= 0;
    public bool IgnoreTargetLightLevel => TargetLightLevels[0] == 0 && TargetLightLevels[1] == 0 && TargetLightLevels[2] == maxLightLevel && TargetLightLevels[3] == maxLightLevel;



    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        if (entityTags != null)
        {
            EntityTags = [.. entityTags.Select(tagList => new EntityTagRule(entity.Api, tagList))];
            entityTags = null;
        }
        if (skipEntityTags != null)
        {
            SkipEntityTags = [.. skipEntityTags.Select(tagList => new EntityTagRule(entity.Api, tagList))];
            skipEntityTags = null;
        }
        if (entityCodes != null)
        {
            InitializeTargetCodes(entityCodes, out TargetEntityCodesExact, out TargetEntityCodesBeginsWith, out TargetEntityFirstLetters);
            entityCodes = null;
        }

        if (TargetLightLevels.Length != 4)
        {
            entity.Api.Logger.Error($"Invalid 'targetLightLevels' value (array length: {TargetLightLevels.Length}, should be 4) in AI task '{Code}' for entity '{entity.Code}'");
            throw new ArgumentException($"Invalid 'targetLightLevels' value (array length: {TargetLightLevels.Length}, should be 4) in AI task '{Code}' for entity '{entity.Code}'");
        }

        if (TargetLightLevels[0] > TargetLightLevels[1] || TargetLightLevels[1] > TargetLightLevels[2] || TargetLightLevels[2] > TargetLightLevels[3])
        {
            entity.Api.Logger.Error($"Invalid 'targetLightLevels' value: [{TargetLightLevels[0]},{TargetLightLevels[1]},{TargetLightLevels[2]},{TargetLightLevels[3]}], in AI task '{Code}' for entity '{entity.Code}'");
            throw new ArgumentException($"Invalid 'targetLightLevels' value: [{TargetLightLevels[0]},{TargetLightLevels[1]},{TargetLightLevels[2]},{TargetLightLevels[3]}], in AI task '{Code}' for entity '{entity.Code}'");
        }
    }

    /// <summary>
    /// Makes a similar system - "target codes from an array of entity codes with or without wildcards" - available to any other game element which requires it
    /// </summary>
    /// <param name="codes"></param>
    /// <param name="targetEntityCodesExact"></param>
    /// <param name="targetEntityCodesBeginsWith"></param>
    /// <param name="targetEntityFirstLetters"></param>
    protected static void InitializeTargetCodes(string[] codes, out string[] targetEntityCodesExact, out string[] targetEntityCodesBeginsWith, out string targetEntityFirstLetters) // @TODO move into some utils class
    {
        List<string> targetEntityCodesList = [];
        List<string> beginsWith = [];
        targetEntityFirstLetters = "";

        for (int i = 0; i < codes.Length; i++)
        {
            string code = codes[i];
            if (code.EndsWith('*'))
            {
                beginsWith.Add(code[..^1]);
            }
            else
            {
                targetEntityCodesList.Add(code);
            }
        }

        targetEntityCodesBeginsWith = [.. beginsWith];

        targetEntityCodesExact = new string[targetEntityCodesList.Count];
        int exactEntityCodeIndex = 0;
        foreach (string code in targetEntityCodesList)
        {
            if (code.Length == 0) continue;

            targetEntityCodesExact[exactEntityCodeIndex++] = code;

            char firstLetter = code[0];
            if (targetEntityFirstLetters.IndexOf(firstLetter) < 0)
            {
                targetEntityFirstLetters += firstLetter;
            }
        }

        foreach (string code in targetEntityCodesBeginsWith)
        {
            if (code.Length == 0)
            {
                targetEntityFirstLetters = ""; // code.Length zero indicates universal wildcard "*", therefore IsTargetableEntity should match everything - used by BeeMob for example
                break;
            }

            char firstLetter = code[0];
            if (targetEntityFirstLetters.IndexOf(firstLetter) < 0)
            {
                targetEntityFirstLetters += firstLetter;
            }
        }
    }
}

public abstract class AiTaskBaseTargetableR : AiTaskBaseR, IWorldIntersectionSupplier
{
    public Entity? TargetEntity => targetEntity;
    public virtual bool AggressiveTargeting => true;

    #region IWorldIntersectionSupplier
    // Is used in hasDirectContact()
    public Vec3i MapSize => entity.World.BlockAccessor.MapSize;
    public Block GetBlock(BlockPos pos) => entity.World.BlockAccessor.GetBlock(pos);
    public Cuboidf[] GetBlockIntersectionBoxes(BlockPos pos) => entity.World.BlockAccessor.GetBlock(pos).GetCollisionBoxes(entity.World.BlockAccessor, pos);
    public IBlockAccessor blockAccessor => entity.World.BlockAccessor;
    public bool IsValidPos(BlockPos pos) => entity.World.BlockAccessor.IsValidPos(pos);
    public Entity[] GetEntitiesAround(Vec3d position, float horRange, float vertRange, ActionConsumable<Entity>? matches = null) => [];
    #endregion

    protected bool RecentlySearchedForTarget => entity.World.ElapsedMilliseconds - lastTargetSearchMs < Config.TargetSearchCooldownMs;
    protected virtual string[] HostileEmotionStates => ["aggressiveondamage", "aggressivearoundentities"];


    private AiTaskBaseTargetableConfig Config => GetConfig<AiTaskBaseTargetableConfig>();

    protected Entity? targetEntity;
    protected long lastTargetSearchMs;

    #region Variables to reduce heap allocations because we dont use structs
    private BlockSelection blockSel = new();
    private EntitySelection entitySel = new();
    private readonly Vec3d rayTraceFrom = new();
    private readonly Vec3d rayTraceTo = new();
    private readonly Vec3d tmpPos = new();
    #endregion


    /// <summary>
    /// Is used exclusively to set <see cref="stepHeight"/>
    /// </summary>
    protected EntityBehaviorControlledPhysics? physicsBehavior;
    /// <summary>
    /// Is used by fleeEntity and stayInRange ai tasks, but is set for everyone
    /// </summary>
    protected float stepHeight;


    protected AiTaskBaseTargetableR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskBaseTargetableConfig>(entity, taskConfig, aiConfig);

        lastTargetSearchMs = entity.World.ElapsedMilliseconds - Rand.Next(Config.TargetSearchCooldownMs); // spreading first check after loading the world
    }

    protected AiTaskBaseTargetableR(EntityAgent entity) : base(entity)
    {
        partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>() ?? throw new ArgumentException("EntityPartitioning mod system is not found");

        AiTaskRegistry.TaskCodes.TryGetValue(GetType(), out string? codeFromType);

        baseConfig = new AiTaskBaseTargetableConfig();
        baseConfig.Code = baseConfig.Code == "" ? codeFromType ?? "" : baseConfig.Code;
        baseConfig.Init(entity);
    }

    public override void AfterInitialize()
    {
        base.AfterInitialize();
        physicsBehavior = entity.GetBehavior<EntityBehaviorControlledPhysics>();
    }

    public override void StartExecute()
    {
        stepHeight = physicsBehavior?.StepHeight ?? 0.6f;

        base.StartExecute();

        if (Config.TriggerEmotionState != null)
        {
            entity.GetBehavior<EntityBehaviorEmotionStates>()?.TryTriggerState(Config.TriggerEmotionState, 1, targetEntity?.EntityId ?? 0);
        }
    }

    public virtual void ClearAttacker()
    {
        attackedByEntity = null;
        attackedByEntityMs = -2 * Config.RecentlyAttackedTimeoutMs;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="target"></param>
    /// <param name="range"></param>
    /// <param name="ignoreEntityCode">Actually ignores also weight and tags filters</param>
    /// <returns></returns>
    protected virtual bool IsTargetableEntity(Entity target, float range)
    {
        if (!target.Alive && Config.IgnoreDeadEntities) return false;

        if (!CheckTargetHerdId(target)) return false;

        if (Config.TargetEverything) return CanSense(target, range);

        if (CheckTargetWeight(target.Properties.Weight) && CheckTargetTags(target.Tags))
        {
            return CanSense(target, range);
        }
        else if (CheckTargetCodes(target.Code.Path)) // entity code check serve as a list of exceptions from other checks to include entities filtered out from previous checks
        {
            return CanSense(target, range);
        }

        return false;
    }

    protected virtual bool CheckTargetHerdId(Entity target)
    {
        if (Config.TargetEntitiesWithSameHerdId && entity is EntityAgent entityAgent1 && target is EntityAgent targetEntityAgent1 && entityAgent1.HerdId != 0 && entityAgent1.HerdId != targetEntityAgent1.HerdId)
            return false;

        if (Config.TargetEntitiesWithDifferentHerdId && entity is EntityAgent entityAgent2 && target is EntityAgent targetEntityAgent2 && entityAgent2.HerdId != 0 && entityAgent2.HerdId == targetEntityAgent2.HerdId)
            return false;

        return true;
    }

    protected virtual bool CheckTargetWeight(float weight)
    {
        float weightFraction = entity.Properties.Weight > 0 ? weight / entity.Properties.Weight : float.MaxValue;
        if (Config.MinTargetWeight > 0 && weightFraction < Config.MinTargetWeight) return false;
        if (Config.MaxTargetWeight > 0 && weightFraction > Config.MaxTargetWeight) return false;
        return true;
    }

    protected virtual bool CheckTargetTags(EntityTagArray tags)
    {
        if (Config.NoTags) return false;

        if (!Config.ReverseTagsCheck)
        {
            if (EntityTagRule.IntersectsWithEach(tags, Config.EntityTags))
            {
                if (Config.SkipEntityTags.Length == 0) return true;

                if (!Config.ReverseSkipTagsCheck)
                {
                    if (!EntityTagRule.IntersectsWithEach(tags, Config.SkipEntityTags)) return true;
                }
                else
                {
                    if (!EntityTagRule.ContainsAllFromAtLeastOne(tags, Config.SkipEntityTags)) return true;
                }
            }
        }
        else
        {
            if (EntityTagRule.ContainsAllFromAtLeastOne(tags, Config.EntityTags))
            {
                if (Config.SkipEntityTags.Length == 0) return true;

                if (!Config.ReverseSkipTagsCheck)
                {
                    if (!EntityTagRule.IntersectsWithEach(tags, Config.SkipEntityTags)) return true;
                }
                else
                {
                    if (!EntityTagRule.ContainsAllFromAtLeastOne(tags, Config.SkipEntityTags)) return true;
                }
            }
        }

        return false;
    }

    protected virtual bool CheckTargetCodes(string testPath)
    {
        if (Config.TargetEntityFirstLetters.Length == 0) return false;
        if (Config.TargetEntityFirstLetters.IndexOf(testPath[0]) < 0) return false;

        for (int i = 0; i < Config.TargetEntityCodesExact.Length; i++)
        {
            if (testPath == Config.TargetEntityCodesExact[i]) return true;
        }

        for (int i = 0; i < Config.TargetEntityCodesBeginsWith.Length; i++)
        {
            if (testPath.StartsWithFast(Config.TargetEntityCodesBeginsWith[i])) return true;
        }

        return false;
    }

    protected virtual float GetTargetLightLevelRangeMultiplier(Entity target)
    {
        if (Config.IgnoreTargetLightLevel) return 1;

        int targetLightLevel = entity.World.BlockAccessor.GetLightLevel(target.Pos.AsBlockPos, Config.TargetLightLevelType);

        if (targetLightLevel <= Config.TargetLightLevels[0] || targetLightLevel >= Config.TargetLightLevels[3]) return 0;

        if (targetLightLevel >= Config.TargetLightLevels[1] && targetLightLevel <= Config.TargetLightLevels[2]) return 1;

        if (targetLightLevel <= Config.TargetLightLevels[1] && Config.TargetLightLevels[0] != Config.TargetLightLevels[1])
        {
            return (targetLightLevel - Config.TargetLightLevels[0]) / (float)(Config.TargetLightLevels[1] - Config.TargetLightLevels[0]);
        }

        if (targetLightLevel >= Config.TargetLightLevels[2] && Config.TargetLightLevels[2] != Config.TargetLightLevels[3])
        {
            return 1f - (targetLightLevel - Config.TargetLightLevels[2]) / (Config.TargetLightLevels[3] - Config.TargetLightLevels[2]);
        }

        return 1;
    }

    protected virtual bool CanSense(Entity target, double range)
    {
        if (!target.Alive && Config.IgnoreDeadEntities) return false; // duplication of the check from IsTargetableEntity, needed when only CanSense is used without IsTargetableEntity
        if (target.EntityId == entity.EntityId || !target.IsInteractable && Config.TargetOnlyInteractableEntities) return false;

        if (Config.SkipEntityCodes.Length != 0)
        {
            for (int i = 0; i < Config.SkipEntityCodes.Length; i++)
            {
                if (WildcardUtil.Match(Config.SkipEntityCodes[i], target.Code)) return false;
            }
        }

        if (target is EntityPlayer entityPlayer && !CanSensePlayer(entityPlayer, range)) return false;

        if (!CheckDetectionRange(target, range)) return false;

        return true;
    }

    protected virtual bool CanSensePlayer(EntityPlayer target, double range)
    {
        if (!CheckEntityHostility(target)) return false;

        if (!TargetablePlayerMode(target)) return false;

        return true;
    }

    protected virtual bool CheckEntityHostility(EntityPlayer target)
    {
        if (!Config.FriendlyTarget && AggressiveTargeting)
        {
            return Config.CreatureHostility switch
            {
                EnumCreatureHostility.Aggressive => true,
                EnumCreatureHostility.Passive => emotionStatesBehavior == null || !IsInEmotionState(HostileEmotionStates),
                EnumCreatureHostility.NeverHostile => false,
                _ => false
            };
        }

        return true;
    }

    protected virtual bool CheckDetectionRange(Entity target, double range)
    {
        if (entity.ServerPos.Dimension != target.Pos.Dimension) return false;

        float rangeMultiplier = GetDetectionRangeMultiplier(target);
        
        if (rangeMultiplier <= 0)
        {
            return false;
        }
        
        if (rangeMultiplier != 1 && entity.ServerPos.DistanceTo(target.Pos) > range * rangeMultiplier)
        {
            return false;
        }

        return true;
    }

    protected virtual float GetDetectionRangeMultiplier(Entity target)
    {
        float rangeMultiplier = 1;

        if (!Config.IgnoreTargetLightLevel)
        {
            rangeMultiplier *= GetTargetLightLevelRangeMultiplier(target);
        }

        if (target is EntityAgent entityAgent && entityAgent.Controls.Sneak && target.OnGround && target.OnGround)
        {
            rangeMultiplier *= Config.SneakRangeReduction;
        }

        if (Config.SeekingRangeAffectedByPlayerStat && target is EntityPlayer)
        {
            rangeMultiplier *= target.Stats.GetBlended("animalSeekingRange");
        }

        return rangeMultiplier;
    }

    protected virtual bool TargetablePlayerMode(EntityPlayer target)
    {
        if (Config.TargetPlayerInAllGameModes) return true;

        return target.Player is not IServerPlayer player ||
            player.WorldData.CurrentGameMode != EnumGameMode.Creative &&
            player.WorldData.CurrentGameMode != EnumGameMode.Spectator &&
            player is IServerPlayer { ConnectionState: EnumClientState.Playing };
    }

    protected virtual Entity? GetGuardedEntity()
    {
        string? uid = entity.WatchedAttributes.GetString("guardedPlayerUid");
        if (uid != null)
        {
            return entity.World.PlayerByUid(uid)?.Entity;
        }

        long id = entity.WatchedAttributes.GetLong("guardedEntityId");
        if (id != 0)
        {
            entity.World.GetEntityById(id);
        }

        return null;
    }

    protected virtual bool IsNonAttackingPlayer(Entity target)
    {
        return (attackedByEntity == null || attackedByEntity.EntityId != target.EntityId) && target is EntityPlayer;
    }

    protected virtual bool HasDirectContact(Entity targetEntity, float maxDistance, float maxVerticalDistance)
    {
        if (targetEntity.Pos.Dimension != entity.Pos.Dimension) return false;

        Cuboidd targetBox = targetEntity.SelectionBox.ToDouble().Translate(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
        tmpPos.Set(entity.ServerPos).Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.ServerPos.Yaw);
        double distance = targetBox.ShortestDistanceFrom(tmpPos);
        double vertDistance = Math.Abs(targetBox.ShortestVerticalDistanceFrom(tmpPos.Y));
        
        if (distance >= maxDistance || vertDistance >= maxVerticalDistance) return false;

        rayTraceFrom.Set(entity.ServerPos);
        rayTraceFrom.Y += 1 / 32f;
        rayTraceTo.Set(targetEntity.ServerPos);
        rayTraceTo.Y += 1 / 32f;
        bool directContact = false;

        entity.World.RayTraceForSelection(this, rayTraceFrom, rayTraceTo, ref blockSel, ref entitySel);
        directContact = blockSel == null;

        if (!directContact)
        {
            rayTraceFrom.Y += entity.SelectionBox.Y2 * 7 / 16f;
            rayTraceTo.Y += targetEntity.SelectionBox.Y2 * 7 / 16f;
            entity.World.RayTraceForSelection(this, rayTraceFrom, rayTraceTo, ref blockSel, ref entitySel);
            directContact = blockSel == null;
        }

        if (!directContact)
        {
            rayTraceFrom.Y += entity.SelectionBox.Y2 * 7 / 16f;
            rayTraceTo.Y += targetEntity.SelectionBox.Y2 * 7 / 16f;
            entity.World.RayTraceForSelection(this, rayTraceFrom, rayTraceTo, ref blockSel, ref entitySel);
            directContact = blockSel == null;
        }

        if (!directContact) return false;

        return true;
    }

    protected virtual float GetFearReductionFactor()
    {
        return Config.UseFearReductionFactor ? Math.Max(0f, (Config.TamingGenerations - GetOwnGeneration()) / Config.TamingGenerations) : 1f;
    }

    protected virtual bool SearchForTarget()
    {
        float seekingRange = GetSeekingRange();

        targetEntity = partitionUtil.GetNearestEntity(entity.ServerPos.XYZ, seekingRange, entity => IsTargetableEntity(entity, seekingRange), Config.SearchType);

        return targetEntity != null;
    }

    protected virtual float GetSeekingRange()
    {
        float fearReductionFactor = GetFearReductionFactor();

        return Config.SeekingRange * fearReductionFactor;
    }

    protected virtual float GetAverageSize(Entity target)
    {
        return target.SelectionBox.XSize / 2 + entity.SelectionBox.XSize / 2;
    }

    protected virtual bool CheckAndResetSearchCooldown()
    {
        if (RecentlySearchedForTarget) return false;

        lastTargetSearchMs = entity.World.ElapsedMilliseconds;

        return true;
    }

    protected virtual bool ShouldRetaliate()
    {
        return Config.RetaliateAttacks &&
            attackedByEntity != null &&
            attackedByEntity.Alive &&
            attackedByEntity.IsInteractable &&
            CanSense(attackedByEntity, Config.SeekingRange) &&
            !entity.ToleratesDamageFrom(attackedByEntity);
    }

    protected virtual float MinDistanceToTarget(float extraDistance = 0)
    {
        return entity.SelectionBox.XSize / 2 + (targetEntity?.SelectionBox.XSize ?? 0) / 2 + extraDistance;
    }
}
