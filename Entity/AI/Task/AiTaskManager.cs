using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public static class ApiTaskAdditions
{
    public static void RegisterAiTask<TTask>(this ICoreServerAPI serverAPI, string code) where TTask : IAiTask
    {
        AiTaskRegistry.Register<TTask>(code);
    }
}

public static class AiTaskRegistry
{
    public static readonly Dictionary<string, Type> TaskTypes = [];
    public static readonly Dictionary<Type, string> TaskCodes = [];

    public static void Register<TTask>(string code) where TTask : IAiTask
    {
        TaskTypes[code] = typeof(TTask);
        TaskCodes[typeof(TTask)] = code;
    }

    static AiTaskRegistry()
    {
        Register<AiTaskWander>("wander");
        Register<AiTaskLookAround>("lookaround");
        Register<AiTaskMeleeAttack>("meleeattack");
        Register<AiTaskSeekEntity>("seekentity");
        Register<AiTaskFleeEntity>("fleeentity");
        Register<AiTaskStayCloseToEntity>("stayclosetoentity");
        Register<AiTaskGetOutOfWater>("getoutofwater");
        Register<AiTaskIdle>("idle");
        Register<AiTaskSeekFoodAndEat>("seekfoodandeat");
        Register<AiTaskSeekBlockAndLay>("seekblockandlay");
        Register<AiTaskUseInventory>("useinventory");

        Register<AiTaskMeleeAttackTargetingEntity>("meleeattacktargetingentity");
        Register<AiTaskSeekTargetingEntity>("seektargetingentity");
        Register<AiTaskStayCloseToGuardedEntity>("stayclosetoguardedentity");

        Register<AiTaskJealousMeleeAttack>("jealousmeleeattack");
        Register<AiTaskJealousSeekEntity>("jealousseekentity");

        Register<AiTaskLookAtEntity>("lookatentity");
        Register<AiTaskGotoEntity>("gotoentity");

        // Refactored
        Register<AiTaskWanderR>("wander-r");
        Register<AiTaskBellAlarmR>("bellalarm-r");
        Register<AiTaskComeToOwnerR>("cometoowner-r");
        Register<AiTaskEatHeldItemR>("eathelditem-r");
        //Register<AiTaskFishMoveFastR>("fishmovefast-r"); // WIP
        //Register<AiTaskFishOutOfWaterR>("fishoutofwater-r"); // WIP
        Register<AiTaskFleeEntityR>("fleeentity-r");
        Register<AiTaskGetOutOfWaterR>("getoutofwater-r");
        Register<AiTaskIdleR>("idle-r");
        //Register<AiTaskJealousMeleeAttackR>("jealousmeleeattack-r"); // WIP
        //Register<AiTaskJealousSeekEntityR>("jealousseekentity-r"); // WIP
        Register<AiTaskLookAroundR>("lookaround-r");
        Register<AiTaskLookAtEntityR>("lookatentity-r");
        Register<AiTaskSeekFoodAndEatR>("seekfoodandeat-r");
        Register<AiTaskSeekEntityR>("seekentity-r");
        //Register<AiTaskSeekTargetingEntityR>("seektargetingentity-r"); // WIP
        Register<AiTaskShootAtEntityR>("shootatentity-r");
        Register<AiTaskStayCloseToEntityR>("stayclosetoentity-r");
        //Register<AiTaskStayCloseToGuardedEntityR>("stayclosetoguardedentity-r"); // WIP
        Register<AiTaskStayInRangeR>("stayinrange-r");
        Register<AiTaskTurretModeR>("turretmode-r");
        Register<AiTaskWanderR>("wander-r");
    }
}

public class AiRuntimeConfig : ModSystem
{
    public static bool RunAiTasks = true;
    public static bool RunAiActivities = true;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        api.Event.RegisterGameTickListener(onTick250ms, 250, 31);
    }

    private ICoreServerAPI? serverApi;
    private void onTick250ms(float obj)
    {
        RunAiTasks = serverApi?.World.Config.GetAsBool("runAiTasks", true) ?? true;
        RunAiActivities = serverApi?.World.Config.GetAsBool("runAiActivities", true) ?? true;
    }
}


