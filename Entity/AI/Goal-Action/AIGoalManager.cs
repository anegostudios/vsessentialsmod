using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

#nullable disable

namespace Vintagestory.GameContent
{
    public static class AiGoalRegistry
    {
        public static Dictionary<string, Type> GoalTypes = new Dictionary<string, Type>();
        public static Dictionary<Type, string> GoalCodes = new Dictionary<Type, string>();

        public static Dictionary<string, Type> ActionTypes = new Dictionary<string, Type>();
        public static Dictionary<Type, string> ActionCodes = new Dictionary<Type, string>();


        public static void RegisterGoal<T>(string code) where T : AiGoalBase
        {
            GoalTypes[code] = typeof(T);
            GoalCodes[typeof(T)] = code;
        }

        public static void RegisterAction<T>(string code) where T : AiActionBase
        {
            ActionTypes[code] = typeof(T);
            ActionCodes[typeof(T)] = code;
        }



        static AiGoalRegistry()
        {
            
        }
    }


    public class AiGoalManager
    {
        Entity entity;
        List<AiGoalBase> Goals = new List<AiGoalBase>();
        AiGoalBase activeGoal;

        public AiGoalManager(Entity entity)
        {
            this.entity = entity;
        }

        public void AddGoal(AiGoalBase goal)
        {
            Goals.Add(goal);
        }

        public void RemoveGoal(AiGoalBase goal)
        {
            Goals.Remove(goal);
        }


        public void OnGameTick(float dt)
        {
            foreach (AiGoalBase newGoal in Goals)
            {
                if ((activeGoal == null || newGoal.Priority > activeGoal.PriorityForCancel) && newGoal.ShouldExecuteAll())
                {
                    activeGoal?.FinishExecuteAll(true);
                    activeGoal = newGoal;
                    newGoal.StartExecuteAll();
                }
            }


            if (activeGoal != null && !activeGoal.ContinueExecuteAll(dt))
            {
                activeGoal.FinishExecuteAll(false);
                activeGoal = null;
            }
            


            if (entity.World.EntityDebugMode)
            {
                string tasks = "";
                if (activeGoal != null) tasks += AiTaskRegistry.TaskCodes[activeGoal.GetType()] + "(" + activeGoal.Priority + ")";
                entity.DebugAttributes.SetString("AI Goal", tasks.Length > 0 ? tasks : "-");
            }
        }


        internal void Notify(string key, object data)
        {
            for (int i = 0; i < Goals.Count; i++)
            {
                AiGoalBase newGoal = Goals[i];

                if (newGoal.Notify(key, data))
                {
                    if ((newGoal == null || newGoal.Priority > activeGoal.PriorityForCancel))
                    {
                        activeGoal?.FinishExecuteAll(true);
                        activeGoal = newGoal;
                        newGoal.StartExecuteAll();
                    }
                }
            }
        }


        internal void OnStateChanged(EnumEntityState beforeState)
        {
            foreach (IAiTask task in Goals)
            {
                task.OnStateChanged(beforeState);
            }
        }
    }
}
