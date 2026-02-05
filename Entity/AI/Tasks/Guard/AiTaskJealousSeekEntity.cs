using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public class AiTaskJealousSeekEntity : AiTaskSeekEntity
    {
        Entity? guardedEntity;

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

        public override bool IgnoreDamageFrom(Entity attacker)
        {
            return attacker == (guardedEntity ??= GetGuardedEntity()) || base.IgnoreDamageFrom(attacker);
        }
    }
}
