using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent
{
    public class AiTaskSeekTargetingEntity : AiTaskSeekEntity
    {
        Entity guardedEntity;
        Entity lastattackingEntity;
        long lastattackingEntityFoundMs;

        public AiTaskSeekTargetingEntity(EntityAgent entity) : base(entity)
        {
            searchWaitMs = 1000;
        }

        public override bool ShouldExecute()
        {
            if (entity.World.Rand.NextDouble() < 0.1)
            {
                var uid = entity.WatchedAttributes.GetString("guardedPlayerUid");
                if (uid != null)
                {
                    guardedEntity = entity.World.PlayerByUid(uid)?.Entity;
                }
                else
                {
                    var id = entity.WatchedAttributes.GetLong("guardedEntityId");
                    guardedEntity = entity.World.GetEntityById(id);
                }
            }

            if (guardedEntity == null) return false;
            if (entity.WatchedAttributes.GetBool("commandSit") == true) return false;

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
