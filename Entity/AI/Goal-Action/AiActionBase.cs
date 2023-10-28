using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public abstract class AiActionBase
    {
        public Random rand;
        public EntityAgent entity;
        public IWorldAccessor world;
        public AnimationMetaData animMeta;

        protected string sound;
        protected float soundRange;
        protected int soundStartMs;
        protected float soundChance = 1.01f;

        protected PathTraverserBase pathTraverser;

        protected double secondsActive;


        public AiActionBase(EntityAgent entity)
        {
            this.entity = entity;
            this.world = entity.World;
            rand = new Random((int)entity.EntityId);

            this.pathTraverser = entity.GetBehavior<EntityBehaviorTaskAI>().PathTraverser;
        }

        public virtual void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            JsonObject animationCfg = taskConfig["animation"];
            if (animationCfg.Exists)
            {
                string code = animationCfg.AsString()?.ToLowerInvariant();
                animMeta = new AnimationMetaData()
                {
                    Code = code,
                    Animation = code,
                    AnimationSpeed = taskConfig["animationSpeed"].AsFloat(1f)
                }.Init();
            }

            JsonObject soundCfg = taskConfig["sound"];
            if (soundCfg.Exists)
            {
                sound = soundCfg.AsString();
                soundRange = taskConfig["soundRange"].AsFloat(16);
                soundStartMs = taskConfig["soundStartMs"].AsInt(0);
            }
        }

        public virtual bool ShouldExecuteAll()
        {
            soundChance = Math.Min(1.01f, soundChance + 1 / 500f);


            return ShouldExecute();
        }

        internal abstract bool ShouldExecute();

        protected virtual void StartExecute()
        {

        }

        public virtual void StartExecuteAll()
        {
            if (animMeta != null)
            {
                entity.AnimManager.StartAnimation(animMeta);
            }

            if (sound != null)
            {
                if (entity.World.Rand.NextDouble() <= soundChance)
                {
                    if (soundStartMs == 0)
                    {
                        entity.World.PlaySoundAt(new AssetLocation("sounds/" + sound), entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z, null, true, soundRange);
                    }
                } else
                {
                    soundStartMs = 0;
                }

                soundChance = Math.Max(0.025f, soundChance - 0.2f);
            }

            StartExecute();
        }

        public bool ContinueExecuteAll(float dt)
        {
            secondsActive += dt;

            if (soundStartMs > 0 && secondsActive > soundStartMs * 1000)
            {
                entity.World.PlaySoundAt(new AssetLocation("sounds/" + sound), entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z, null, true, soundRange);
            }

            return ContinueExecute(dt);
        }

        protected abstract bool ContinueExecute(float dt);


        public virtual void FinishExecuteAll(bool cancelled)
        {
            if (animMeta != null)
            {
                entity.AnimManager.StopAnimation(animMeta.Code);
            }

            FinishExecute(cancelled);
        }

        protected virtual void FinishExecute(bool cancelled)
        {
            
        }
    }
}
