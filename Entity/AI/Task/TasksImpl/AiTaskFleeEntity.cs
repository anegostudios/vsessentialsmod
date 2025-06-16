using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent
{
    public class AiTaskFleeEntity : AiTaskBaseTargetable
    {
        Vec3d targetPos = new Vec3d();
        float targetYaw = 0f;

        float moveSpeed = 0.02f;
        float seekingRange = 25f;
        float executionChance = 0.1f;
        float fleeingDistance = 31f;
        float minDayLight = -1f;
        float fleeDurationMs = 5000;
        float instafleeOnDamageChance = 0;
        bool cancelOnHurt = false;

        long fleeStartMs;
        bool stuck;
        
        bool lowStabilityAttracted;
        bool ignoreDeepDayLight;
        
		bool cancelNow;

        float nowFleeingDistance;
        bool instafleenow=false;

        public override bool AggressiveTargeting => false;


        public AiTaskFleeEntity(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
            seekingRange = taskConfig["seekingRange"].AsFloat(25);
            executionChance = taskConfig["executionChance"].AsFloat(0.1f);
            minDayLight = taskConfig["minDayLight"].AsFloat(-1f);
            cancelOnHurt = taskConfig["cancelOnHurt"].AsBool(false);
            ignoreDeepDayLight = taskConfig["ignoreDeepDayLight"].AsBool(false);
            fleeingDistance = taskConfig["fleeingDistance"].AsFloat(seekingRange + 15);
            fleeDurationMs = taskConfig["fleeDurationMs"].AsInt(9000);
            instafleeOnDamageChance = taskConfig["instafleeOnDamageChance"].AsFloat(0f);
            lowStabilityAttracted = entity.World.Config.GetString("temporalStability").ToBool(true) && entity.Properties.Attributes?["spawnCloserDuringLowStability"].AsBool() == true;
        }



        readonly Vec3d ownPos = new Vec3d();
        public override bool ShouldExecute()
        {
            soundChance = Math.Min(1.01f, soundChance + 1 / 500f);

            if (instafleenow) return TryInstaFlee();

            if (rand.NextDouble() > executionChance) return false;
            if (noEntityCodes && (attackedByEntity == null || !retaliateAttacks)) return false;
            if (!PreconditionsSatisifed()) return false;

            // This code section controls drifter behavior - they retreat (flee slowly) from the player in the daytime, this is "switched off" below ground or at night, also switched off in temporal storms
            // Has to be checked every tick because the drifter attributes change during temporal storms  (grrr, this is a slow way to do it)

            // Do we use daylight levels to determine fleeing behaviour? If not skip all of this.
            if (minDayLight > 0)
            {
                // Are we currently in a temporal storm? If so then return false because we don't flee in storms.
                if ( entity.Attributes.GetBool("ignoreDaylightFlee", false)) return false;

                // Are we below sea level and set to ignore daylight levels underground? If so then return false because we don't flee underground.
                if (ignoreDeepDayLight && entity.ServerPos.Y < world.SeaLevel - 2) return false;

                //Is the light level too weak to affect us? If so then return false because the light is too dim to make us flee.
                float sunlight = entity.World.BlockAccessor.GetLightLevel((int)entity.ServerPos.X, (int)entity.ServerPos.Y, (int)entity.ServerPos.Z, EnumLightLevelType.TimeOfDaySunLight) / (float)entity.World.SunBrightness;
                if (sunlight < minDayLight) return false;
            }

            int generation = GetOwnGeneration();
            float fearReductionFactor = (WhenInEmotionState != null) ? 1 : Math.Max(0f, (tamingGenerations - generation) / tamingGenerations);

            ownPos.SetWithDimension(entity.ServerPos);
            float hereRange = fearReductionFactor * seekingRange;

            entity.World.FrameProfiler.Mark("task-fleeentity-shouldexecute-init");

            if (lowStabilityAttracted)
            {
                targetEntity = partitionUtil.GetNearestEntity(ownPos, hereRange, entity =>
                {
                    if (entity is not EntityAgent) return false;
                    if (!IsTargetableEntity(entity, hereRange)) return false;
                    if (entity is not EntityPlayer) return true;
                    return entity.WatchedAttributes.GetDouble("temporalStability", 1) > 0.25;
                }, EnumEntitySearchType.Creatures) as EntityAgent;
            }
            else
            {
                targetEntity = partitionUtil.GetNearestEntity(ownPos, hereRange, entity => IsTargetableEntity(entity, hereRange) && entity is EntityAgent, EnumEntitySearchType.Creatures) as EntityAgent;
            }

            nowFleeingDistance = fleeingDistance;

            entity.World.FrameProfiler.Mark("task-fleeentity-shouldexecute-entitysearch");

            if (targetEntity != null)
            {
                if (entity.ToleratesDamageFrom(targetEntity)) nowFleeingDistance /= 2;
                float yaw = (float)Math.Atan2(targetEntity.ServerPos.X - entity.ServerPos.X, targetEntity.ServerPos.Z - entity.ServerPos.Z);
                updateTargetPosFleeMode(targetPos, yaw);
                return true;
            }

            return false;
        }

        private bool TryInstaFlee()
        {
            // Beyond visual range: Run in looking direction
            if (targetEntity == null || entity.ServerPos.DistanceTo(targetEntity.ServerPos) > seekingRange)
            {
                float cosYaw = GameMath.Cos(entity.ServerPos.Yaw);
                float sinYaw = GameMath.Sin(entity.ServerPos.Yaw);
                double offset = 200;
                targetPos = new Vec3d(entity.ServerPos.X + sinYaw * offset, entity.ServerPos.Y, entity.ServerPos.Z + cosYaw * offset);
                targetYaw = entity.ServerPos.Yaw;
                targetEntity = null;
            }
            else
            {
                nowFleeingDistance = (float)entity.ServerPos.DistanceTo(targetEntity.ServerPos) + 15;
                if (entity.ToleratesDamageFrom(targetEntity)) nowFleeingDistance /= 2.5f;
                updateTargetPosFleeMode(targetPos, entity.ServerPos.Yaw);
            }

            
            instafleenow = false;

            return true;
        }

        public override void StartExecute()
        {
            base.StartExecute();

            cancelNow = false;

            soundChance = Math.Max(0.025f, soundChance - 0.2f);

            float size = targetEntity?.SelectionBox.XSize ?? 0;

            pathTraverser.WalkTowards(targetPos, moveSpeed, size + 0.2f, OnGoalReached, OnStuck);

            fleeStartMs = entity.World.ElapsedMilliseconds;
            stuck = false;
        }


        Vec3d tmpTargetPos = new Vec3d();
        public override bool ContinueExecute(float dt)
        {
            if (world.Rand.NextDouble() < 0.2)
            {
                float yaw = targetEntity == null ? -targetYaw : (float)Math.Atan2(targetEntity.ServerPos.X - entity.ServerPos.X, targetEntity.ServerPos.Z - entity.ServerPos.Z);

                updateTargetPosFleeMode(tmpTargetPos.Set(targetPos), yaw);
                pathTraverser.CurrentTarget.X = tmpTargetPos.X;
                pathTraverser.CurrentTarget.Y = tmpTargetPos.Y;
                pathTraverser.CurrentTarget.Z = tmpTargetPos.Z;
                pathTraverser.Retarget();
            }

            //entity.World.SpawnParticles(1, ColorUtil.WhiteArgb, tmpTargetPos, tmpTargetPos, new Vec3f(), new Vec3f(), 1, 0, 2, EnumParticleModel.Quad);

            if (targetEntity != null && entity.ServerPos.SquareDistanceTo(targetEntity.ServerPos) > nowFleeingDistance * nowFleeingDistance)
            {
                return false;
            }
            if (targetEntity == null && entity.World.ElapsedMilliseconds - fleeStartMs > 5000)
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

            return !stuck && (targetEntity == null || targetEntity.Alive) && (entity.World.ElapsedMilliseconds - fleeStartMs < fleeDurationMs) && !cancelNow && pathTraverser.Active;
        }       

        


        public override void OnEntityHurt(DamageSource source, float damage)
        {
            base.OnEntityHurt(source, damage);

            if (cancelOnHurt) cancelNow = true;

            if (source.Type != EnumDamageType.Heal && entity.World.Rand.NextDouble() < instafleeOnDamageChance)
            {
                instafleenow = true;
                targetEntity = source.GetCauseEntity();
            }
        }

        public void InstaFleeFrom(Entity fromEntity)
        {
            instafleenow = true;
            targetEntity = fromEntity;
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
            pathTraverser.Retarget();
        }

        // Unfortunate minor amount of code duplication but here we need the base method without the e.IsInteractable check
        public override bool CanSense(Entity e, double range)
        {
            if (e.EntityId == entity.EntityId) return false;
            if (e is EntityPlayer eplr) return CanSensePlayer(eplr, range);

            if (skipEntityCodes != null)
            {
                for (int i = 0; i < skipEntityCodes.Length; i++)
                {
                    if (WildcardUtil.Match(skipEntityCodes[i], e.Code)) return false;
                }
            }

            return true;
        }
    }
}
