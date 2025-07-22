using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Essentials;
using Vintagestory.GameContent;

namespace Vintagestory.API.Common;

/// <summary>
/// Base parameters of all AI tasks that can be specified in JSON.<br/>
/// Parameters names are not case sensitive.<br/>
/// <br/>
/// Changes 1.20.12 => 1.21.0-pre.3<br/>
/// - mincooldown => minCooldownMs<br/>
/// - maxcooldown => maxCooldownMs<br/>
/// - initialMinCoolDown => initialMinCooldownMs<br/>
/// - initialMaxCooldown => initialMaxCoolDownMs<br/>
/// - whenInEmotionState: string => string[]<br/>
/// - whenNotInEmotionState: string => string[]<br/>
/// - dayTimeFrameInaccuracy default value: 0.3 => 0<br/>
/// - executionChance default value: ~ => 0.1
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskBaseConfig
{
    /// <summary>
    /// Defines type of the task. Not used by tasks themselves.
    /// </summary>
    [JsonProperty] public string Code = "";

    /// <summary>
    /// If set to 'false' task wont be added to the entity and wont be instantiated. Not used by tasks themselves.
    /// </summary>
    [JsonProperty] public bool Enabled = true;

    /// <summary>
    /// Task identifier that is used to start tasks via commands, to start 'inactive' task for eidolon and to start task 'seekentity' for other herd members.
    /// </summary>
    [JsonProperty] public string Id = "";

    /// <summary>
    /// Tasks with higher priority will be executed first. For task to be executed instead of active task, its priority should be greater than active task <see cref="PriorityForCancel"/>.
    /// </summary>
    [JsonProperty] public float Priority = 0;

    /// <summary>
    /// For an active task to be replaced with new one, its 'PriorityForCancel' should be lower than new task <see cref="Priority"/>. By default is equal to <see cref="Priority"/>.
    /// </summary>
    [JsonProperty] private float? priorityForCancel = null;

    /// <summary>
    /// Is used to allow tasks to be executed in parallel. Up too <see cref="AiTaskManager.ActiveTasksBySlot"/> tasks (currently 8) can be executed at same time.<br/>
    /// Tasks with same slot number will replace each other, while tasks with different slot numbers can run side by side.
    /// </summary>
    [JsonProperty] public int Slot = 0;

    /// <summary>
    /// Minimal cooldown before task can be run again, in milliseconds.<br/>
    /// There are two types of cooldown: short and long. This parameters sets short cooldown.<br/>
    /// Long cooldown is set in hours and relies on <see cref="IWorldAccessor.Calendar.TotalHours"/>.
    /// </summary>
    [JsonProperty] public int MinCooldownMs = 0;

    /// <summary>
    /// Maximum cooldown before task can be run again, in milliseconds.<br/>
    /// There are two types of cooldown: short and long. This parameters sets short cooldown.<br/>
    /// Long cooldown is set in hours and relies on <see cref="IWorldAccessor.Calendar.TotalHours"/>.
    /// </summary>
    [JsonProperty] public int MaxCooldownMs = 100;

    /// <summary>
    /// Minimum cooldown before task can be run again, in hours.<br/>
    /// There are two types of cooldown: short and long. This parameters sets long cooldown.<br/>
    /// Long cooldown is set in hours and relies on <see cref="IWorldAccessor.Calendar.TotalHours"/>.
    /// </summary>
    [JsonProperty] public double MinCooldownHours = 0;

    /// <summary>
    /// Maximum cooldown before task can be run again, in hours.<br/>
    /// There are two types of cooldown: short and long. This parameters sets long cooldown.<br/>
    /// Long cooldown is set in hours and relies on <see cref="IWorldAccessor.Calendar.TotalHours"/>.
    /// </summary>
    [JsonProperty] public double MaxCooldownHours = 0;

    /// <summary>
    /// Minimum time that should pass before task can be executed after entity is initialized, in milliseconds
    /// </summary>
    [JsonProperty] public int InitialMinCooldownMs = 0;

    /// <summary>
    /// Minimum time that should pass before task can be executed after entity is initialized, in milliseconds
    /// </summary>
    [JsonProperty] public int InitialMaxCooldownMs = 0;

    /// <summary>
    /// List of entity tags that will be added to entity when task start.<br/>
    /// Tags that were actually added (that entity didn't have when task is started) will be removed when task finishes.
    /// </summary>
    [JsonProperty] private string[]? tagsAppliedToEntity = [];

    /// <summary>
    /// If set to 'true' task will be started only if entity is swimming.<br/>
    /// If set to 'false' task will be started only if entity is not swimming.<br/>
    /// If not set, this condition will be ignored.
    /// </summary>
    [JsonProperty] public bool? WhenSwimming = null;

    /// <summary>
    /// If set to 'true' task will be started only if entity's feet are in liquid.<br/>
    /// If set to 'false' task will be started only if entity's feet are in liquid.<br/>
    /// If not set, this condition will be ignored.
    /// </summary>
    [JsonProperty] public bool? WhenFeetInLiquid = null;

    /// <summary>
    /// Task will be started when entity is in one of these emotion states. If specified none, this condition will be ignored.
    /// </summary>
    [JsonProperty] public string[] WhenInEmotionState = [];

    /// <summary>
    /// Task will be started when entity is not in any of these emotion states. If specified none, this condition will be ignored.
    /// </summary>
    [JsonProperty] public string[] WhenNotInEmotionState = [];

    /// <summary>
    /// If set to an animation code, animation with code will be played at the start of the task, and stopped at the end.
    /// </summary>
    [JsonProperty] private string animation = "";

    /// <summary>
    /// Animation speed of <see cref="Animation"/>.
    /// </summary>
    [JsonProperty] private float? animationSpeed = null;

    /// <summary>
    /// If set to a path to a sound starting from sounds folder, sound will be played in <see cref="SoundsStartMs"/> milliseconds after task start.<br/>
    /// Sound will be repeated each <see cref="SoundRepeatMs"/> milliseconds if it is greater than 0.
    /// </summary>
    [JsonProperty] public AssetLocation? Sound = null;

    /// <summary>
    /// Sound range of the <see cref="Sound"/> and <see cref="FinishSound"/>.
    /// </summary>
    [JsonProperty] public float SoundRange = 16;

    /// <summary>
    /// Sound will be started in this amount of milliseconds after task starts.
    /// </summary>
    [JsonProperty] public int SoundStartMs = 0;

    /// <summary>
    /// If greater than 0, sound will be repeated each <see cref="SoundRepeatMs"/> amount of milliseconds.
    /// </summary>
    [JsonProperty] public int SoundRepeatMs = 0;

    /// <summary>
    /// Should pitch of <see cref="Sound"/> and <see cref="FinishSound"/> be randomized.
    /// </summary>
    [JsonProperty] public bool RandomizePitch = true;

    /// <summary>
    /// Volume of <see cref="Sound"/> and <see cref="FinishSound"/>. From 0 to 1. Cant be greater than 1 due to limitation of sound library that VS uses.
    /// </summary>
    [JsonProperty] public float SoundVolume = 1.0f;

    /// <summary>
    /// Chance to play <see cref="Sound"/>.
    /// </summary>
    [JsonProperty] public float SoundChance = 1.0f;

    /// <summary>
    /// If set to a path to a sound starting from sounds folder, sound will be played after the task is finished.
    /// </summary>
    [JsonProperty] public AssetLocation? FinishSound = null;

    /// <summary>
    /// If entity light level is outside of specified range, ai tasks wont be executed.<br/>
    /// Minimum light level is 0, maximum is 32.
    /// </summary>
    [JsonProperty] public int[] EntityLightLevels = [0, 32];

    /// <summary>
    /// Light level type that will be used for <see cref="EntityLightLevels"/>. See <see cref="EnumLightLevelType"/> for description of each light level type. Case sensitive.<br/>
    /// </summary>
    [JsonProperty] public EnumLightLevelType EntityLightLevelType = EnumLightLevelType.MaxTimeOfDayLight;

    /// <summary>
    /// A list of day-time-frames in which AI task can start. If no frames specified, day time is ignored.<br/>
    /// Each frame is a pair of day hours: [0.5, 2.9].<br/>
    /// In hours. Assumes 24 hours per day.
    /// </summary>
    [JsonProperty] private float[][]? duringDayTimeFramesHours = [];

    /// <summary>
    /// A random value from [-DayTimeFrameInaccuracy/2, DayTimeFrameInaccuracy/2] will be added to current day hour when checking for <see cref="DuringDayTimeFrames"/>.<br/>
    /// In hours. Assumes 24 hours per day. Can be fractional.<br/>
    /// For all entities with same schedule to not act in the same moment (which can feel robotic) set it some small value, 0.3 for example.
    /// </summary>
    [JsonProperty] public float DayTimeFrameInaccuracy = 3;

    /// <summary>
    /// AI tasks will stop when current time is out of <see cref="AiTaskBaseConfig.duringDayTimeFramesHours"/>.
    /// </summary>
    [JsonProperty] public bool StopIfOutOfDayTimeFrames = false;

    /// <summary>
    /// At what temperature range AI task should start. In degrees Celsius.<br/>
    /// Standard range of possible temperatures is [-20, 40].
    /// </summary>
    [JsonProperty] public float[]? TemperatureRange = null;

    /// <summary>
    /// Chance for this task to be executed each tick.
    /// </summary>
    [JsonProperty] public float ExecutionChance = 0.1f;

    /// <summary>
    /// Stops task if entity is hurt
    /// </summary>
    [JsonProperty] public bool StopOnHurt = false;

    /// <summary>
    /// Minimal duration after which task will be stopped if has not been stopped already.
    /// </summary>
    [JsonProperty] public int MinDurationMs = 0;

    /// <summary>
    /// Maximum duration after which task will be stopped if has not been stopped already.<br/>
    /// If less or equal to 0, duration will be ignored.
    /// </summary>
    [JsonProperty] public int MaxDurationMs = 0;

    /// <summary>
    /// Determines how long ago last attack should be for this task to consider being recently attacked.<br/>
    /// Is used to clear attacked-by entity.
    /// </summary>
    [JsonProperty] public int RecentlyAttackedTimeoutMs = 30000;

    /// <summary>
    /// If entity was recently attacked, task wont be executed.
    /// </summary>
    [JsonProperty] public bool DontExecuteIfRecentlyAttacked = false;


    public float PriorityForCancel => priorityForCancel ?? Priority;

    public EntityTagArray TagsAppliedToEntity = EntityTagArray.Empty;

    public AnimationMetaData? AnimationMeta = null;

    public DayTimeFrame[] DuringDayTimeFrames = [];


    public bool Initialized => initialized;

    protected const int maxLightLevel = 32;
    
    private bool initialized = false;

    /// <summary>
    /// For configs created from code. Dont forget to call base method.
    /// </summary>
    public virtual void Init(EntityAgent entity)
    {
        if (tagsAppliedToEntity != null) TagsAppliedToEntity = entity.Api.TagRegistry.EntityTagsToTagArray(tagsAppliedToEntity);
        if (duringDayTimeFramesHours != null)
        {
            DuringDayTimeFrames = [.. duringDayTimeFramesHours.Select(frame => new DayTimeFrame(frame[0], frame[1]))];
        }

        duringDayTimeFramesHours = null;
        tagsAppliedToEntity = null;

        if (animation != "")
        {
            string animationCode = animation.ToLowerInvariant();

            AnimationMetaData? animationMetaData = Array.Find(entity.Properties.Client.Animations, a => a.Code == animationCode);

            if (animationMetaData != null)
            {
                if (animationSpeed != null)
                {
                    AnimationMeta = animationMetaData.Clone();
                    AnimationMeta.AnimationSpeed = animationSpeed.Value;
                }
                else
                {
                    AnimationMeta = animationMetaData;
                }
            }
            else
            {
                AnimationMeta = new AnimationMetaData()
                {
                    Code = animationCode,
                    Animation = animationCode,
                    AnimationSpeed = animationSpeed ?? 1
                }.Init();

                AnimationMeta.EaseInSpeed = 1f;
                AnimationMeta.EaseOutSpeed = 1f;
            }
        }

        if (Sound != null)
        {
            Sound = Sound.WithPathPrefixOnce("sounds/");
        }

        if (FinishSound != null)
        {
            FinishSound = FinishSound.WithPathPrefixOnce("sounds/");
        }


        initialized = true;
    }

    /// <summary>
    /// For config read from json. Dont forget to call base method.
    /// </summary>
    public virtual void Init(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
    {
        Init(entity);

        initialized = true;
    }
}

public abstract class AiTaskBaseR : IAiTask
{
    public virtual string Id => Config.Id;
    public virtual int Slot => Config.Slot;
    public virtual float Priority => Config.Priority;
    public virtual float PriorityForCancel => Config.PriorityForCancel;
    /// <summary>
    /// Set and used by <see cref="AiTaskManager"/>
    /// </summary>
    public string ProfilerName { get; set; } = "";
    /// <summary>
    /// Is exclusively used by <see cref="EntityBehaviorSemiTamed"/>, looks like a crutch.
    /// </summary>
    public string[] WhenInEmotionState => Config.WhenInEmotionState;
    public Entity? AttackedByEntity => attackedByEntity;

    protected AiTaskBaseConfig baseConfig;

    private AiTaskBaseConfig Config => baseConfig;


    // Optimization stuff, dont touch
    [ThreadStatic]
    private static Random? randThreadStatic;
    protected Random Rand => randThreadStatic ??= new Random();

    protected bool RecentlyAttacked => entity.World.ElapsedMilliseconds - attackedByEntityMs < Config.RecentlyAttackedTimeoutMs;

    protected readonly EntityAgent entity;
    protected readonly IWorldAccessor world;
    protected WaypointsTraverser pathTraverser;
    protected EntityBehaviorEmotionStates? emotionStatesBehavior;
    protected EntityBehaviorTaskAI taskAiBehavior;
    protected EntityPartitioning partitionUtil;
    protected const int maxLightLevel = 32;
    protected const int standardHoursPerDay = 24;
    protected bool stopTask = false;
    protected bool active = false;
    protected long durationUntilMs = 0;
    protected float currentDayTimeInaccuracy = 0;
    protected Entity? attackedByEntity;
    protected long attackedByEntityMs;

    /// <summary>
    /// Last value of <see cref="IWorldAccessor.ElapsedMilliseconds"/> when sound was played.
    /// </summary>
    protected long lastSoundTotalMs;
    /// <summary>
    /// The value of <see cref="IWorldAccessor.ElapsedMilliseconds"/> when the short cooldown will be over.
    /// </summary>
    protected long cooldownUntilMs;
    /// <summary>
    /// The value of <see cref="IGameCalendar.TotalHours"/> when the long cooldown will be over.
    /// </summary>
    protected double cooldownUntilTotalHours;
    /// <summary>
    /// The value of <see cref="IWorldAccessor.ElapsedMilliseconds"/> when this task was last started.
    /// </summary>
    protected long executionStartTimeMs;
    /// <summary>
    /// Tags that were actually applied on the start of the task. Will be removed from entity when task is finished.
    /// </summary>
    protected EntityTagArray tagsAppliedOnStart;


    protected AiTaskBaseR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
    {
#pragma warning disable S3010 // Static fields should not be updated in constructors
        randThreadStatic ??= new Random((int)entity.EntityId);
#pragma warning restore S3010

        this.entity = entity;
        world = entity.World;
        pathTraverser = entity.GetBehavior<EntityBehaviorTaskAI>().PathTraverser
            ?? throw new ArgumentException($"PathTraverser should not be null, possible error on EntityBehaviorTaskAI initialization for entity: {entity.Code}.");
        emotionStatesBehavior = entity.GetBehavior<EntityBehaviorEmotionStates>();
        taskAiBehavior = entity.GetBehavior<EntityBehaviorTaskAI>()
            ?? throw new ArgumentException($"Entity '{entity.Code}' does not have EntityBehaviorTaskAI.");
        partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>()
            ?? throw new ArgumentException("EntityPartitioning mod system is not found");

        baseConfig = LoadConfig<AiTaskBaseConfig>(entity, taskConfig, aiConfig);

        int initialMinCooldown = Config.InitialMinCooldownMs;
        int initialMaxCooldown = Config.InitialMaxCooldownMs;

        if (Config.TemperatureRange != null && Config.TemperatureRange.Length != 2)
        {
            entity.Api.Logger.Error($"Invalid 'temperatureRange' value in AI task '{Config.Code}' for entity '{entity.Code}'");
            throw new ArgumentException($"Invalid 'temperatureRange' value in AI task '{Config.Code}' for entity '{entity.Code}'");
        }

        cooldownUntilMs = entity.World.ElapsedMilliseconds + initialMinCooldown + entity.World.Rand.Next(initialMaxCooldown - initialMinCooldown);

        attackedByEntityMs = -Config.RecentlyAttackedTimeoutMs;
    }

    protected AiTaskBaseR(EntityAgent entity)
    {
#pragma warning disable S3010 // Static fields should not be updated in constructors
        randThreadStatic ??= new Random((int)entity.EntityId);
#pragma warning restore S3010

        this.entity = entity;
        world = entity.World;
        pathTraverser = entity.GetBehavior<EntityBehaviorTaskAI>().PathTraverser
            ?? throw new ArgumentException($"PathTraverser should not be null, possible error on EntityBehaviorTaskAI initialization for entity: {entity.Code}.");
        emotionStatesBehavior = entity.GetBehavior<EntityBehaviorEmotionStates>();
        taskAiBehavior = entity.GetBehavior<EntityBehaviorTaskAI>()
            ?? throw new ArgumentException($"Entity '{entity.Code}' does not have EntityBehaviorTaskAI.");
        partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>()
            ?? throw new ArgumentException("EntityPartitioning mod system is not found");

        AiTaskRegistry.TaskCodes.TryGetValue(GetType(), out string? codeFromType);

        baseConfig = new();
        baseConfig.Code = baseConfig.Code == "" ? codeFromType ?? "" : baseConfig.Code;
        baseConfig.Init(entity);
    }

    public virtual void AfterInitialize()
    {
        if (!baseConfig.Initialized)
        {
            throw new InvalidOperationException($"Config was not initialized for task '{Config.Code}' and entity '{entity.Code}'. Have you forgot to call 'base.Init()' method in 'Init()'?");
        }
    }

    public abstract bool ShouldExecute();

    public virtual void StartExecute()
    {
        if (Config.AnimationMeta != null)
        {
            entity.AnimManager.StartAnimation(Config.AnimationMeta);
        }

        if (Config.Sound != null && entity.World.Rand.NextDouble() <= Config.SoundChance)
        {
            if (Config.SoundStartMs > 0)
            {
                entity.World.RegisterCallback((dt) =>
                {
                    entity.World.PlaySoundAt(Config.Sound, entity.ServerPos.X, entity.ServerPos.InternalY, entity.ServerPos.Z, null, Config.RandomizePitch, Config.SoundRange, Config.SoundVolume);
                    lastSoundTotalMs = entity.World.ElapsedMilliseconds;
                }, Config.SoundStartMs);
            }
            else
            {
                entity.World.PlaySoundAt(Config.Sound, entity.ServerPos.X, entity.ServerPos.InternalY, entity.ServerPos.Z, null, Config.RandomizePitch, Config.SoundRange, Config.SoundVolume);
                lastSoundTotalMs = entity.World.ElapsedMilliseconds;
            }
        }

        if (Config.MaxDurationMs <= 0)
        {
            durationUntilMs = 0;
        }
        else
        {
            durationUntilMs = entity.World.ElapsedMilliseconds + Config.MinDurationMs + entity.World.Rand.Next(Config.MaxDurationMs - Config.MinDurationMs);
        }

        executionStartTimeMs = entity.World.ElapsedMilliseconds;

        tagsAppliedOnStart = ~entity.Tags & Config.TagsAppliedToEntity;
        if (tagsAppliedOnStart != EntityTagArray.Empty)
        {
            entity.Tags |= tagsAppliedOnStart;
            entity.MarkTagsDirty();
        }

        active = true;
        stopTask = false;
    }

    public virtual bool ContinueExecute(float dt) => ContinueExecuteChecks(dt);

    public virtual void FinishExecute(bool cancelled)
    {
        cooldownUntilMs = entity.World.ElapsedMilliseconds + Config.MinCooldownMs + entity.World.Rand.Next(Config.MaxCooldownMs - Config.MinCooldownMs);
        cooldownUntilTotalHours = entity.World.Calendar.TotalHours + Config.MinCooldownHours + entity.World.Rand.NextDouble() * (Config.MaxCooldownHours - Config.MinCooldownHours);

        // Ugly hack to fix attack animation sometimes not playing - it seems it gets stopped even before it gets sent to the client?
        if (Config.AnimationMeta != null && Config.AnimationMeta.Code != "attack" && Config.AnimationMeta.Code != "idle")
        {
            entity.AnimManager.StopAnimation(Config.AnimationMeta.Code);
        }

        if (Config.FinishSound != null)
        {
            entity.World.PlaySoundAt(Config.FinishSound, entity.ServerPos.X, entity.ServerPos.InternalY, entity.ServerPos.Z, null, Config.RandomizePitch, Config.SoundRange, Config.SoundVolume);
        }

        if (tagsAppliedOnStart != EntityTagArray.Empty)
        {
            entity.Tags &= ~tagsAppliedOnStart;
            entity.MarkTagsDirty();
        }

        active = false;
    }

    /// <summary>
    /// Called whenever the entity changes from Active to Inactive state, or vice versa
    /// </summary>
    /// <param name="beforeState"></param>
    public virtual void OnStateChanged(EnumEntityState beforeState)
    {
        // Reset timer because otherwise the tasks will always be executed upon entering active state
        if (entity.State == EnumEntityState.Active)
        {
            IWorldAccessor World = entity.World;
            cooldownUntilMs = World.ElapsedMilliseconds + Config.MinCooldownMs + World.Rand.Next(Config.MaxCooldownMs - Config.MinCooldownMs);
        }
    }

    public virtual bool Notify(string key, object data) => false;

    public virtual void OnEntityLoaded() { }

    public virtual void OnEntitySpawn() { }

    public virtual void OnEntityDespawn(EntityDespawnData reason) { }

    public virtual void OnEntityHurt(DamageSource source, float damage)
    {
        if (Config.StopOnHurt) stopTask = true;

        attackedByEntity = source.GetCauseEntity();
        if (attackedByEntity != null)
        {
            attackedByEntityMs = entity.World.ElapsedMilliseconds;
        }
    }

    public virtual void OnNoPath(Vec3d target) { }

    public virtual bool CanContinueExecute() => true;

    protected static TConfig LoadConfig<TConfig>(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) where TConfig : AiTaskBaseConfig
    {
        TConfig? config;

        try
        {
            config = taskConfig.AsObject<TConfig>();
            if (config == null)
            {
                string code = taskConfig["code"]?.AsString("") ?? "";
                string errorMessage = $"Failed to parse task config '{code} ({typeof(TConfig)})' for entity '{entity.Code}'.";
                entity.Api.Logger.Error(errorMessage);
                throw new ArgumentNullException(errorMessage);
            }
        }
        catch (Exception exception)
        {
            string code = taskConfig["code"]?.AsString("") ?? "";
            entity.Api.Logger.Error($"Failed to parse task config '{code} ({typeof(TConfig)})' for entity '{entity.Code}'. Exception:\n{exception}\n");
            throw;
        }

        try
        {
            config.Init(entity, taskConfig, aiConfig);
        }
        catch (Exception exception)
        {
            string code = taskConfig["code"]?.AsString("") ?? "";
            entity.Api.Logger.Error($"Failed initiate config for task '{code} ({typeof(TConfig)})' for entity '{entity.Code}'. Exception:\n{exception}\n");
            throw;
        }

        if (!config.Initialized)
        {
            string errorMessage = $"Config was not initialized for task '{config.Code}' and entity '{entity.Code}'. Have you forgot to call 'base.Init()' method in '{typeof(TConfig)}.Init()'?";
            entity.Api.Logger.Error(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        return config;
    }

    protected TConfig GetConfig<TConfig>() where TConfig : AiTaskBaseConfig
    {
        return baseConfig as TConfig ?? throw new InvalidOperationException($"Wrong type of config '{baseConfig.GetType()}', should be '{typeof(TConfig)}' or it subclass.");
    }

    /// <summary>
    /// Checks for swimming and emotion states
    /// </summary>
    /// <returns></returns>
    protected virtual bool PreconditionsSatisficed()
    {
        if (!CheckExecutionChance()) return false;
        if (!CheckCooldowns()) return false;
        if (!CheckEntityState()) return false;
        if (!CheckEmotionStates()) return false;
        if (!CheckEntityLightLevel()) return false;

        currentDayTimeInaccuracy = (float)entity.World.Rand.NextDouble() * Config.DayTimeFrameInaccuracy - Config.DayTimeFrameInaccuracy / 2;

        if (!CheckDayTimeFrames()) return false;
        if (!CheckTemperature()) return false;
        if (Config.DontExecuteIfRecentlyAttacked && RecentlyAttacked) return false;
        return true;
    }

    /// <summary>
    /// Returns 'true' if this entity is in a one of supplied emotions state, or if no emotion states are supplied.<br/>
    /// Returns 'false' if entity does not have emotion state behavior.
    /// </summary>
    protected virtual bool IsInEmotionState(params string[] emotionStates)
    {
        if (emotionStatesBehavior == null) return false;
        if (emotionStates.Length == 0) return true;

        for (int i = 0; i < emotionStates.Length; i++)
        {
            if (emotionStatesBehavior.IsInEmotionState(emotionStates[i])) return true;
        }
        return false;
    }

    protected virtual bool DurationExceeded() => durationUntilMs > 0 && entity.World.ElapsedMilliseconds > durationUntilMs;

    protected virtual bool CheckEntityLightLevel()
    {
        if (Config.EntityLightLevels[0] == 0 && Config.EntityLightLevels[1] == 32) return true;

        int lightLevel = entity.World.BlockAccessor.GetLightLevel((int)entity.Pos.X, (int)entity.Pos.InternalY, (int)entity.Pos.Z, Config.EntityLightLevelType);

        return Config.EntityLightLevels[0] <= lightLevel && lightLevel <= Config.EntityLightLevels[1];
    }

    protected virtual bool CheckDayTimeFrames()
    {
        if (Config.DuringDayTimeFrames.Length == 0) return true;

        // introduce a bit of randomness so that (e.g.) hens do not all wake up simultaneously at 06:00, which looks artificial
        // essentially works in fractions of a day, instead of hours, but for convinience scaled to use 24 hours per day scale
        double hourOfDay = entity.World.Calendar.HourOfDay / entity.World.Calendar.HoursPerDay * standardHoursPerDay + currentDayTimeInaccuracy;
        foreach (DayTimeFrame frame in Config.DuringDayTimeFrames)
        {
            if (frame.Matches(hourOfDay)) return true;
        }

        return false;
    }

    protected virtual bool CheckTemperature()
    {
        if (Config.TemperatureRange == null) return true;
        
        float temperature = entity.World.BlockAccessor.GetClimateAt(entity.Pos.AsBlockPos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, entity.World.Calendar.TotalDays).Temperature;

        Debug.WriteLine($"temperature: {temperature}");

        return Config.TemperatureRange[0] <= temperature && temperature <= Config.TemperatureRange[1];
    }

    protected virtual bool CheckCooldowns()
    {
        return cooldownUntilMs <= entity.World.ElapsedMilliseconds && cooldownUntilTotalHours <= entity.World.Calendar.TotalHours;
    }

    protected virtual bool CheckEntityState()
    {
        if (Config.WhenSwimming != null && Config.WhenSwimming != entity.Swimming) return false;
        if (Config.WhenFeetInLiquid != null && Config.WhenFeetInLiquid != entity.FeetInLiquid) return false;

        return true;
    }

    protected virtual bool CheckEmotionStates()
    {
        if (Config.WhenInEmotionState.Length > 0 && !IsInEmotionState(Config.WhenInEmotionState)) return false;
        if (Config.WhenNotInEmotionState.Length > 0 && IsInEmotionState(Config.WhenNotInEmotionState)) return false;

        return true;
    }

    protected virtual bool CheckExecutionChance()
    {
        return Rand.NextDouble() <= Config.ExecutionChance;
    }

    protected virtual bool ContinueExecuteChecks(float dt)
    {
        if (stopTask)
        {
            stopTask = false;
            return false;
        }

        if (DurationExceeded())
        {
            durationUntilMs = 0;
            return false;
        }

        if (Config.Sound != null && Config.SoundRepeatMs > 0 && entity.World.ElapsedMilliseconds > lastSoundTotalMs + Config.SoundRepeatMs)
        {
            entity.World.PlaySoundAt(Config.Sound, entity.ServerPos.X, entity.ServerPos.InternalY, entity.ServerPos.Z, null, Config.RandomizePitch, Config.SoundRange, Config.SoundVolume);
            lastSoundTotalMs = entity.World.ElapsedMilliseconds;
        }

        if (Config.StopIfOutOfDayTimeFrames && !CheckDayTimeFrames())
        {
            return false;
        }

        return true;
    }

    protected virtual int GetOwnGeneration() // @TODO refactor taming code
    {
        int generation = entity.WatchedAttributes.GetInt("generation", 0);
        if (entity.Properties.Attributes?.IsTrue("tamed") == true)
        {
            generation += 10; // @TODO hardcoded value for generation to be considered tamed
        }
        return generation;
    }
}

public class DayTimeFrameJson
{
    public double FromHour;
    public double ToHour;

    public DayTimeFrame ToStruct() => new(FromHour, ToHour);
}

public readonly struct DayTimeFrame(double fromHour, double toHour)
{
    public readonly double FromHour = fromHour;
    public readonly double ToHour = toHour;

    public bool Matches(double hourOfDay)
    {
        return FromHour <= hourOfDay && ToHour >= hourOfDay;
    }
}
