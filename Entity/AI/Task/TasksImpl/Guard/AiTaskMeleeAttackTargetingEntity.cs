using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent
{
    public class AiTaskMeleeAttackTargetingEntity : AiTaskMeleeAttack
    {
        Entity guardedEntity;
        Entity lastattackingEntity;
        long lastattackingEntityFoundMs;

        public AiTaskMeleeAttackTargetingEntity(EntityAgent entity) : base(entity)
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

            return base.ShouldExecute();
        }

        public override void StartExecute()
        {
            base.StartExecute();

            lastattackingEntityFoundMs = entity.World.ElapsedMilliseconds;
            lastattackingEntity = targetEntity;
        }

        public override bool IsTargetableEntity(Entity e, float range, bool ignoreEntityCode = false)
        {
            if (!base.IsTargetableEntity(e, range, ignoreEntityCode)) return false;

            var tasks = e.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.ActiveTasksBySlot;
            return (e == lastattackingEntity && e.Alive) || tasks?.FirstOrDefault(task => {
                return task is AiTaskBaseTargetable at && at.TargetEntity == guardedEntity && at.AggressiveTargeting;
            }) != null;
        }
    }
}