public sealed class AiTaskManager(Entity entity)
{
    public event Action<IAiTask>? OnTaskStarted;
    public event Action<IAiTask>? OnTaskStopped;
    /// <summary>
    /// All delegates must return true to execute the task
    /// </summary>
    public event ActionBoolReturn<IAiTask>? OnShouldExecuteTask;

    public const int ActiveTasksSlotsNumber = 8;

    public bool Shuffle { get; set; }
    public IAiTask?[] ActiveTasksBySlot => activeTasksBySlot;
    public List<IAiTask> AllTasks => tasks;


    public void OnGameTick(float dt)
    {
        if (!AiRuntimeConfig.RunAiTasks)
        {
            if (wasRunAiTasks)
            {
                foreach (IAiTask? task in activeTasksBySlot)
                {
                    task?.FinishExecute(true);
                }
            }
            wasRunAiTasks = false;
            return;
        }
        wasRunAiTasks = true;

        if (Shuffle)
        {
            tasks.Shuffle(entity.World.Rand);
        }

        StartNewTasks();

        ProcessRunningTasks(dt);

        LogRunningTasks();
    }

    public void AddTask(IAiTask task)
    {
        tasks.Add(task);
        task.ProfilerName = "task-startexecute-" + AiTaskRegistry.TaskCodes[task.GetType()];
    }

    public void RemoveTask(IAiTask task)
    {
        tasks.Remove(task);
    }

    public void AfterInitialize()
    {
        foreach (IAiTask task in tasks)
        {
            task.AfterInitialize();
        }
    }

    public void ExecuteTask(IAiTask task, int slot)
    {
        task.StartExecute();
        activeTasksBySlot[slot] = task;

        if (entity.World.FrameProfiler.Enabled)
        {
            entity.World.FrameProfiler.Mark("task-startexecute-" + AiTaskRegistry.TaskCodes[task.GetType()]);
        }
    }

    public void ExecuteTask<TTask>() where TTask : IAiTask
    {
        foreach (TTask task in tasks.OfType<TTask>())
        {
            int slot = task.Slot;
            IAiTask? activeTask = activeTasksBySlot[slot];
            if (activeTask != null)
            {
                activeTask.FinishExecute(true);
                OnTaskStopped?.Invoke(activeTask);
            }

            activeTasksBySlot[slot] = task;
            task.StartExecute();
            OnTaskStarted?.Invoke(task);
            entity.World.FrameProfiler.Mark(task.ProfilerName);
        }
    }

    public TTask? GetTask<TTask>() where TTask : IAiTask => (TTask?)tasks.Find(task => task is TTask);

    public IAiTask? GetTask(string id) => tasks.Find(task => task.Id == id);

    public IEnumerable<TTask> GetTasks<TTask>() where TTask : IAiTask => tasks.OfType<TTask>();

    public void StopTask(Type taskType)
    {
        foreach (IAiTask? task in activeTasksBySlot)
        {
            if (task?.GetType() == taskType)
            {
                task.FinishExecute(true);
                OnTaskStopped?.Invoke(task);
                activeTasksBySlot[task.Slot] = null;
            }
        }

        entity.World.FrameProfiler.Mark("finishexecute");
    }

    public void StopTask<TTask>() where TTask : IAiTask
    {
        foreach (TTask task in activeTasksBySlot.OfType<TTask>())
        {
            task.FinishExecute(true);
            OnTaskStopped?.Invoke(task);
            activeTasksBySlot[task.Slot] = null;
        }

        entity.World.FrameProfiler.Mark("finishexecute");
    }

    public void StopTasks()
    {
        foreach (IAiTask? task in activeTasksBySlot)
        {
            if (task == null) continue;
            task.FinishExecute(true);
            OnTaskStopped?.Invoke(task);
            activeTasksBySlot[task.Slot] = null;
        }
    }

    public bool IsTaskActive(string id)
    {
        foreach (IAiTask? task in activeTasksBySlot)
        {
            if (task != null && task.Id == id) return true;
        }

        return false;
    }


