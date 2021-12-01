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
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class AiTaskFleeEntity : AiTaskBaseTargetable
    {
        EntityAgent targetEntity;
        Vec3d targetPos = new Vec3d();
        float moveSpeed = 0.02f;
        float seekingRange = 25f;
        float executionChance = 0.1f;
        float fleeingDistance = 31f;
        float minDayLight = -1f;
        float fleeDurationMs = 5000;
        bool cancelOnHurt = false;

        long fleeStartMs;
        bool stuck;

        float stepHeight;

        EntityPartitioning partitionUtil;

        bool lowStabilityAttracted;
        bool ignoreDeepDayLight;

        float tamingGenerations = 10f;
        Vec3d tmpVec = new Vec3d();
        Vec3d collTmpVec = new Vec3d();

		bool cancelNow;
        public AiTaskFleeEntity(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();

            base.LoadConfig(taskConfig, aiConfig);

            tamingGenerations = taskConfig["tamingGenerations"].AsFloat(10f);
            moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
            seekingRange = taskConfig["seekingRange"].AsFloat(25);
            executionChance = taskConfig["executionChance"].AsFloat(0.1f);
            minDayLight = taskConfig["minDayLight"].AsFloat(-1f);
            cancelOnHurt = taskConfig["cancelOnHurt"].AsBool(false);
            ignoreDeepDayLight = taskConfig["ignoreDeepDayLight"].AsBool(false);
            fleeingDistance = taskConfig["fleeingDistance"].AsFloat(seekingRange + 6);
            fleeDurationMs = taskConfig["fleeDurationMs"].AsInt(9000);
            lowStabilityAttracted = entity.World.Config.GetString("temporalStability").ToBool(true) && entity.Properties.Attributes?["spawnCloserDuringLowStability"].AsBool() == true;
        }


        

        public override bool ShouldExecute()
        {
            soundChance = Math.Min(1.01f, soundChance + 1 / 500f);

            if (rand.NextDouble() > 2 * executionChance) return false;


            if (whenInEmotionState != null && bhEmo?.IsInEmotionState(whenInEmotionState) != true) return false;
            if (whenNotInEmotionState != null && bhEmo?.IsInEmotionState(whenNotInEmotionState) == true) return false;

            // Double exec chance, but therefore halved here again to increase response speed for creature when aggressive
            if (whenInEmotionState == null && rand.NextDouble() > 0.5f) return false;

            float sunlight = entity.World.BlockAccessor.GetLightLevel((int)entity.ServerPos.X, (int)entity.ServerPos.Y, (int)entity.ServerPos.Z, EnumLightLevelType.TimeOfDaySunLight) / (float)entity.World.SunBrightness;
            if ((ignoreDeepDayLight && entity.ServerPos.Y < world.SeaLevel - 2) || sunlight < minDayLight)
            {
                if (!entity.Attributes.GetBool("ignoreDaylightFlee", false))
                {
                    return false;
                }
            }

            int generation = entity.WatchedAttributes.GetInt("generation", 0);
            float fearReductionFactor = Math.Max(0f, (tamingGenerations - generation) / tamingGenerations);
            if (whenInEmotionState != null) fearReductionFactor = 1;

            Vec3d ownPos = entity.ServerPos.XYZ;
            float hereRange = fearReductionFactor * seekingRange;

            targetEntity = (EntityAgent)partitionUtil.GetNearestEntity(ownPos, hereRange, (e) => {
                if (!isTargetableEntity(e, hereRange)) return false;
                return e.Code.Path != "player" || !lowStabilityAttracted || e.WatchedAttributes.GetDouble("temporalStability", 1) < 0.25;
            });


            if (targetEntity != null)
            {
                updateTargetPos();
                
                return true;
            }

            return false;
        }


        public override void StartExecute()
        {
            base.StartExecute();

            cancelNow = false;

            var bh = entity.GetBehavior<EntityBehaviorControlledPhysics>();
            stepHeight = bh == null ? 0.6f : bh.stepHeight;

            soundChance = Math.Max(0.025f, soundChance - 0.2f);

            float size = targetEntity.CollisionBox.XSize;

            pathTraverser.WalkTowards(targetPos, moveSpeed, size + 0.2f, OnGoalReached, OnStuck);

            fleeStartMs = entity.World.ElapsedMilliseconds;
            stuck = false;
        }


        public override bool ContinueExecute(float dt)
        {
            if (world.Rand.NextDouble() < 0.2)
            {
                updateTargetPos();
                pathTraverser.CurrentTarget.X = targetPos.X;
                pathTraverser.CurrentTarget.Y = targetPos.Y;
                pathTraverser.CurrentTarget.Z = targetPos.Z;
            }


            if (entity.ServerPos.SquareDistanceTo(targetEntity.ServerPos.XYZ) > fleeingDistance * fleeingDistance)
            {
                return false;
            }

            if (world.Rand.NextDouble() < 0.25)
            {
                float sunlight = entity.World.BlockAccessor.GetLightLevel((int)entity.ServerPos.X, (int)entity.ServerPos.Y, (int)entity.ServerPos.Z, EnumLightLevelType.TimeOfDaySunLight) / (float)entity.World.SunBrightness;
                if ((ignoreDeepDayLight && entity.ServerPos.Y < world.SeaLevel - 2) || sunlight < minDayLight)
                {
                    if (!entity.Attributes.GetBool("ignoreDaylightFlee", false))
                    {
                        return false;
                    }
                }
            }

            return !stuck && targetEntity.Alive && (entity.World.ElapsedMilliseconds - fleeStartMs < fleeDurationMs) && !cancelNow;
        }


        

        private void updateTargetPos()
        {
            float yaw = (float)Math.Atan2(targetEntity.ServerPos.X - entity.ServerPos.X, targetEntity.ServerPos.Z - entity.ServerPos.Z);

            // Simple steering behavior
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, yaw - GameMath.PI / 2);

            // Running into wall?
            if (traversable(tmpVec))
            {
                targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw - GameMath.PI / 2);
                return;
            }

            // Try 90 degrees left
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, yaw - GameMath.PI);
            if (traversable(tmpVec))
            {
                targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw - GameMath.PI);
                return;
            }

            // Try 90 degrees right
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, yaw);
            if (traversable(tmpVec))
            {
                targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw);
                return;
            }

            // Run towards target o.O
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, -yaw);
            targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, -yaw);
        }


        public override void OnEntityHurt(DamageSource source, float damage)
        {
            base.OnEntityHurt(source, damage);

            if (cancelOnHurt) cancelNow = true;
        }




        bool traversable(Vec3d pos)
        {
            return
                !world.CollisionTester.IsColliding(world.BlockAccessor, entity.CollisionBox, pos, false) ||
                !world.CollisionTester.IsColliding(world.BlockAccessor, entity.CollisionBox, collTmpVec.Set(pos).Add(0, Math.Min(1, stepHeight), 0), false)
            ;
        }


        public override void FinishExecute(bool cancelled)
        {
            pathTraverser.Stop();
            base.FinishExecute(cancelled);
        }


        private void OnStuck()
        {
            stuck = true;
        }

        private void OnGoalReached()
        {
            pathTraverser.Active = true;
        }
    }
}
