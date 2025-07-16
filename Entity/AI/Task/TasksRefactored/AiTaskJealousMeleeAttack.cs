using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

public class AiTaskJealousMeleeAttackR : AiTaskMeleeAttackR
{
    protected Entity? guardedEntity;

    public AiTaskJealousMeleeAttackR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
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

    protected override bool IsTargetableEntity(Entity e, float range)
    {
        if (!base.IsTargetableEntity(e, range)) return false;

        return e.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.AllTasks?.Find(task => {
            return task is AiTaskStayCloseToGuardedEntityR at && at.guardedEntity == guardedEntity;
        }) != null;
    }
}