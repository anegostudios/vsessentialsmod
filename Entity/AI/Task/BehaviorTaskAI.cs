using System;
using System.Text;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Essentials;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorTaskAI : EntityBehavior
    {
        public AiTaskManager taskManager;
        public PathTraverserBase PathTraverser;

        public EntityBehaviorTaskAI(Entity entity) : base(entity)
        {
            taskManager = new AiTaskManager(entity);
        }

        public override void OnEntitySpawn()
        {
            base.OnEntitySpawn();

            taskManager.OnEntitySpawn();
        }

        public override void OnEntityLoaded()
        {
            base.OnEntityLoaded();

            taskManager.OnEntityLoaded();
        }

        public override void OnEntityDespawn(EntityDespawnReason reason)
        {
            base.OnEntityDespawn(reason);

            taskManager.OnEntityDespawn(reason);
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, float damage)
        {
            base.OnEntityReceiveDamage(damageSource, damage);

            taskManager.OnEntityHurt(damageSource, damage);
        }

        public override void Initialize(EntityProperties properties, JsonObject aiconfig)
        {
            if (!(entity is EntityAgent))
            {
                entity.World.Logger.Error("The task ai currently only works on entities inheriting from EntityAgent. Will ignore loading tasks for entity {0} ", entity.Code);
                return;
            }

            //PathTraverser = new StraightLineTraverser(entity as EntityAgent);
            PathTraverser = new WaypointsTraverser(entity as EntityAgent);

            JsonObject[] tasks = aiconfig["aitasks"]?.AsArray();
            if (tasks == null) return;

            foreach(JsonObject taskConfig in tasks) 
            {
                string taskCode = taskConfig["code"]?.AsString();
                Type taskType = null;
                if (!AiTaskRegistry.TaskTypes.TryGetValue(taskCode, out taskType))
                {
                    entity.World.Logger.Error("Task with code {0} for entity {1} does not exist. Ignoring.", taskCode, entity.Code);
                    continue;
                }

                IAiTask task = (IAiTask)Activator.CreateInstance(taskType, (EntityAgent)entity);
                task.LoadConfig(taskConfig, aiconfig);

                taskManager.AddTask(task);
            }
        }


        public override void OnGameTick(float deltaTime)
        {
            // AI is only running for active entities
            if (entity.State != EnumEntityState.Active || !entity.Alive) return;

            PathTraverser.OnGameTick(deltaTime);

            entity.World.FrameProfiler.Mark("entity-ai-pathing");

            taskManager.OnGameTick(deltaTime);

            entity.World.FrameProfiler.Mark("entity-ai-tasks");
        }


        public override void OnStateChanged(EnumEntityState beforeState, ref EnumHandling handled)
        {
            taskManager.OnStateChanged(beforeState);
        }


        

        public override void Notify(string key, object data)
        {
            taskManager.Notify(key, data);
        }

        public override void GetInfoText(StringBuilder infotext)
        {
            base.GetInfoText(infotext);
        }

        public override string PropertyName()
        {
            return "taskai";
        }
    }
}
