using System;
using System.Drawing;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

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
        protected int teleportToRange;
        public int TeleportMaxRange;

        public float minSeekSeconds = 3f;

        protected Vec3d targetOffset = new Vec3d();
        protected Vec3d initialTargetPos;

        public int allowTeleportCount = 0;

        float executingSeconds =0;

        int stuckCounter;
        long cooldownUntilTotalMs;

        public AiTaskStayCloseToEntity(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            moveSpeed = taskConfig["movespeed"].AsFloat(0.03f);
            range = taskConfig["searchRange"].AsFloat(8f);
            maxDistance = taskConfig["maxDistance"].AsFloat(3f);
            minSeekSeconds = taskConfig["minSeekSeconds"].AsFloat(3f);
            onlyIfLowerId = taskConfig["onlyIfLowerId"].AsBool();
            entityCode = taskConfig["entityCode"].AsString();
            allowTeleport = taskConfig["allowTeleport"].AsBool();
            teleportAfterRange = taskConfig["teleportAfterRange"].AsFloat(30f);
            teleportToRange = taskConfig["teleportToRange"].AsInt(1);
            TeleportMaxRange = taskConfig["teleportMaxRange"].AsInt(int.MaxValue);
        }

        public override bool ShouldExecute()
        {
            if (rand.NextDouble() > 0.01f) return false;

            if (stuckCounter > 3)
            {
                stuckCounter = 0;
                cooldownUntilTotalMs = entity.World.ElapsedMilliseconds + 60 * 1000;
            }
            if (entity.World.ElapsedMilliseconds < cooldownUntilMs) return false;

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

            executingSeconds = 0;
            initialTargetPos = targetEntity.ServerPos.XYZ;

            if (targetEntity.ServerPos.DistanceTo(entity.ServerPos) > TeleportMaxRange)
            {
                stuck = true;
                return;
            }

            float size = targetEntity.SelectionBox.XSize;
            pathTraverser.NavigateTo_Async(targetEntity.ServerPos.XYZ, moveSpeed, size + 0.2f, OnGoalReached, OnStuck, OnNoPath, 1000, 1);
            targetOffset.Set(entity.World.Rand.NextDouble() * 2 - 1, 0, entity.World.Rand.NextDouble() * 2 - 1);
            stuck = false;
        }

        public override bool CanContinueExecute()
        {
            return pathTraverser.Ready;
        }

        public override bool ContinueExecute(float dt)
        {
            //Check if time is still valid for task.
            if (!IsInValidDayTimeHours(false)) return false;

            if (initialTargetPos.DistanceTo(targetEntity.ServerPos.XYZ) > 3)
            {
                initialTargetPos = targetEntity.ServerPos.XYZ;
                pathTraverser.Retarget();
            }

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

            if ((allowTeleport || allowTeleportCount > 0) && executingSeconds > 4 && (dist > teleportAfterRange * teleportAfterRange || stuck) && entity.World.Rand.NextDouble() < 0.05)
            {
                tryTeleport();
            }

            executingSeconds += dt;

            return (!stuck && pathTraverser.Active) || executingSeconds < minSeekSeconds;
        }

        private Vec3d findDecentTeleportPos()
        {
            var ba = entity.World.BlockAccessor;
            var rnd = entity.World.Rand;

            Vec3d pos = new Vec3d();
            BlockPos bpos = new BlockPos();
            for (int i = teleportToRange; i < teleportToRange+30; i++)
            {
                float range = GameMath.Clamp(i / 5f, 2, 4.5f);

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
                    if (aboveBlock.Attributes?["insideDamage"].AsInt(0) > 0) continue;

                    bpos.Set((int)pos.X, (int)pos.Y - 1, (int)pos.Z);
                    Block belowBlock = ba.GetBlock(bpos);
                    boxes = belowBlock.GetCollisionBoxes(ba, bpos);
                    if (boxes == null || boxes.Length == 0) continue;
                    if (belowBlock.Attributes?["insideDamage"].AsInt(0) > 0) continue;

                    pos.Y = (int)pos.Y - 1 + boxes.Max(c => c.Y2);

                    return pos;
                }

            }

            return null;
        }


        protected void tryTeleport()
        {
            if ((!allowTeleport && allowTeleportCount <= 0) || targetEntity == null) return;
            Vec3d pos = findDecentTeleportPos();
            if (pos != null)
            {
                entity.TeleportToDouble(pos.X, pos.Y, pos.Z, () =>
                {
                    initialTargetPos = targetEntity.ServerPos.XYZ;
                    pathTraverser.Retarget();
                    allowTeleportCount = Math.Max(0, allowTeleportCount - 1);
                });
            }
        }


        public override void FinishExecute(bool cancelled)
        {
            if (stuck) stuckCounter++;
            else stuckCounter = 0;
            base.FinishExecute(cancelled);
        }

        protected void OnStuck()
        {
            stuck = true;
            //tryTeleport();
        }

        public void OnNoPath()
        {
            // tryTeleport();
        }

        protected virtual void OnGoalReached()
        {
        }
    }
}
