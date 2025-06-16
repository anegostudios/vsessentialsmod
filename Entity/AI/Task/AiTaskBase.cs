using System;
using Vintagestory.API.Common.Entities;
using System.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Util;
using Vintagestory.Essentials;

#nullable disable

namespace Vintagestory.API.Common
{
    public abstract class AiTaskBase : IAiTask
    {
        [ThreadStatic]
        private static Random randTL;
        public Random rand => randTL ?? (randTL = new Random());
        public EntityAgent entity;
        public IWorldAccessor world;
        public AnimationMetaData animMeta;

        /// <summary>
        /// A unique identifier for this task
        /// </summary>
        public string Id { get; set; }


        protected float priority;
        protected float priorityForCancel;
        protected int slot;
        public int Mincooldown;
        public int Maxcooldown;

        protected double mincooldownHours;
        protected double maxcooldownHours;

        protected AssetLocation finishSound;
        protected AssetLocation sound;
        protected float soundRange = 16;
        protected int soundStartMs;
        protected int soundRepeatMs;
        protected float soundChance=1.01f;
        protected long lastSoundTotalMs;

        public string WhenInEmotionState;
        public bool? WhenSwimming;
        public string WhenNotInEmotionState;

        protected long cooldownUntilMs;
        protected double cooldownUntilTotalHours;

        protected WaypointsTraverser pathTraverser;

        protected EntityBehaviorEmotionStates bhEmo;

        protected double defaultTimeoutSec = 30;
        protected TimeSpan timeout;
        protected long executeStartTimeMs;

        private string profilerName;
        public string ProfilerName { get => profilerName; set => profilerName = value; }

        public AiTaskBase(EntityAgent entity)
        {
            this.entity = entity;
            this.world = entity.World;
            if (randTL == null) randTL = new Random((int)entity.EntityId);

            this.pathTraverser = entity.GetBehavior<EntityBehaviorTaskAI>().PathTraverser;
            bhEmo = entity.GetBehavior<EntityBehaviorEmotionStates>();
        }

        public virtual void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            this.priority = taskConfig["priority"].AsFloat();
            this.priorityForCancel = taskConfig["priorityForCancel"].AsFloat(priority);

            this.Id = taskConfig["id"].AsString();
            this.slot = (int)taskConfig["slot"]?.AsInt(0);
            this.Mincooldown = (int)taskConfig["mincooldown"]?.AsInt(0);
            this.Maxcooldown = (int)taskConfig["maxcooldown"]?.AsInt(100);

            this.mincooldownHours = (double)taskConfig["mincooldownHours"]?.AsDouble(0);
            this.maxcooldownHours = (double)taskConfig["maxcooldownHours"]?.AsDouble(0);

            int initialmincooldown = (int)taskConfig["initialMinCoolDown"]?.AsInt(Mincooldown);
            int initialmaxcooldown = (int)taskConfig["initialMaxCoolDown"]?.AsInt(Maxcooldown);

            timeout = TimeSpan.FromSeconds(taskConfig["timeoutSec"]?.AsDouble(defaultTimeoutSec) ?? defaultTimeoutSec);

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

            this.WhenSwimming = taskConfig["whenSwimming"]?.AsBool();
            this.WhenInEmotionState = taskConfig["whenInEmotionState"].AsString();
            this.WhenNotInEmotionState = taskConfig["whenNotInEmotionState"].AsString();

            JsonObject soundCfg = taskConfig["sound"];
            if (soundCfg.Exists)
            {
                sound = AssetLocation.Create(soundCfg.AsString(), entity.Code.Domain).WithPathPrefixOnce("sounds/");
                soundRange = taskConfig["soundRange"].AsFloat(16);
                soundStartMs = taskConfig["soundStartMs"].AsInt(0);
                soundRepeatMs = taskConfig["soundRepeatMs"].AsInt(0);
            }
            JsonObject finishSoundCfg = taskConfig["finishSound"];
            if (finishSoundCfg.Exists)
            {
                finishSound = AssetLocation.Create(finishSoundCfg.AsString(), entity.Code.Domain).WithPathPrefixOnce("sounds/");
            }

            cooldownUntilMs = entity.World.ElapsedMilliseconds + initialmincooldown + entity.World.Rand.Next(initialmaxcooldown - initialmincooldown);
        }

        protected bool PreconditionsSatisifed()
        {
            if (WhenSwimming != null && WhenSwimming != entity.Swimming) return false;
            if (WhenInEmotionState != null && IsInEmotionState(WhenInEmotionState) != true) return false;
            if (WhenNotInEmotionState != null && IsInEmotionState(WhenNotInEmotionState) == true) return false;
            return true;
        }

        protected bool IsInEmotionState(string emostate)
        {
            if (bhEmo == null) return false;

            if (emostate.ContainsFast('|'))
            {
                var states = emostate.Split("|");
                for (int i = 0; i < states.Length; i++)
                {
                    if (bhEmo.IsInEmotionState(states[i])) return true;
                }
                return false;
            }

            return bhEmo.IsInEmotionState(emostate);
        }

        public virtual void AfterInitialize()
        {
        }

        public virtual int Slot
        {
            get { return slot; }
        }

        public virtual float Priority
        {
            get { return priority; }
            set { priority = value; }
        }

        public virtual float PriorityForCancel
        {
            get { return priorityForCancel; }
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
                        entity.World.PlaySoundAt(sound, entity.ServerPos.X, entity.ServerPos.InternalY, entity.ServerPos.Z, null, true, soundRange);
                        lastSoundTotalMs = entity.World.ElapsedMilliseconds;
                    }, soundStartMs);
                } else
                {
                    entity.World.PlaySoundAt(sound, entity.ServerPos.X, entity.ServerPos.InternalY, entity.ServerPos.Z, null, true, soundRange);
                    lastSoundTotalMs = entity.World.ElapsedMilliseconds;
                }
                
            }

            executeStartTimeMs = entity.World.ElapsedMilliseconds;
        }

        public virtual bool ContinueExecute(float dt)
        {
            if (sound != null && soundRepeatMs > 0 && entity.World.ElapsedMilliseconds > lastSoundTotalMs + soundRepeatMs)
            {
                entity.World.PlaySoundAt(sound, entity.ServerPos.X, entity.ServerPos.InternalY, entity.ServerPos.Z, null, true, soundRange);
                lastSoundTotalMs = entity.World.ElapsedMilliseconds;
            }

            return true;
        }

        public virtual void FinishExecute(bool cancelled)
        {
            cooldownUntilMs = entity.World.ElapsedMilliseconds + Mincooldown + entity.World.Rand.Next(Maxcooldown - Mincooldown);
            cooldownUntilTotalHours = entity.World.Calendar.TotalHours + mincooldownHours + entity.World.Rand.NextDouble() * (maxcooldownHours - mincooldownHours);

            // Ugly hack to fix attack animation sometimes not playing - it seems it gets stopped even before it gets sent to the client?
            if (animMeta != null && animMeta.Code != "attack" && animMeta.Code != "idle")
            {
                entity.AnimManager.StopAnimation(animMeta.Code);
            }

            if (finishSound != null)
            {
                entity.World.PlaySoundAt(finishSound, entity.ServerPos.X, entity.ServerPos.InternalY, entity.ServerPos.Z, null, true, soundRange);
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
                cooldownUntilMs = World.ElapsedMilliseconds + Mincooldown + World.Rand.Next(Maxcooldown - Mincooldown);
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

        protected virtual bool timeoutExceeded() => (entity.World.ElapsedMilliseconds - executeStartTimeMs) > timeout.TotalMilliseconds;
    }
}
