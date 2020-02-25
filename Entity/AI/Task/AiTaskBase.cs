using System;
using System.Threading;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.API.Common
{
    public abstract class AiTaskBase : IAiTask
    {
        public ThreadLocal<Random> randTL;
        public Random rand => randTL.Value;
        public EntityAgent entity;
        public IWorldAccessor world;
        public AnimationMetaData animMeta;

        protected float priority;
        protected float priorityForCancel;
        protected int slot;
        protected int mincooldown;
        protected int maxcooldown;

        protected double mincooldownHours;
        protected double maxcooldownHours;

        protected AssetLocation sound;
        protected float soundRange;
        protected int soundStartMs;
        protected float soundChance=1.01f;

        protected string whenInEmotionState;
        protected string whenNotInEmotionState;

        protected long cooldownUntilMs;
        protected double cooldownUntilTotalHours;

        protected PathTraverserBase pathTraverser;
        

        public AiTaskBase(EntityAgent entity)
        {
            this.entity = entity;
            this.world = entity.World;
            randTL = new ThreadLocal<Random>(() => new Random((int)entity.EntityId));

            this.pathTraverser = entity.GetBehavior<EntityBehaviorTaskAI>().PathTraverser; 
        }

        public virtual void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            this.priority = taskConfig["priority"].AsFloat();
            this.priorityForCancel = taskConfig["priorityForCancel"].AsFloat(priority);


            this.slot = (int)taskConfig["slot"]?.AsInt(0);
            this.mincooldown = (int)taskConfig["mincooldown"]?.AsInt(0);
            this.maxcooldown = (int)taskConfig["maxcooldown"]?.AsInt(100);

            this.mincooldownHours = (double)taskConfig["mincooldownHours"]?.AsDouble(0);
            this.maxcooldownHours = (double)taskConfig["maxcooldownHours"]?.AsDouble(0);

            int initialmincooldown = (int)taskConfig["initialMinCoolDown"]?.AsInt(mincooldown);
            int initialmaxcooldown = (int)taskConfig["initialMaxCoolDown"]?.AsInt(maxcooldown);

            if (taskConfig["animation"].Exists)
            {
                animMeta = new AnimationMetaData()
                {
                    Code = taskConfig["animation"].AsString()?.ToLowerInvariant(),
                    Animation = taskConfig["animation"].AsString()?.ToLowerInvariant(),
                    AnimationSpeed = taskConfig["animationSpeed"].AsFloat(1f)
                }.Init();
            }

            this.whenInEmotionState = taskConfig["whenInEmotionState"].AsString();
            this.whenNotInEmotionState = taskConfig["whenNotInEmotionState"].AsString();


            if (taskConfig["sound"].Exists)
            {
                sound = AssetLocation.Create(taskConfig["sound"].AsString(), entity.Code.Domain).WithPathPrefixOnce("sounds/");
                soundRange = taskConfig["soundRange"].AsFloat(16);
                soundStartMs = taskConfig["soundStartMs"].AsInt(0);
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
                animMeta.EaseInSpeed = 1f;
                animMeta.EaseOutSpeed = 1f;
                entity.AnimManager.StartAnimation(animMeta);
                //Console.WriteLine("entity id " + entity.EntityId + " ai task start anim " + animMeta.Code);
            }

            if (sound != null && entity.World.Rand.NextDouble() <= soundChance)
            {
                if (soundStartMs > 0)
                {
                    entity.World.RegisterCallback((dt) => {
                        entity.World.PlaySoundAt(sound, entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z, null, true, soundRange);
                    }, soundStartMs);
                } else
                {
                    entity.World.PlaySoundAt(sound, entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z, null, true, soundRange);
                }
                
            }
        }

        public virtual bool ContinueExecute(float dt)
        {
            return true;
        }

        public virtual void FinishExecute(bool cancelled)
        {
            cooldownUntilMs = entity.World.ElapsedMilliseconds + mincooldown + entity.World.Rand.Next(maxcooldown - mincooldown);
            cooldownUntilTotalHours = entity.World.Calendar.TotalHours + mincooldownHours + entity.World.Rand.NextDouble() * (maxcooldownHours - mincooldownHours);

            // Ugly hack to fix attack animation sometimes not playing - it seems it gets stopped even before it gets sent to the client?
            if (animMeta != null && animMeta.Code != "attack" && animMeta.Code != "idle")
            {
                //Console.WriteLine("entity id " + entity.EntityId +" ai task stop anim " + animMeta.Code);
                entity.AnimManager.StopAnimation(animMeta.Code);
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

        public virtual void OnEntityDespawn(EntityDespawnReason reason)
        {

        }

        public virtual void OnEntityHurt(DamageSource source, float damage)
        {
            
        }
    }
}
