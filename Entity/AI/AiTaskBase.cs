using Newtonsoft.Json;
using System;
using System.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.Essentials;

namespace Vintagestory.API.Common
{
    public readonly struct DayTimeFrame(double fromHour, double toHour)
    {
        public readonly double FromHour = fromHour;
        public readonly double ToHour = toHour;

        public bool Matches(double hourOfDay)
        {
            return (FromHour <= hourOfDay && ToHour >= hourOfDay) // Normal case
                || (FromHour > ToHour && (FromHour <= hourOfDay || ToHour >= hourOfDay)); // Time frame includes midnight
        }
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public abstract class AiTaskBase : IAiTask
    {
        [ThreadStatic]
        private static Random? randTL;
        public Random rand => randTL ?? (randTL = new Random());
        public readonly EntityAgent entity;
        public readonly IWorldAccessor world;
        public AnimationMetaData? animMeta;

        /// <summary>
        /// A unique identifier for this task type, used to start specific tasks from code
        /// </summary>
        [JsonProperty]
        public virtual string? Id { get; set; }

        /// <summary>
        /// Tasks with higher priority will be executed first. For a task to begin running when another task is already active, its priority should be greater than active task's <see cref="PriorityForCancel"/>.
        /// </summary>
        [JsonProperty]
        public virtual float Priority { get; set; }
        [JsonProperty]
        protected float? priorityForCancel;
        public virtual float PriorityForCancel => priorityForCancel ?? Priority;

        /// <summary>
        /// Can allow tasks to be executed in parallel. Up to <see cref="AiTaskManager.ActiveTasksBySlot"/> tasks (currently 8) can be executed at same time.<br/>
        /// Tasks with same slot number will replace each other, while tasks with different slot numbers can run side by side.
        /// </summary>
        [JsonProperty]
        public virtual int Slot { get; set; } = 0;

        /// <summary> If all other conditions are met, this is the chance for the task to start each tick. </summary>
        [JsonProperty]
        public double ExecutionChance = 1;

        /// <summary>
        /// List of entity tags that will be added to entity when task start.<br/>
        /// Added tags will be removed when task finishes, unless the entity already had them when starting the task. Not designed for use with multiple slots, as tags may be removed too early if multiple AI tasks added the same ones.
        /// </summary>
        [JsonProperty]
        public TagSetFast TagsAppliedToEntity;
        /// <summary> Tags that were actually applied on the start of the task. Will be removed from entity when task is finished. </summary>
        protected TagSetFast tagsAppliedOnStart;

        /// <summary> Minimal cooldown before task can be run again, in IRL milliseconds (generally used for shorter cooldowns). </summary>
        [JsonProperty]
        public int MinCooldownMs = 0;
        [Obsolete("Use MinCooldownMs instead. Kept for now for json backwards compatibility.")]
        [JsonProperty]
        public int Mincooldown { get => MinCooldownMs; set => MinCooldownMs = value; }
        /// <summary> Maximum cooldown before task can be run again, in IRL milliseconds (generally used for shorter cooldowns). </summary>
        [JsonProperty]
        public int MaxCooldownMs = 100;
        [Obsolete("Use MaxCooldownMs instead. Kept for now for json backwards compatibility.")]
        [JsonProperty]
        public int Maxcooldown { get => MaxCooldownMs; set => MaxCooldownMs = value; }

        /// <summary>
        /// Minimum cooldown before task can be run again, in in-game hours
        /// (<see cref="IWorldAccessor.Calendar.TotalHours"/>, generally used for longer cooldowns).
        /// </summary>
        [JsonProperty]
        protected double MinCooldownHours = 0;
        [Obsolete("Use MinCooldownHours instead.")]
        protected double mincooldownHours { get => MinCooldownHours; set => MinCooldownHours = value; }
        /// <summary>
        /// Maximum cooldown before task can be run again, in in-game hours
        /// (<see cref="IWorldAccessor.Calendar.TotalHours"/>, generally used for longer cooldowns).
        /// </summary>
        [JsonProperty]
        protected double MaxCooldownHours = 0;
        [Obsolete("Use MaxCooldownHours instead.")]
        protected double maxcooldownHours { get => MaxCooldownHours; set => MaxCooldownHours = value; }

        /// <summary> Sound to play when the task is finished. Including 'sounds/' at the start is not needed. </summary>
        protected AssetLocation? finishSound;
        /// <summary>
        /// Sound to play <see cref="SoundStartMs"/> milliseconds after task start,
        /// and repeat each <see cref="SoundRepeatMs"/> milliseconds if that is greater than 0.<br/>
        /// Can leave out the 'sounds/' from the start.
        /// </summary>
        protected AssetLocation? sound;
        /// <summary> Sound range of the <see cref="Sound"/> and <see cref="FinishSound"/>. </summary>
        [JsonProperty]
        protected float soundRange = 16;
        /// <summary> Sound will be started in this amount of milliseconds after task starts. </summary>
        [JsonProperty]
        protected int soundStartMs;
        /// <summary> If greater than 0, sound will repeat at this rate (in milliseconds). </summary>
        [JsonProperty]
        protected int soundRepeatMs;
        /// <summary> Chance to play <see cref="Sound"/>. </summary>
        [JsonProperty]
        protected float soundChance = 1.01f;
        protected long lastSoundTotalMs;

        /// <summary>
        /// If set to 'true' task will start only if entity is swimming.<br/>
        /// If set to 'false' task will start only if entity is not swimming.<br/>
        /// If not set, this condition will be ignored.
        /// </summary>
        [JsonProperty]
        public bool? WhenSwimming;

        /// <summary> Task will start only when the entity is in at least one of these emotion states. If left blank, the task does not require any emotion state. </summary>
        [JsonProperty]
        public string[]? WhenInEmotionStates;
        /// <summary> Task will start only when the entity is in NONE of these emotion states. If left blank, this condition will be ignored. </summary>
        [JsonProperty]
        public string[]? WhenNotInEmotionStates;
        [Obsolete("Use WhenInEmotionStates (plural, array) instead")] // May be set as private later. Supports json backwards comaptibility.
        [JsonProperty]
        public string? WhenInEmotionState { get => WhenInEmotionStates?[0]; set => WhenInEmotionStates = value?.Split("|"); }
        [Obsolete("Use WhenNotInEmotionStates (plural, array) instead")] // May be set as private later. Supports json backwards comaptibility.
        [JsonProperty]
        public string? WhenNotInEmotionState { get => WhenNotInEmotionStates?[0]; set => WhenNotInEmotionStates = value?.Split("|"); }

        protected long cooldownUntilMs;
        protected double cooldownUntilTotalHours;

        protected WaypointsTraverser pathTraverser;
        protected EntityBehaviorEmotionStates? bhEmo;

        /// <summary>
        /// A list of day-time-frames in which AI task can start. If left blank, time of day is ignored.<br/>
        /// Each frame is a pair of day hours: [0.5, 2.9].<br/>
        /// In hours. Assumes 24 hours per day. 0 = midnight, 12 = noon, 23.5 = 11:30 pm<br/>
        /// For overnight tasks, you can set the first number larger than the second (e.g. [20, 6] goes from 8 pm to 6 am)
        /// </summary>
        [JsonProperty]
        public DayTimeFrame[]? duringDayTimeFrames;

        /// <summary>
        /// A random value from -DayTimeFrameInaccuracy to +DayTimeFrameInaccuracy will be added to current day hour when checking for <see cref="DuringDayTimeFrames"/>.<br/>
        /// In hours. Assumes 24 hours per day. Can be fractional.<br/>
        /// For all entities with the same schedule to not act in the same moment (which can feel robotic) set it some small value, 0.15 for example.
        /// </summary>
        [JsonProperty]
        public float DayTimeFrameInaccuracy = 0.15f;

        /// <summary>
        /// If the entity's light level is outside of this range, this ai task won't be executed.<br/>
        /// Minimum light level is 0, maximum is 32.
        /// </summary>
        [JsonProperty]
        public int[] EntityLightLevels = [0, 32];

        /// <summary>
        /// Light level type that will be used for <see cref="EntityLightLevels"/>. See <see cref="EnumLightLevelType"/> for description of each light level type. Case sensitive.<br/>
        /// </summary>
        [JsonProperty]
        public EnumLightLevelType EntityLightLevelType = EnumLightLevelType.MaxTimeOfDayLight;

        /// <summary>
        /// At what temperature range AI task should start. In degrees Celsius.<br/>
        /// Standard range of possible temperatures is [-20, 40].
        /// </summary>
        [JsonProperty]
        public float[]? TemperatureRange = null;

        protected double defaultTimeoutSec = 30;
        protected TimeSpan timeout;
        protected long ExecutionStartTimeMs;

        /// <summary>
        /// Set and used by <see cref="AiTaskManager"/>
        /// </summary>
        public string ProfilerName { get; set; } = "";

        public AiTaskBase(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        {
            this.entity = entity;
            this.world = entity.World;
            randTL ??= new Random((int)entity.EntityId);

            this.pathTraverser = entity.GetBehavior<EntityBehaviorTaskAI>()!.PathTraverser!;
            if (pathTraverser == null)
            {
                throw new ArgumentNullException("No PathTraverser found for entity " + entity.Code);
            }

            bhEmo = entity.GetBehavior<EntityBehaviorEmotionStates>();

            SetDefaultValues();
            if (taskConfig.Exists)
            {
                // Note that the generic type here is only used for proving the target is a class rather than a struct,
                // and does not limit which fields will be populated
                JsonUtil.Populate<AiTaskBase>(taskConfig.Token, this);
            }

            Id = taskConfig["id"].AsString();

            int initialmincooldown = taskConfig["initialMinCoolDown"].AsInt(MinCooldownMs);
            int initialmaxcooldown = taskConfig["initialMaxCoolDown"].AsInt(MaxCooldownMs);
            cooldownUntilMs = entity.World.ElapsedMilliseconds + initialmincooldown + entity.World.Rand.Next(initialmaxcooldown - initialmincooldown);

            timeout = TimeSpan.FromSeconds(taskConfig["timeoutSec"].AsDouble(defaultTimeoutSec));

            JsonObject animationCfg = taskConfig["animation"];
            if (animationCfg.Exists)
            {
                var code = animationCfg.AsString()?.ToLowerInvariant();
                JsonObject animationSpeedCfg = taskConfig["animationSpeed"];
                float speed = animationSpeedCfg.AsFloat(1f);

                var cmeta = this.entity.Properties.Client.Animations.FirstOrDefault(a => a.Code == code);
                if (cmeta != null)
                {
                    if (animationSpeedCfg.Exists)
                    {
                        animMeta = cmeta.Clone();
                        animMeta.AnimationSpeed = speed;
                    } else
                    {
                        animMeta = cmeta;
                    }
                } else
                {
                    animMeta = new AnimationMetaData()
                    {
                        Code = code,
                        Animation = code,
                        AnimationSpeed = speed
                    }.Init();

                    animMeta.EaseInSpeed = 1f;
                    animMeta.EaseOutSpeed = 1f;
                }
            }

            JsonObject soundCfg = taskConfig["sound"];
            if (soundCfg.Exists)
            {
                sound = AssetLocation.Create(soundCfg.AsString(), entity.Code.Domain).WithPathPrefixOnce("sounds/");
            }
            JsonObject finishSoundCfg = taskConfig["finishSound"];
            if (finishSoundCfg.Exists)
            {
                finishSound = AssetLocation.Create(finishSoundCfg.AsString(), entity.Code.Domain).WithPathPrefixOnce("sounds/");
            }

            string? taskCode = taskConfig["code"].AsString();
            if (TemperatureRange != null && TemperatureRange.Length != 2)
            {
                throw new ArgumentException($"Invalid 'temperatureRange' value in AI task '{taskCode}' for entity '{entity.Code}'");
            }
            if (EntityLightLevels.Length != 2)
            {
                throw new ArgumentException($"Invalid 'entityLightLevels' value in AI task '{taskCode}' for entity '{entity.Code}'");
            }
        }

        // Override this to to set fields to the default values they should have if left out of the json
        // Always call base.SetDefaultValues() when you override
        protected virtual void SetDefaultValues()
        {
            // Empty
        }

        protected virtual bool IsOnCooldown()
        {
            return cooldownUntilMs <= entity.World.ElapsedMilliseconds && cooldownUntilTotalHours <= entity.World.Calendar.TotalHours;
        }

        protected virtual void ResetCooldown()
        {
            cooldownUntilMs = entity.World.ElapsedMilliseconds + MinCooldownMs + entity.World.Rand.Next(MaxCooldownMs - MinCooldownMs);
            cooldownUntilTotalHours = entity.World.Calendar.TotalHours + MinCooldownHours + entity.World.Rand.NextDouble() * (MaxCooldownHours - MinCooldownHours);
        }

        protected virtual bool IsValidTemperature()
        {
            if (TemperatureRange == null) return true;

            float temperature = entity.World.BlockAccessor.GetClimateAt(entity.Pos.AsBlockPos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, entity.World.Calendar.TotalDays).Temperature;
            return TemperatureRange[0] <= temperature && temperature <= TemperatureRange[1];
        }

        protected virtual bool IsValidLightLevel()
        {
            if (EntityLightLevels[0] == 0 && EntityLightLevels[1] == 32) return true;

            int lightLevel = entity.World.BlockAccessor.GetLightLevel((int)entity.Pos.X, (int)entity.Pos.InternalY, (int)entity.Pos.Z, EntityLightLevelType);
            return EntityLightLevels[0] <= lightLevel && lightLevel <= EntityLightLevels[1];
        }

        protected virtual bool PreconditionsSatisfied()
        {
            if (WhenSwimming != null && WhenSwimming != entity.Swimming) return false;
            if (WhenInEmotionStates != null && !IsInEmotionState(WhenInEmotionStates)) return false;
            if (WhenNotInEmotionStates != null && IsInEmotionState(WhenNotInEmotionStates)) return false;
            if (!IsValidTemperature()) return false;
            if (!IsValidLightLevel()) return false;
            if (!IsInValidDayTimeHours(true)) return false;
            return true;
        }

        protected virtual bool IsInValidDayTimeHours(bool initialRandomness)
        {
            if (duringDayTimeFrames != null)
            {
                // Introduce a bit of randomness so that (e.g.) hens do not all wake up simultaneously at 06:00, which looks artificial
                double hourOfDay = entity.World.Calendar.HourOfDay / entity.World.Calendar.HoursPerDay * 24f;
                if (initialRandomness)
                {
                    hourOfDay += DayTimeFrameInaccuracy * (entity.World.Rand.NextDouble() * 2 - 1);
                }
                for (int i = 0; i < duringDayTimeFrames.Length; i++)
                {
                    if (duringDayTimeFrames[i].Matches(hourOfDay)) return true;
                }
                return false;
            }
            // If there's no specified time frames, then any time is valid.
            return true;
        }

        protected virtual bool IsInEmotionState(string[] emotionStates)
        {
            if (bhEmo == null) return false;

            foreach (string state in emotionStates)
            {
                if (bhEmo.IsInEmotionState(state)) return true;
            }

            return false;
        }

        protected virtual bool IsInEmotionState(string emotionState)
        {
            if (bhEmo == null) return false;
            if (emotionState == null) return false;

            return bhEmo.IsInEmotionState(emotionState);
        }

        public virtual void AfterInitialize()
        {
        }

        public abstract bool ShouldExecute();

        public virtual void StartExecute()
        {
            if (animMeta != null)
            {
                entity.AnimManager.StartAnimation(animMeta);
            }

            if (sound != null && entity.World.Rand.NextDouble() <= soundChance)
            {
                if (soundStartMs > 0)
                {
                    entity.World.RegisterCallback((dt) => {
                        entity.World.PlaySoundAt(sound, entity.Pos.X, entity.Pos.InternalY, entity.Pos.Z, null, true, soundRange);
                        lastSoundTotalMs = entity.World.ElapsedMilliseconds;
                    }, soundStartMs);
                } else
                {
                    entity.World.PlaySoundAt(sound, entity.Pos.X, entity.Pos.InternalY, entity.Pos.Z, null, true, soundRange);
                    lastSoundTotalMs = entity.World.ElapsedMilliseconds;
                }

            }

            ExecutionStartTimeMs = entity.World.ElapsedMilliseconds;

            tagsAppliedOnStart = TagsAppliedToEntity & ~entity.Tags; // Do not modify tags the entity already had.
            if (!tagsAppliedOnStart.IsEmpty)
            {
                entity.Tags |= tagsAppliedOnStart;
                entity.MarkTagsDirty();
            }
        }

        public virtual bool ContinueExecute(float dt)
        {
            if (sound != null && soundRepeatMs > 0 && entity.World.ElapsedMilliseconds > lastSoundTotalMs + soundRepeatMs)
            {
                entity.World.PlaySoundAt(sound, entity.Pos.X, entity.Pos.InternalY, entity.Pos.Z, null, true, soundRange);
                lastSoundTotalMs = entity.World.ElapsedMilliseconds;
            }

            //Check if time is still valid for task.
            if (!IsInValidDayTimeHours(false)) return false;

            return true;
        }

        public virtual void FinishExecute(bool cancelled)
        {
            ResetCooldown();

            // Ugly hack to fix attack animation sometimes not playing - it seems it gets stopped even before it gets sent to the client?
            if (animMeta != null && animMeta.Code != "attack" && animMeta.Code != "idle")
            {
                entity.AnimManager.StopAnimation(animMeta.Code);
            }

            if (finishSound != null)
            {
                entity.World.PlaySoundAt(finishSound, entity.Pos.X, entity.Pos.InternalY, entity.Pos.Z, null, true, soundRange);
            }

            if (!tagsAppliedOnStart.IsEmpty)
            {
                entity.Tags &= ~tagsAppliedOnStart;
                entity.MarkTagsDirty();
            }
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
                var World = entity.World;
                cooldownUntilMs = World.ElapsedMilliseconds + MinCooldownMs + World.Rand.Next(MaxCooldownMs - MinCooldownMs);
            }
        }

        public virtual bool Notify(string key, object data)
        {
            return false;
        }

        public virtual void OnEntityLoaded()
        {

        }

        public virtual void OnEntitySpawn()
        {

        }

        public virtual void OnEntityDespawn(EntityDespawnData reason)
        {

        }

        public virtual void OnEntityHurt(DamageSource source, float damage)
        {

        }

        public virtual void OnNoPath(Vec3d target)
        {

        }

        public virtual bool CanContinueExecute()
        {
            return true;
        }

        protected virtual bool timeoutExceeded() => (entity.World.ElapsedMilliseconds - ExecutionStartTimeMs) > timeout.TotalMilliseconds;
    }
}
