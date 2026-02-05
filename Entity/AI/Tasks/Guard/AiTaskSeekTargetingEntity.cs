using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public class AiTaskSeekTargetingEntity : AiTaskSeekEntity
    {
        Entity? guardedEntity;
        Entity? lastattackingEntity;
        long lastattackingEntityFoundMs;

        public AiTaskSeekTargetingEntity(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            searchWaitMs = 1000;
        }

        public override bool ShouldExecute()
        {
            if (entity.World.Rand.NextDouble() < 0.1)
            {
                guardedEntity = GetGuardedEntity();
            }

            if (guardedEntity == null) return false;
            if (entity.WatchedAttributes.GetBool("commandSit") == true) return false;

            if (entity.World.ElapsedMilliseconds - lastattackingEntityFoundMs > 30000 || lastattackingEntity == guardedEntity)
            {
                lastattackingEntity = null;
            }

            return base.ShouldExecute();
        }

        protected override bool GetIsTamed() => true;

        public override void StartExecute()
        {
            base.StartExecute();

            lastattackingEntityFoundMs = entity.World.ElapsedMilliseconds;
            lastattackingEntity = targetEntity;
        }

        public override bool IgnoreDamageFrom(Entity attacker)
        {
            return attacker == (guardedEntity ??= GetGuardedEntity()) || base.IgnoreDamageFrom(attacker);
        }
    }
}
