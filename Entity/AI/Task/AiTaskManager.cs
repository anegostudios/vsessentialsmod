using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public static class ApiTaskAdditions
    {
        public static void RegisterAiTask(this ICoreServerAPI sapi, string code, Type type)
        {
            AiTaskRegistry.Register(code, type);
        }

        public static void RegisterAiTask<T>(this ICoreServerAPI sapi, string code) where T : AiTaskBase
        {
            AiTaskRegistry.Register<T>(code);
        }
    }


    public static class AiTaskRegistry
    {
        public static Dictionary<string, Type> TaskTypes = new Dictionary<string, Type>();
        public static Dictionary<Type, string> TaskCodes = new Dictionary<Type, string>();

        public static void Register(string code, Type type)
        {
            TaskTypes[code] = type;
            TaskCodes[type] = code;
        }

        public static void Register<T>(string code) where T : AiTaskBase
        {
            TaskTypes[code] = typeof(T);
            TaskCodes[typeof(T)] = code;
        }   

        static AiTaskRegistry()
        {
            Register("wander", typeof(AiTaskWander));
            Register("lookaround", typeof(AiTaskLookAround));
            Register("meleeattack", typeof(AiTaskMeleeAttack));
            Register("seekentity", typeof(AiTaskSeekEntity));
            Register("fleeentity", typeof(AiTaskFleeEntity));
            Register("stayclosetoentity", typeof(AiTaskStayCloseToEntity));
            Register("getoutofwater", typeof(AiTaskGetOutOfWater));
            Register("idle", typeof(AiTaskIdle));
            Register("seekfoodandeat", typeof(AiTaskSeekFoodAndEat));
            Register("seekblockandlay", typeof(AiTaskSeekBlockAndLay));
            Register("useinventory", typeof(AiTaskUseInventory));

            Register("meleeattacktargetingentity", typeof(AiTaskMeleeAttackTargetingEntity));
            Register("seektargetingentity", typeof(AiTaskSeekTargetingEntity));
            Register("stayclosetoguardedentity", typeof(AiTaskStayCloseToGuardedEntity));

            Register("jealousmeleeattack", typeof(AiTaskJealousMeleeAttack));
            Register("jealousseekentity", typeof(AiTaskJealousSeekEntity));
        }
    }


    public class AiTaskManager
    {
        public event Action<IAiTask> OnTaskStarted;
        public ActionBoolReturn<IAiTask> ShouldExecuteTask = (task) => true;

        Entity entity;
        List<IAiTask> tasks = new List<IAiTask>();
        IAiTask[] activeTasksBySlot = new IAiTask[8];

        public IAiTask[] ActiveTasksBySlot => activeTasksBySlot;
        public List<IAiTask> AllTasks => tasks;

        public AiTaskManager(Entity entity)
        {
            this.entity = entity;
        }

        public void AddTask(IAiTask task)
        {
            tasks.Add(task);
        }

        public void RemoveTask(IAiTask task)
        {
            tasks.Remove(task);
        }

        public void ExecuteTask(IAiTask task, int slot)
        {
            task.StartExecute();
            activeTasksBySlot[slot] = task;

            if (entity.World.FrameProfiler.Enabled)
            {
                entity.World.FrameProfiler.Mark("entity-ai-tasks-start-exec" + task.GetType());
            }
        }

        public T GetTask<T>() where T : IAiTask
        {
            foreach (IAiTask task in tasks)
            {
                if (task is T)
                {
                    return (T)task;
                }
            }

            return default(T);
        }

        public void ExecuteTask<T>() where T : IAiTask
        {
            foreach (IAiTask task in tasks)
            {
                if (task is T)
                {
                    int slot = task.Slot;

                    if (activeTasksBySlot[slot] != null)
                    {
                        activeTasksBySlot[slot].FinishExecute(true);
                    }

                    activeTasksBySlot[slot] = task;
                    task.StartExecute();
                    OnTaskStarted?.Invoke(task);
                }
            }

            entity.World.FrameProfiler.Mark("entity-ai-tasks-start-execg");
        }

        

        public void StopTask(Type taskType)
        {
            foreach (IAiTask task in activeTasksBySlot)
            {
                if (task?.GetType() == taskType)
                {
                    task.FinishExecute(true);
                    activeTasksBySlot[task.Slot] = null;
                }
            }

            entity.World.FrameProfiler.Mark("entity-ai-tasks-fin-exec");
        }

        public void OnGameTick(float dt)
        {
            entity.World.FrameProfiler.Mark("entity-ai-tasks-tick-begin");

            foreach (IAiTask task in tasks)
            {
                int slot = task.Slot;

                if ((activeTasksBySlot[slot] == null || task.Priority > activeTasksBySlot[slot].PriorityForCancel) && task.ShouldExecute() && ShouldExecuteTask(task))
                {
                    if (activeTasksBySlot[slot] != null)
                    {
                        activeTasksBySlot[slot].FinishExecute(true);
                    }

                    activeTasksBySlot[slot] = task;
                    task.StartExecute();
                    OnTaskStarted?.Invoke(task);
                }

                if (entity.World.FrameProfiler.Enabled)
                {
                    entity.World.FrameProfiler.Mark("entity-ai-tasks-tick-start-exec" + task.GetType());
                }
            }

            entity.World.FrameProfiler.Mark("entity-ai-tasks-tick-begin-cont");

            for (int i = 0; i < activeTasksBySlot.Length; i++)
            {
                IAiTask task = activeTasksBySlot[i];
                if (task == null) continue;

                if (!task.ContinueExecute(dt))
                {
                    task.FinishExecute(false);
                    activeTasksBySlot[i] = null;
                }

                if (entity.World.FrameProfiler.Enabled)
                {
                    entity.World.FrameProfiler.Mark("entity-ai-tasks-tick-cont-" + task.GetType());
                }
            }


            entity.World.FrameProfiler.Mark("entity-ai-tasks-tick-cont-exec");


            if (entity.World.EntityDebugMode)
            {
                string tasks = "";
                int j = 0;
                for (int i = 0; i < activeTasksBySlot.Length; i++)
                {
                    IAiTask task = activeTasksBySlot[i];
                    if (task == null) continue;
                    if (j++ > 0) tasks += ", ";

                    string code;
                    AiTaskRegistry.TaskCodes.TryGetValue(task.GetType(), out code);

                    tasks += code + "(p"+task.Priority+", pc"+task.PriorityForCancel+")";
#if DEBUG
                    // temporary for debugging
                    if (entity.Properties.Habitat == EnumHabitat.Underwater && task is AiTaskWander wand)
                    {
                        tasks += String.Format(" Heading to: {0:0.00},{1:0.00},{2:0.00}", wand.MainTarget.X - 500000, wand.MainTarget.Y, wand.MainTarget.Z - 500000);
                    }
#endif

                }
                entity.DebugAttributes.SetString("AI Tasks", tasks.Length > 0 ? tasks : "-");
            }
        }

        public bool IsTaskActive(string id)
        {
            foreach (var val in activeTasksBySlot)
            {
                if (val != null && val.Id == id) return true;
            }

            return false;
        }

        internal void Notify(string key, object data)
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                IAiTask task = tasks[i];

                if (task.Notify(key, data))
                {
                    int slot = tasks[i].Slot;

                    if ((activeTasksBySlot[slot] == null || task.Priority > activeTasksBySlot[slot].PriorityForCancel))
                    {
                        if (activeTasksBySlot[slot] != null)
                        {
                            activeTasksBySlot[slot].FinishExecute(true);
                        }

                        activeTasksBySlot[slot] = task;
                        task.StartExecute();
                        OnTaskStarted?.Invoke(task);
                    }
                }
            }
        }

        internal void OnStateChanged(EnumEntityState beforeState)
        {
            foreach (IAiTask task in tasks)
            {
                task.OnStateChanged(beforeState);
            }
        }

        internal void OnEntitySpawn()
        {
            foreach (IAiTask task in tasks)
            {
                task.OnEntitySpawn();
            }
        }

        internal void OnEntityLoaded()
        {
            foreach (IAiTask task in tasks)
            {
                task.OnEntityLoaded();
            }
        }

        internal void OnEntityDespawn(EntityDespawnReason reason)
        {
            foreach (IAiTask task in tasks)
            {
                task.OnEntityDespawn(reason);
            }
        }


        internal void OnEntityHurt(DamageSource source, float damage)
        {
            foreach (IAiTask task in tasks)
            {
                task.OnEntityHurt(source, damage);
            }
        }

        internal void OnNoPath(Vec3d target)
        {
            foreach (IAiTask task in tasks)
            {
                task.OnNoPath(target);
            }
        }
    }
}
