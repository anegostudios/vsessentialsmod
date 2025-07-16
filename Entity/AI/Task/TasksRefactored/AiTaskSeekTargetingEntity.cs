using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.GameContent
{
    public class AiTaskSeekTargetingEntityR : AiTaskSeekEntityR
    {
        Entity guardedEntity;
        Entity lastattackingEntity;
        long lastattackingEntityFoundMs;

        private AiTaskSeekEntityConfig Config => GetConfig<AiTaskSeekEntityConfig>();

        public AiTaskSeekTargetingEntityR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            Config.TargetSearchCooldownMs = 1000; // @TODO refactor
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

        protected override bool IsTargetableEntity(Entity e, float range)
        {
            if (!base.IsTargetableEntity(e, range)) return false;

            var tasks = e.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.ActiveTasksBySlot;
            return (e == lastattackingEntity && e.Alive) || tasks?.FirstOrDefault(task => {
                return task is AiTaskBaseTargetable at && at.TargetEntity == guardedEntity && at.AggressiveTargeting;
            }) != null;
        }
    }
}
