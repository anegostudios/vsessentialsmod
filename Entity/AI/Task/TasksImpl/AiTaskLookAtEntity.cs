using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class AiTaskLookAtEntity : AiTaskBaseTargetable
    {
        public bool manualExecute;
        public float moveSpeed = 0.02f;
        public float seekingRange = 25f;
        public float maxFollowTime = 60;

        float minTurnAnglePerSec;
        float maxTurnAnglePerSec;
        float curTurnRadPerSec;

        float maxTurnAngleRad = GameMath.TWOPI;
        float spawnAngleRad;

        public AiTaskLookAtEntity(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            maxTurnAngleRad = taskConfig["maxTurnAngleDeg"].AsFloat(360) * GameMath.DEG2RAD;
            spawnAngleRad = entity.Attributes.GetFloat("spawnAngleRad");
        }

        public override bool ShouldExecute()
        {
            if (!manualExecute)
            {
                targetEntity = partitionUtil.GetNearestEntity(entity.ServerPos.XYZ, seekingRange, (e) => IsTargetableEntity(e, seekingRange), EnumEntitySearchType.Creatures);
                return targetEntity != null;
            }
            return false;
        }

        public float MinDistanceToTarget()
        {
            return Math.Max(0.8f, targetEntity.SelectionBox.XSize / 2 + entity.SelectionBox.XSize / 2);
        }

        public override void StartExecute()
        {
            base.StartExecute();

            if (entity?.Properties.Server?.Attributes != null)
            {
                minTurnAnglePerSec = (float)entity.Properties.Server.Attributes.GetTreeAttribute("pathfinder").GetFloat("minTurnAnglePerSec", 250);
                maxTurnAnglePerSec = (float)entity.Properties.Server.Attributes.GetTreeAttribute("pathfinder").GetFloat("maxTurnAnglePerSec", 450);
            }
            else
            {
                minTurnAnglePerSec = 250;
                maxTurnAnglePerSec = 450;
            }

            curTurnRadPerSec = minTurnAnglePerSec + (float)entity.World.Rand.NextDouble() * (maxTurnAnglePerSec - minTurnAnglePerSec);
            curTurnRadPerSec *= GameMath.DEG2RAD * 50 * 0.02f;
        }

        public override bool ContinueExecute(float dt)
        {
            //Check if time is still valid for task.
            if (!IsInValidDayTimeHours(false)) return false;

            Vec3f targetVec = new Vec3f();

            targetVec.Set(
                (float)(targetEntity.ServerPos.X - entity.ServerPos.X),
                (float)(targetEntity.ServerPos.Y - entity.ServerPos.Y),
                (float)(targetEntity.ServerPos.Z - entity.ServerPos.Z)
            );

            float desiredYaw = (float)Math.Atan2(targetVec.X, targetVec.Z);

            if (maxTurnAngleRad < GameMath.PI)
            {
                // This is for the eidolon
                desiredYaw = GameMath.Clamp(desiredYaw, spawnAngleRad - maxTurnAngleRad, spawnAngleRad + maxTurnAngleRad);
            }

            float yawDist = GameMath.AngleRadDistance(entity.ServerPos.Yaw, desiredYaw);
            entity.ServerPos.Yaw += GameMath.Clamp(yawDist, -curTurnRadPerSec * dt, curTurnRadPerSec * dt);
            entity.ServerPos.Yaw = entity.ServerPos.Yaw % GameMath.TWOPI;

            return Math.Abs(yawDist) > 0.01;
        }

        

        public override bool Notify(string key, object data)
        {
            return false;
        }
    }
}
