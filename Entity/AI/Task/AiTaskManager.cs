using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent
{
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
            Register("useinventory", typeof(AiTaskUseInventory));
        }
    }


    public class AiTaskManager
    {
        public event API.Common.Action<IAiTask> OnTaskStarted;
        public ActionBoolReturn<IAiTask> ShouldExecuteTask = (task) => true;

        Entity entity;
        List<IAiTask> Tasks = new List<IAiTask>();
        IAiTask[] ActiveTasksBySlot = new IAiTask[8];       


        public AiTaskManager(Entity entity)
        {
            this.entity = entity;
        }

        public void AddTask(IAiTask task)
        {
            Tasks.Add(task);
        }

        public void RemoveTask(IAiTask task)
        {
            Tasks.Remove(task);
        }

        public void ExecuteTask(IAiTask task, int slot)
        {
            task.StartExecute();
            ActiveTasksBySlot[slot] = task;

            //entity.World.FrameProfiler.Mark("entity-ai-tasks-start-exec" + task.GetType());
        }

        public T GetTask<T>() where T : IAiTask
        {
            foreach (IAiTask task in Tasks)
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
            foreach (IAiTask task in Tasks)
            {
                if (task is T)
                {
                    int slot = task.Slot;

                    if (ActiveTasksBySlot[slot] != null)
                    {
                        ActiveTasksBySlot[slot].FinishExecute(true);
                    }

                    ActiveTasksBySlot[slot] = task;
                    task.StartExecute();
                    OnTaskStarted?.Invoke(task);
                }
            }

            entity.World.FrameProfiler.Mark("entity-ai-tasks-start-execg");
        }


        public void StopTask(Type taskType)
        {
            foreach (IAiTask task in ActiveTasksBySlot)
            {
                if (task?.GetType() == taskType)
                {
                    task.FinishExecute(true);
                }
            }

            entity.World.FrameProfiler.Mark("entity-ai-tasks-fin-exec");
        }

        public void OnGameTick(float dt)
        {
            entity.World.FrameProfiler.Mark("entity-ai-tasks-tick-begin");

            foreach (IAiTask task in Tasks)
            {
                int slot = task.Slot;

                if ((ActiveTasksBySlot[slot] == null || task.Priority > ActiveTasksBySlot[slot].PriorityForCancel) && task.ShouldExecute() && ShouldExecuteTask(task))
                {
                    if (ActiveTasksBySlot[slot] != null)
                    {
                        ActiveTasksBySlot[slot].FinishExecute(true);
                    }

                    ActiveTasksBySlot[slot] = task;
                    task.StartExecute();
                    OnTaskStarted?.Invoke(task);
                }

                if (entity.World.FrameProfiler.Enabled)
                {
                    entity.World.FrameProfiler.Mark("entity-ai-tasks-tick-start-exec" + task.GetType());
                }
            }

            entity.World.FrameProfiler.Mark("entity-ai-tasks-tick-begin-cont");

            for (int i = 0; i < ActiveTasksBySlot.Length; i++)
            {
                IAiTask task = ActiveTasksBySlot[i];
                if (task == null) continue;

                if (!task.ContinueExecute(dt))
                {
                    task.FinishExecute(false);
                    ActiveTasksBySlot[i] = null;
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
                for (int i = 0; i < ActiveTasksBySlot.Length; i++)
                {
                    IAiTask task = ActiveTasksBySlot[i];
                    if (task == null) continue;
                    if (j++ > 0) tasks += ", ";

                    string code;
                    AiTaskRegistry.TaskCodes.TryGetValue(task.GetType(), out code);

                    tasks += code + "("+task.Priority+")";
                }
                entity.DebugAttributes.SetString("AI Tasks", tasks.Length > 0 ? tasks : "-");
            }
        }
        

        internal void Notify(string key, object data)
        {
            for (int i = 0; i < Tasks.Count; i++)
            {
                IAiTask task = Tasks[i];

                if (task.Notify(key, data))
                {
                    int slot = Tasks[i].Slot;

                    if ((ActiveTasksBySlot[slot] == null || task.Priority > ActiveTasksBySlot[slot].PriorityForCancel))
                    {
                        if (ActiveTasksBySlot[slot] != null)
                        {
                            ActiveTasksBySlot[slot].FinishExecute(true);
                        }

                        ActiveTasksBySlot[slot] = task;
                        task.StartExecute();
                        OnTaskStarted?.Invoke(task);
                    }
                }
            }
        }

        internal void OnStateChanged(EnumEntityState beforeState)
        {
            foreach (IAiTask task in Tasks)
            {
                task.OnStateChanged(beforeState);
            }
        }

        internal void OnEntitySpawn()
        {
            foreach (IAiTask task in Tasks)
            {
                task.OnEntitySpawn();
            }
        }

        internal void OnEntityLoaded()
        {
            foreach (IAiTask task in Tasks)
            {
                task.OnEntityLoaded();
            }
        }

        internal void OnEntityDespawn(EntityDespawnReason reason)
        {
            foreach (IAiTask task in Tasks)
            {
                task.OnEntityDespawn(reason);
            }
        }


        internal void OnEntityHurt(DamageSource source, float damage)
        {
            foreach (IAiTask task in Tasks)
            {
                task.OnEntityHurt(source, damage);
            }
        }
        
    }
}
