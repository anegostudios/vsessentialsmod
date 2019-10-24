using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Vintagestory.API.Common
{
    public enum EnumGoalTrigger
    {
        Never,
        Always,
        OnHurt
    }

    public abstract class AiGoalBase
    {
        public Random rand;
        public EntityAgent entity;
        public IWorldAccessor world;

        protected float priority;
        protected float priorityForCancel;
        protected int mincooldown;
        protected int maxcooldown;

        protected double mincooldownHours;
        protected double maxcooldownHours;
        

        protected long cooldownUntilMs;
        protected double cooldownUntilTotalHours;

        protected PathTraverserBase pathTraverser;

        //AiActionBase[] actions;
        Queue<AiActionBase> activeActions = new Queue<AiActionBase>();


        public AiGoalBase(EntityAgent entity)
        {
            this.entity = entity;
            this.world = entity.World;
            rand = new Random((int)entity.EntityId);

            this.pathTraverser = entity.GetBehavior<EntityBehaviorGoalAI>().PathTraverser;
        }

        public virtual void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            this.priority = taskConfig["priority"].AsFloat();
            this.priorityForCancel = taskConfig["priorityForCancel"].AsFloat(priority);
            this.mincooldown = (int)taskConfig["mincooldown"]?.AsInt(0);
            this.maxcooldown = (int)taskConfig["maxcooldown"]?.AsInt(100);

            this.mincooldownHours = (double)taskConfig["mincooldownHours"]?.AsDouble(0);
            this.maxcooldownHours = (double)taskConfig["maxcooldownHours"]?.AsDouble(0);

            int initialmincooldown = (int)taskConfig["initialMinCoolDown"]?.AsInt(mincooldown);
            int initialmaxcooldown = (int)taskConfig["initialMaxCoolDown"]?.AsInt(maxcooldown);
            
            cooldownUntilMs = entity.World.ElapsedMilliseconds + initialmincooldown + entity.World.Rand.Next(initialmaxcooldown - initialmincooldown);
        }
        

        public virtual float Priority
        {
            get { return priority; }
        }

        public virtual float PriorityForCancel
        {
            get { return priorityForCancel; }
        }


        public virtual bool ShouldExecuteAll()
        {
            if (cooldownUntilMs > entity.World.ElapsedMilliseconds) return false;
            if (cooldownUntilTotalHours > entity.World.Calendar.TotalHours) return false;

            

            /*for (int i = 0; i < actions.Length; i++)
            {
                
            }*/

            return ShouldExecute();
        }

        protected abstract bool ShouldExecute();



        public virtual void StartExecuteAll()
        {
            StartExecute();
        }

        protected virtual void StartExecute() { }


        public virtual bool ContinueExecuteAll(float dt)
        {
            return ContinueExecute(dt);
        }

        protected abstract bool ContinueExecute(float dt);



        public virtual void FinishExecuteAll(bool cancelled)
        {
            cooldownUntilMs = entity.World.ElapsedMilliseconds + mincooldown + entity.World.Rand.Next(maxcooldown - mincooldown);
            cooldownUntilTotalHours = entity.World.Calendar.TotalHours + mincooldownHours + entity.World.Rand.NextDouble() * (maxcooldownHours - mincooldownHours);

            FinishExecute(cancelled);
        }

        protected virtual void FinishExecute(bool cancelled) { }



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
    }
}
