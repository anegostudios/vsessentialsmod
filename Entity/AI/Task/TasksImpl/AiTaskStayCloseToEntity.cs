using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class AiTaskStayCloseToEntity : AiTaskBase
    {
        protected Entity targetEntity;
        protected float moveSpeed = 0.03f;
        protected float range = 8f;
        protected float maxDistance = 3f;
        protected string entityCode;
        protected bool stuck = false;
        protected bool onlyIfLowerId = false;
        protected bool allowTeleport;
        protected float teleportAfterRange;

        protected Vec3d targetOffset = new Vec3d();

        public AiTaskStayCloseToEntity(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            moveSpeed = taskConfig["movespeed"].AsFloat(0.03f);
            range = taskConfig["searchRange"].AsFloat(8f);
            maxDistance = taskConfig["maxDistance"].AsFloat(3f);
            onlyIfLowerId = taskConfig["onlyIfLowerId"].AsBool();
            entityCode = taskConfig["entityCode"].AsString();
            allowTeleport = taskConfig["allowTeleport"].AsBool();
            teleportAfterRange = taskConfig["teleportAfterRange"].AsFloat(30f);
        }


        public override bool ShouldExecute()
        {
            if (rand.NextDouble() > 0.01f) return false;

            if (targetEntity == null || !targetEntity.Alive)
            {
                if (onlyIfLowerId)
                {
                    targetEntity = entity.World.GetNearestEntity(entity.ServerPos.XYZ, range, 2, (e) =>
                    {
                        return e.EntityId < entity.EntityId && e.Code.Path.Equals(entityCode);
                    });
                }
                else
                {
                    targetEntity = entity.World.GetNearestEntity(entity.ServerPos.XYZ, range, 2, (e) =>
                    {
                        return e.Code.Path.Equals(entityCode);
                    });
                }
            }

            if (targetEntity != null && (!targetEntity.Alive || targetEntity.ShouldDespawn)) targetEntity = null;
            if (targetEntity == null) return false;

            double x = targetEntity.ServerPos.X;
            double y = targetEntity.ServerPos.Y;
            double z = targetEntity.ServerPos.Z;

            double dist = entity.ServerPos.SquareDistanceTo(x, y, z);

            return dist > maxDistance * maxDistance;
        }


        public override void StartExecute()
        {
            base.StartExecute();

            float size = targetEntity.SelectionBox.XSize;

            pathTraverser.NavigateTo_Async(targetEntity.ServerPos.XYZ, moveSpeed, size + 0.2f, OnGoalReached, OnStuck, null, 1000, 1);

            targetOffset.Set(entity.World.Rand.NextDouble() * 2 - 1, 0, entity.World.Rand.NextDouble() * 2 - 1);

            stuck = false;
        }

        public override bool CanContinueExecute()
        {
            return pathTraverser.Ready;
        }

        public override bool ContinueExecute(float dt)
        {
            double x = targetEntity.ServerPos.X + targetOffset.X;
            double y = targetEntity.ServerPos.Y;
            double z = targetEntity.ServerPos.Z + targetOffset.Z;

            pathTraverser.CurrentTarget.X = x;
            pathTraverser.CurrentTarget.Y = y;
            pathTraverser.CurrentTarget.Z = z;

            float dist = entity.ServerPos.SquareDistanceTo(x, y, z);

            if (dist < 3 * 3)
            {
                pathTraverser.Stop();
                return false;
            }

            if (allowTeleport && dist > teleportAfterRange * teleportAfterRange && entity.World.Rand.NextDouble() < 0.05)
            {
                tryTeleport();
            }

            return !stuck && pathTraverser.Active;
        }

        private Vec3d findDecentTeleportPos()
        {
            var ba = entity.World.BlockAccessor;
            var rnd = entity.World.Rand;

            Vec3d pos = new Vec3d();
            BlockPos bpos = new BlockPos();
            for (int i = 0; i < 30; i++)
            {
                float range = GameMath.Clamp(i / 4f, 2, 4.5f);

                double rndx = rnd.NextDouble() * 2 * range - range;
                double rndz = rnd.NextDouble() * 2 * range - range;

                for (int j = 0; j < 8; j++)
                {
                    // Produces: 0, -1, 1, -2, 2, -3, 3
                    int dy = (1 - (j % 2) * 2) * (int)Math.Ceiling(j / 2f);

                    pos.Set(targetEntity.ServerPos.X + rndx, targetEntity.ServerPos.Y + dy, targetEntity.ServerPos.Z + rndz);


                    bpos.Set((int)pos.X, (int)pos.Y, (int)pos.Z);
                    Block aboveBlock = ba.GetBlock(bpos);
                    var boxes = aboveBlock.GetCollisionBoxes(ba, bpos);
                    if (boxes != null && boxes.Length > 0) continue;

                    bpos.Set((int)pos.X, (int)pos.Y - 1, (int)pos.Z);
                    Block belowBlock = ba.GetBlock(bpos);
                    boxes = belowBlock.GetCollisionBoxes(ba, bpos);
                    if (boxes == null || boxes.Length == 0) continue;

                    pos.Y = (int)pos.Y - 1 + boxes.Max(c => c.Y2);

                    return pos;
                }

            }

            return null;
        }


        protected void tryTeleport()
        {
            if (!allowTeleport || targetEntity == null) return;
            Vec3d pos = findDecentTeleportPos();
            if (pos != null) entity.TeleportTo(pos);
        }


        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);
        }

        protected void OnStuck()
        {
            stuck = true;
            tryTeleport();
        }

        public override void OnNoPath(Vec3d target)
        {
            tryTeleport();
        }

        protected void OnGoalReached()
        {
        }
    }
}
