using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class AiTaskStayCloseToGuardedEntity : AiTaskStayCloseToEntity
    {
        public Entity guardedEntity;

        public AiTaskStayCloseToGuardedEntity(EntityAgent entity) : base(entity)
        {
        }

        public override bool ShouldExecute()
        {
            if (entity.World.Rand.NextDouble() < 0.1)
            {
                guardedEntity = GetGuardedEntity();
            }

            if (guardedEntity == null) return false;
            if (rand.NextDouble() > 0.1f) return false;
            if (entity.WatchedAttributes.GetBool("commandSit") == true) return false;

            targetEntity = guardedEntity;
            double x = guardedEntity.ServerPos.X;
            double y = guardedEntity.ServerPos.Y;
            double z = guardedEntity.ServerPos.Z;

            double dist = entity.ServerPos.SquareDistanceTo(x, y, z);

            return dist > maxDistance * maxDistance;
        }


        public override void StartExecute()
        {
            base.StartExecute();

            float size = targetEntity.SelectionBox.XSize;

            if (!pathTraverser.NavigateTo(targetEntity.ServerPos.XYZ, moveSpeed, size + 0.2f, OnGoalReached, OnStuck, false, 1000, true))
            {
                tryTeleport();
            }

            targetOffset.Set(entity.World.Rand.NextDouble() * 2 - 1, 0, entity.World.Rand.NextDouble() * 2 - 1);

            stuck = false;
        }



        public Entity GetGuardedEntity()
        {
            var uid = entity.WatchedAttributes.GetString("guardedPlayerUid");
            if (uid != null)
            {
                return entity.World.PlayerByUid(uid).Entity;
            }
            else
            {
                var id = entity.WatchedAttributes.GetLong("guardedEntityId");
                return entity.World.GetEntityById(id);
            }
        }

    }
}
