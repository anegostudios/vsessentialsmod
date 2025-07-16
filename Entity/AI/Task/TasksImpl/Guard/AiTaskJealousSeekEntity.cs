using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.GameContent
{
    public class AiTaskJealousSeekEntity : AiTaskSeekEntity
    {
        Entity guardedEntity;

        public AiTaskJealousSeekEntity(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
        }

        public override bool ShouldExecute()
        {
            if (entity.World.Rand.NextDouble() < 0.1)
            {
                guardedEntity = GetGuardedEntity();
            }

            if (guardedEntity == null) return false;

            return base.ShouldExecute();
        }

        public override bool IsTargetableEntity(Entity e, float range, bool ignoreEntityCode = false)
        {
            if (!base.IsTargetableEntity(e, range, ignoreEntityCode)) return false;

            return e.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.AllTasks?.FirstOrDefault(task => {
                return task is AiTaskStayCloseToGuardedEntity at && at.guardedEntity == guardedEntity;
            }) != null;
        }
    }
}
