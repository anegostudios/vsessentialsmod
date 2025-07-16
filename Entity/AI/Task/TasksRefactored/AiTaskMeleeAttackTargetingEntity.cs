using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.GameContent
{
    public class AiTaskMeleeAttackTargetingEntityR : AiTaskMeleeAttackR
    {
        Entity guardedEntity;
        Entity lastattackingEntity;
        long lastattackingEntityFoundMs;

        public AiTaskMeleeAttackTargetingEntityR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            
        }

        public override bool ShouldExecute()
        {
            if (entity.World.Rand.NextDouble() < 0.1)
            {
                guardedEntity = GetGuardedEntity();
            }

            if (guardedEntity == null) return false;

            if (entity.World.ElapsedMilliseconds - lastattackingEntityFoundMs > 30000)
            {
                lastattackingEntity = null;
            }

            if (attackedByEntity == guardedEntity)
            {
                attackedByEntity = null;
            }

            return base.ShouldExecute();
        }

        public override void StartExecute()
        {
            base.StartExecute();

            lastattackingEntityFoundMs = entity.World.ElapsedMilliseconds;
            lastattackingEntity = targetEntity;
        }

        protected override bool IsTargetableEntity(Entity e, float range)
        {
            if (!base.IsTargetableEntity(e, range)) return false;
            if (e == guardedEntity) return false;

            var tasks = e.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.ActiveTasksBySlot;
            return (e == lastattackingEntity && e.Alive) || tasks?.FirstOrDefault(task => {
                return task is AiTaskBaseTargetable at && at.TargetEntity == guardedEntity && at.AggressiveTargeting;
            }) != null;
        }
    }
}