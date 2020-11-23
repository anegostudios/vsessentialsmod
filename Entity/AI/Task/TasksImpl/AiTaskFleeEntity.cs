using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class AiTaskFleeEntity : AiTaskBase
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

        string[] fleeEntityCodesExact = new string[] { "player" };
        string[] fleeEntityCodesBeginsWith = new string[0];

        float stepHeight;

        EntityPartitioning partitionUtil;

        bool lowStabilityAttracted;
        bool ignoreDeepDayLight;

        

        public AiTaskFleeEntity(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();

            base.LoadConfig(taskConfig, aiConfig);

            if (taskConfig["movespeed"] != null)
            {
                moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
            }

            if (taskConfig["seekingRange"] != null)
            {
                seekingRange = taskConfig["seekingRange"].AsFloat(25);
            }

            if (taskConfig["executionChance"] != null)
            {
                executionChance = taskConfig["executionChance"].AsFloat(0.07f);
            }

            if (taskConfig["minDayLight"] != null)
            {
                minDayLight = taskConfig["minDayLight"].AsFloat(-1f);
            }

            if (taskConfig["cancelOnHurt"] != null)
            {
                cancelOnHurt = taskConfig["cancelOnHurt"].AsBool(false);
            }

            if (taskConfig["ignoreDeepDayLight"] != null)
            {
                ignoreDeepDayLight = taskConfig["ignoreDeepDayLight"].AsBool(false);
            }

            if (taskConfig["fleeingDistance"] != null)
            {
                fleeingDistance = taskConfig["fleeingDistance"].AsFloat(25f);
            } else fleeingDistance = seekingRange + 6;

            if (taskConfig["fleeDurationMs"] != null)
            {
                fleeDurationMs = taskConfig["fleeDurationMs"].AsInt(9000);
            }

            if (taskConfig["entityCodes"] != null)
            {
                string[] codes = taskConfig["entityCodes"].AsArray<string>(new string[] { "player" });

                List<string> exact = new List<string>();
                List<string> beginswith = new List<string>();

                for (int i = 0; i < codes.Length; i++)
                {
                    string code = codes[i];
                    if (code.EndsWith("*")) beginswith.Add(code.Substring(0, code.Length - 1));
                    else exact.Add(code);
                }

                fleeEntityCodesExact = exact.ToArray();
                fleeEntityCodesBeginsWith = beginswith.ToArray();
            }

            lowStabilityAttracted = entity.World.Config.GetString("temporalStability").ToBool(true) && entity.Properties.Attributes?["spawnCloserDuringLowStability"].AsBool() == true;
        }


        

        public override bool ShouldExecute()
        {
            soundChance = Math.Min(1.01f, soundChance + 1 / 500f);

            if (rand.NextDouble() > 2 * executionChance) return false;


            if (whenInEmotionState != null && !entity.HasEmotionState(whenInEmotionState)) return false;
            if (whenNotInEmotionState != null && entity.HasEmotionState(whenNotInEmotionState)) return false;

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
            float fearReductionFactor = Math.Max(0f, (10f - generation) / 10f);
            if (whenInEmotionState != null) fearReductionFactor = 1;

            Vec3d ownPos = entity.ServerPos.XYZ;
            double hereRange = fearReductionFactor * seekingRange;

            targetEntity = (EntityAgent)partitionUtil.GetNearestEntity(ownPos, hereRange, (e) => {
                if (!e.Alive || e.EntityId == this.entity.EntityId || e is EntityItem) return false;

                for (int i = 0; i < fleeEntityCodesExact.Length; i++)
                {
                    if (e.Code.Path == fleeEntityCodesExact[i])
                    {
                        if (e is EntityAgent eagent)
                        {
                            float rangeMul = 1f;

                            // Sneaking halves the detection range
                            if (eagent.Controls.Sneak && eagent.OnGround)
                            {
                                rangeMul *= 0.6f;
                            }
                            // Trait bonus
                            if (e.Code.Path == "player")
                            {
                                rangeMul = eagent.Stats.GetBlended("animalSeekingRange");
                            }

                            if (rangeMul != 1 && e.ServerPos.DistanceTo(ownPos) > hereRange * rangeMul) return false;
                        }

                        if (e.Code.Path == "player")
                        {
                            IPlayer player = entity.World.PlayerByUid(((EntityPlayer)e).PlayerUID);
                            bool ok = player == null || (player.WorldData.CurrentGameMode != EnumGameMode.Creative && player.WorldData.CurrentGameMode != EnumGameMode.Spectator);

                            ok &= !lowStabilityAttracted || e.WatchedAttributes.GetDouble("temporalStability", 1) > 0.25;

                            return ok;
                        }
                        return true;
                    }
                }

                for (int i = 0; i < fleeEntityCodesBeginsWith.Length; i++)
                {
                    if (e.Code.Path.StartsWithFast(fleeEntityCodesBeginsWith[i])) return true;
                }

                return false;
            });

            //yawOffset = 0;

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

            var bh = entity.GetBehavior<EntityBehaviorControlledPhysics>();
            stepHeight = bh == null ? 0.6f : bh.stepHeight;

            soundChance = Math.Max(0.025f, soundChance - 0.2f);

            float size = targetEntity.CollisionBox.X2 - targetEntity.CollisionBox.X1;

            //pathTraverser.NavigateTo(targetPos, moveSpeed, size + 0.2f, OnGoalReached, OnStuck);
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

            //if (entity.IsActivityRunning("invulnerable")) return false;

            return !stuck && targetEntity.Alive && (entity.World.ElapsedMilliseconds - fleeStartMs < fleeDurationMs);
        }


        Vec3d tmpVec = new Vec3d();
        //float yawOffset;

        private void updateTargetPos()
        {
            float yaw = (float)Math.Atan2(targetEntity.ServerPos.X - entity.ServerPos.X, targetEntity.ServerPos.Z - entity.ServerPos.Z);

            /*tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(10, 0, yaw - GameMath.PI / 2);

            int tries = 10;
            double dx, dy, dz;
            while (tries-- > 0)
            {
                dx = rand.NextDouble() * 8 - 4;
                dy = rand.NextDouble() * 8 - 4;
                dz = rand.NextDouble() * 8 - 4;

                if (entity.Properties.Habitat == EnumHabitat.Land)
                {
                    tmpVec.Y = moveDownToFloor((int)tmpVec.X, tmpVec.Y, (int)tmpVec.Z);
                }

                tmpVec.Add(dx, dx, dz);

                pathTraverser.NavigateTo(targetPos, moveSpeed, 1, OnGoalReached, OnStuck, tries > 0, 999, true);
            }*/


            // Some simple steering behavior, works really suxy
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, yaw - GameMath.PI / 2);

            // Running into wall?
            if (traversable(tmpVec))
            {
                //yawOffset = 0;
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
            //if (traversable(tmpVec))
            {
                targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, -yaw);
                return;
            }
        }


        

        Vec3d collTmpVec = new Vec3d();
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
