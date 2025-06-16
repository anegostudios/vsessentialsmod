using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

#nullable disable

namespace Vintagestory.GameContent
{
    public class EntityBehaviorSemiTamed : EntityBehavior
    {
        public EntityBehaviorSemiTamed(Entity entity) : base(entity)
        {
            
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);
            entity.GetBehavior<EntityBehaviorTaskAI>().TaskManager.OnShouldExecuteTask += TaskManager_OnShouldExecuteTask;
        }

        private bool TaskManager_OnShouldExecuteTask(IAiTask t)
        {
            if (t is AiTaskFleeEntity fleetask)
            {
                return fleetask.WhenInEmotionState == null && fleetask.targetEntity is not EntityPlayer;
            }
            return true;
        }

        public override string PropertyName() => "semitamed";
    }
}
