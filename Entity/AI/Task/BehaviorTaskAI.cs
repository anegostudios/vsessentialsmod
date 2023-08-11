using System;
using System.Text;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Essentials;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorTaskAI : EntityBehavior
    {
        public AiTaskManager TaskManager;
        public PathTraverserBase PathTraverser;

        public EntityBehaviorTaskAI(Entity entity) : base(entity)
        {
            TaskManager = new AiTaskManager(entity);
        }

        public override void OnEntitySpawn()
        {
            base.OnEntitySpawn();

            TaskManager.OnEntitySpawn();
        }

        public override void OnEntityLoaded()
        {
            base.OnEntityLoaded();

            TaskManager.OnEntityLoaded();
        }

        public override void OnEntityDespawn(EntityDespawnData reason)
        {
            base.OnEntityDespawn(reason);

            TaskManager.OnEntityDespawn(reason);
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            base.OnEntityReceiveDamage(damageSource, ref damage);

            TaskManager.OnEntityHurt(damageSource, damage);
        }

        public override void Initialize(EntityProperties properties, JsonObject aiconfig)
        {
            if (!(entity is EntityAgent))
            {
                entity.World.Logger.Error("The task ai currently only works on entities inheriting from EntityAgent. Will ignore loading tasks for entity {0} ", entity.Code);
                return;
            }

            PathTraverser = new WaypointsTraverser(entity as EntityAgent);

            JsonObject[] tasks = aiconfig["aitasks"]?.AsArray();
            if (tasks == null) return;

            foreach (JsonObject taskConfig in tasks) 
            {
                string taskCode = taskConfig["code"]?.AsString();
                bool enabled = taskConfig["enabled"].AsBool(true);
                if (!enabled)
                {
                    continue;
                }

                Type taskType = null;
                if (!AiTaskRegistry.TaskTypes.TryGetValue(taskCode, out taskType))
                {
                    entity.World.Logger.Error("Task with code {0} for entity {1} does not exist. Ignoring.", taskCode, entity.Code);
                    continue;
                }

                IAiTask task = (IAiTask)Activator.CreateInstance(taskType, (EntityAgent)entity);

                try
                {
                    task.LoadConfig(taskConfig, aiconfig);
                } catch (Exception)
                {
                    entity.World.Logger.Error("Task with code {0} for entity {1}: Unable to load json code.", taskCode, entity.Code);
                    throw;
                }

                TaskManager.AddTask(task);
            }
        }

        public override void AfterInitialized(bool onSpawn)
        {
            TaskManager.AfterInitialize();
        }


        public override void OnGameTick(float deltaTime)
        {
            // AI is only running for active entities
            if (entity.State != EnumEntityState.Active || !entity.Alive) return;
            entity.World.FrameProfiler.Mark("ai-init");

            PathTraverser.OnGameTick(deltaTime);

            entity.World.FrameProfiler.Mark("ai-pathfinding");


            entity.World.FrameProfiler.Enter("ai-tasks");

            TaskManager.OnGameTick(deltaTime);

            entity.World.FrameProfiler.Leave();
        }


        public override void OnStateChanged(EnumEntityState beforeState, ref EnumHandling handled)
        {
            TaskManager.OnStateChanged(beforeState);
        }


        

        public override void Notify(string key, object data)
        {
            TaskManager.Notify(key, data);
        }

        public override void GetInfoText(StringBuilder infotext)
        {
            base.GetInfoText(infotext);
        }

        public override void OnNoPath(Vec3d target)
        {
            TaskManager.OnNoPath(target);
        }

        public override string PropertyName()
        {
            return "taskai";
        }
    }
}
