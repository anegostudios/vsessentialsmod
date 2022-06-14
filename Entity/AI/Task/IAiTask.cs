using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.API.Common
{
    public interface IAiTask
    {
        /// <summary>
        /// A unique identifer for this task
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Up to 8 tasks can be active concurrently. This number denotes the index of task. Most tasks run in slot 0.
        /// </summary>
        int Slot { get; }

        /// <summary>
        /// Return equal or below zero to not execute this action.
        /// Any value above zero is considered if the current executing task is of a lower priority
        /// </summary>
        /// <returns></returns>
        float Priority { get; }

        /// <summary>
        /// When the activitiy is active you may wanna give it a higher priority to finish executing
        /// </summary>
        float PriorityForCancel { get; }
        string ProfilerName { get; set; }

        /// <summary>
        /// Return true if this task should execute
        /// </summary>
        /// <returns></returns>
        bool ShouldExecute();

        /// <summary>
        /// Called the first time this task is considered active
        /// </summary>
        void StartExecute();

        /// <summary>
        /// Called every game time while this task is active. Return false to stop execution.
        /// </summary>
        /// <returns></returns>
        bool ContinueExecute(float dt);
        

        /// <summary>
        /// Called once execution has stopped. If cancelled is true, the task has been forcefully stopped because a higher priority task has to be executed
        /// </summary>
        /// <returns></returns>
        void FinishExecute(bool cancelled);

        /// <summary>
        /// Called when a behaviors has been loaded via entityconfig
        /// </summary>
        /// <param name="taskConfig">AI Task Specific config</param>
        /// <param name="aiConfig">All of the AI config</param>
        void LoadConfig(JsonObject taskConfig, JsonObject aiConfig);

        /// <summary>
        /// Called when the entity changed from active to inactive state and vice versa (inactive means no player is in its ticking range)
        /// </summary>
        /// <param name="beforeState"></param>
        void OnStateChanged(EnumEntityState beforeState);

        /// <summary>
        /// The notify event bubbled up from event.notify(). You may use this method in the same way as ShouldExecute(), i.e. return true if the task should start now. 
        /// May not start if a higher priority task in the same slot is currently running
        /// </summary>
        /// <param name="key"></param>
        /// <param name="data"></param>
        bool Notify(string key, object data);

        void OnEntityLoaded();
        void OnEntitySpawn();
        void OnEntityDespawn(EntityDespawnReason reason);
        void OnEntityHurt(DamageSource source, float damage);
        void OnNoPath(Vec3d target);

        /// <summary>
        /// Checks whether there is any reason why the AITask cannot move on to the ContinueExecute() method, for example if there is a path traverser and it is waiting for an asynchronous operation to complete
        /// </summary>
        /// <returns>True if the AITask can move on to ContinueExecute(); if it's not ready returns false</returns>
        bool CanContinueExecute();
    }
}