    internal void Notify(string key, object data)
    {
        if (key == "starttask")
        {
            string taskId = (string)data;

            if (activeTasksBySlot.FirstOrDefault(task => task?.Id == taskId) != null) return;

            IAiTask? task = GetTask(taskId);
            if (task == null) return;

            IAiTask? activeTask = activeTasksBySlot[task.Slot];
            if (activeTask != null)
            {
                activeTask.FinishExecute(true);
                OnTaskStopped?.Invoke(activeTask);
            }
            activeTasksBySlot[task.Slot] = null;
            ExecuteTask(task, task.Slot);
            return;
        }

        if (key == "stoptask")
        {
            string taskId = (string)data;

            IAiTask? task = activeTasksBySlot.FirstOrDefault(task => task?.Id == taskId);
            if (task == null) return;

            task.FinishExecute(true);
            OnTaskStopped?.Invoke(task);
            activeTasksBySlot[task.Slot] = null;
            return;
        }

        for (int taskIndex = 0; taskIndex < tasks.Count; taskIndex++)
        {
            IAiTask task = tasks[taskIndex];

            if (task.Notify(key, data))
            {
                int slot = tasks[taskIndex].Slot;

                IAiTask? activeTask = activeTasksBySlot[slot];

                if (activeTask == null || task.Priority > activeTask.PriorityForCancel)
                {
                    if (activeTask != null)
                    {
                        activeTask.FinishExecute(true);
                        OnTaskStopped?.Invoke(activeTask);
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

    internal void OnEntityDespawn(EntityDespawnData reason)
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



    private readonly Entity entity = entity;
    private readonly List<IAiTask> tasks = [];
    private readonly IAiTask?[] activeTasksBySlot = new IAiTask[ActiveTasksSlotsNumber];
    private bool wasRunAiTasks;

    private void StartNewTasks()
    {
        foreach (IAiTask task in tasks)
        {
            if (task.Priority < 0) continue;

            int slot = task.Slot;
            IAiTask? oldTask = activeTasksBySlot[slot];
            if ((oldTask == null || task.Priority > oldTask.PriorityForCancel) && task.ShouldExecute() && ShouldExecuteTask(task))
            {
                oldTask?.FinishExecute(true);
                if (oldTask != null)
                {
                    OnTaskStopped?.Invoke(oldTask);
                }
                activeTasksBySlot[slot] = task;
                task.StartExecute();
                OnTaskStarted?.Invoke(task);
            }

            if (entity.World.FrameProfiler.Enabled)
            {
                entity.World.FrameProfiler.Mark(task.ProfilerName);
            }
        }
    }

    private bool ShouldExecuteTask(IAiTask task)
    {
        if (OnShouldExecuteTask == null) return true;

        bool exec = true;
        foreach (ActionBoolReturn<IAiTask> dele in OnShouldExecuteTask.GetInvocationList())
        {
            exec &= dele(task);
        }

        return exec;
    }

    private void ProcessRunningTasks(float dt)
    {
        for (int index = 0; index < activeTasksBySlot.Length; index++)
        {
            IAiTask? task = activeTasksBySlot[index];
            if (task == null) continue;
            if (!task.CanContinueExecute()) continue;

            if (!task.ContinueExecute(dt))
            {
                task.FinishExecute(false);
                OnTaskStopped?.Invoke(task);
                activeTasksBySlot[index] = null;
            }

            if (entity.World.FrameProfiler.Enabled)
            {
                entity.World.FrameProfiler.Mark("task-continueexec-" + AiTaskRegistry.TaskCodes[task.GetType()]);
            }
        }
    }

    private void LogRunningTasks()
    {
        if (!entity.World.EntityDebugMode) return;

        StringBuilder tasksInfo = new();

        int taskIndex = 0;
        for (int slotIndex = 0; slotIndex < activeTasksBySlot.Length; slotIndex++)
        {
            IAiTask? task = activeTasksBySlot[slotIndex];
            if (task == null) continue;
            if (taskIndex++ > 0) tasksInfo.Append(", ");

            AiTaskRegistry.TaskCodes.TryGetValue(task.GetType(), out string code);

            tasksInfo.Append($"{code}(p{task.Priority}, pc {task.PriorityForCancel})");
        }
        entity.DebugAttributes.SetString("AI Tasks", tasksInfo.Length > 0 ? tasksInfo.ToString() : "-");

    }
}
