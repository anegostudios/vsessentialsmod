using System;
using System.Threading;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using System.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

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
        protected int mincooldown;
        protected int maxcooldown;

        protected double mincooldownHours;
        protected double maxcooldownHours;

        protected AssetLocation finishSound;
        protected AssetLocation sound;
        protected float soundRange = 16;
        protected int soundStartMs;
        protected int soundRepeatMs;
        protected float soundChance=1.01f;
        protected long lastSoundTotalMs;

        protected string whenInEmotionState;
        protected string whenNotInEmotionState;

        protected long cooldownUntilMs;
        protected double cooldownUntilTotalHours;

        protected PathTraverserBase pathTraverser;

        protected EntityBehaviorEmotionStates bhEmo;

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
            this.mincooldown = (int)taskConfig["mincooldown"]?.AsInt(0);
            this.maxcooldown = (int)taskConfig["maxcooldown"]?.AsInt(100);

            this.mincooldownHours = (double)taskConfig["mincooldownHours"]?.AsDouble(0);
            this.maxcooldownHours = (double)taskConfig["maxcooldownHours"]?.AsDouble(0);

            int initialmincooldown = (int)taskConfig["initialMinCoolDown"]?.AsInt(mincooldown);
            int initialmaxcooldown = (int)taskConfig["initialMaxCoolDown"]?.AsInt(maxcooldown);

            if (taskConfig["animation"].Exists)
            {
                var code = taskConfig["animation"].AsString()?.ToLowerInvariant();
                float speed = taskConfig["animationSpeed"].AsFloat(1f);

                var cmeta = this.entity.Properties.Client.Animations.FirstOrDefault(a => a.Code == code);
                if (cmeta != null)
                {
                    if (taskConfig["animationSpeed"].Exists)
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

            this.whenInEmotionState = taskConfig["whenInEmotionState"].AsString();
            this.whenNotInEmotionState = taskConfig["whenNotInEmotionState"].AsString();


            if (taskConfig["sound"].Exists)
            {
                sound = AssetLocation.Create(taskConfig["sound"].AsString(), entity.Code.Domain).WithPathPrefixOnce("sounds/");
                soundRange = taskConfig["soundRange"].AsFloat(16);
                soundStartMs = taskConfig["soundStartMs"].AsInt(0);
                soundRepeatMs = taskConfig["soundRepeatMs"].AsInt(0);
            }
            if (taskConfig["finishSound"].Exists)
            {
                finishSound = AssetLocation.Create(taskConfig["finishSound"].AsString(), entity.Code.Domain).WithPathPrefixOnce("sounds/");
            }

            cooldownUntilMs = entity.World.ElapsedMilliseconds + initialmincooldown + entity.World.Rand.Next(initialmaxcooldown - initialmincooldown);
        }

        public virtual int Slot
        {
            get { return slot; }
        }

        public virtual float Priority
        {
            get { return priority; }
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
                        entity.World.PlaySoundAt(sound, entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z, null, true, soundRange);
                        lastSoundTotalMs = entity.World.ElapsedMilliseconds;
                    }, soundStartMs);
                } else
                {
                    entity.World.PlaySoundAt(sound, entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z, null, true, soundRange);
                    lastSoundTotalMs = entity.World.ElapsedMilliseconds;
                }
                
            }
        }

        public virtual bool ContinueExecute(float dt)
        {
            if (sound != null && soundRepeatMs > 0 && entity.World.ElapsedMilliseconds > lastSoundTotalMs + soundRepeatMs)
            {
                entity.World.PlaySoundAt(sound, entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z, null, true, soundRange);
                lastSoundTotalMs = entity.World.ElapsedMilliseconds;
            }

            return true;
        }

        public virtual void FinishExecute(bool cancelled)
        {
            cooldownUntilMs = entity.World.ElapsedMilliseconds + mincooldown + entity.World.Rand.Next(maxcooldown - mincooldown);
            cooldownUntilTotalHours = entity.World.Calendar.TotalHours + mincooldownHours + entity.World.Rand.NextDouble() * (maxcooldownHours - mincooldownHours);

            // Ugly hack to fix attack animation sometimes not playing - it seems it gets stopped even before it gets sent to the client?
            if (animMeta != null && animMeta.Code != "attack" && animMeta.Code != "idle")
            {
                entity.AnimManager.StopAnimation(animMeta.Code);
            }

            if (finishSound != null)
            {
                entity.World.PlaySoundAt(finishSound, entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z, null, true, soundRange);
            }
        }


        public virtual void OnStateChanged(EnumEntityState beforeState)
        {
            // Reset timer because otherwise the tasks will always be executed upon entering active state
            if (entity.State == EnumEntityState.Active)
            {
                cooldownUntilMs = entity.World.ElapsedMilliseconds + mincooldown + entity.World.Rand.Next(maxcooldown - mincooldown);
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
    }
}
