using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class AiTaskSeekEntity : AiTaskBase
    {
        EntityAgent targetEntity;
        Vec3d targetPos;

        float moveSpeed = 0.02f;
        float seekingRange = 25f;
        float belowTempSeekingRange = 25f;
        float belowTempThreshold = -999;
        float maxFollowTime = 60;
        
        bool stopNow = false;
        string[] seekEntityCodesExact = new string[] { "player" };
        string[] seekEntityCodesBeginsWith = new string[0];

        float currentFollowTime = 0;

        bool alarmHerd = false;
        bool leapAtTarget = false;
        float leapHeightMul = 1f;
        string leapAnimationCode="jump";
        float leapChance = 1f;

        bool siegeMode;

        long finishedMs;
        bool jumpAnimOn;

        long lastSearchTotalMs;

        EntityPartitioning partitionUtil;

        
        bool lowTempMode;

        public AiTaskSeekEntity(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();

            base.LoadConfig(taskConfig, aiConfig);

            
            if (taskConfig["leapAnimation"].Exists)
            {
                leapAnimationCode = taskConfig["leapAnimation"].AsString(null);
            }

            if (taskConfig["leapChance"].Exists)
            {
                leapChance = taskConfig["leapChance"].AsFloat(1);
            }

            if (taskConfig["leapHeightMul"].Exists)
            {
                leapHeightMul = taskConfig["leapHeightMul"].AsFloat(1);
            }

            if (taskConfig["movespeed"] != null)
            {
                moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
            }

            if (taskConfig["seekingRange"] != null)
            {
                seekingRange = taskConfig["seekingRange"].AsFloat(25);
            }

            if (taskConfig["belowTempSeekingRange"] != null)
            {
                belowTempSeekingRange = taskConfig["belowTempSeekingRange"].AsFloat(25);
            }

            if (taskConfig["belowTempThreshold"] != null)
            {
                belowTempThreshold = taskConfig["belowTempThreshold"].AsFloat(-999);
            }
            


            if (taskConfig["maxFollowTime"] != null)
            {
                maxFollowTime = taskConfig["maxFollowTime"].AsFloat(60);
            }

            if (taskConfig["alarmHerd"] != null)
            {
                alarmHerd = taskConfig["alarmHerd"].AsBool(false);
            }

            if (taskConfig["leapAtTarget"] != null)
            {
                leapAtTarget = taskConfig["leapAtTarget"].AsBool(false);
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

                seekEntityCodesExact = exact.ToArray();
                seekEntityCodesBeginsWith = beginswith.ToArray();
            }
        }


        public override bool ShouldExecute()
        {
            // React immediately on hurt, otherwise only 1/10 chance of execution
            if (rand.NextDouble() > 0.1f && (whenInEmotionState == null || !entity.HasEmotionState(whenInEmotionState))) return false;

            if (whenInEmotionState != null && !entity.HasEmotionState(whenInEmotionState)) return false;
            if (whenNotInEmotionState != null && entity.HasEmotionState(whenNotInEmotionState)) return false;
            if (lastSearchTotalMs + 4000 > entity.World.ElapsedMilliseconds) return false;
            if (whenInEmotionState == null && rand.NextDouble() > 0.5f) return false;


            if (jumpAnimOn && entity.World.ElapsedMilliseconds - finishedMs > 2000)
            {
                entity.AnimManager.StopAnimation("jump");
            }

            if (cooldownUntilMs > entity.World.ElapsedMilliseconds) return false;

            if (belowTempThreshold > -99)
            {
                ClimateCondition conds = entity.World.BlockAccessor.GetClimateAt(entity.Pos.AsBlockPos, EnumGetClimateMode.NowValues);
                lowTempMode = conds != null && conds.Temperature <= belowTempThreshold;
            }

            float range = lowTempMode ? belowTempSeekingRange : seekingRange;


            lastSearchTotalMs = entity.World.ElapsedMilliseconds;

            Vec3d ownPos = entity.ServerPos.XYZ;

            targetEntity = (EntityAgent)partitionUtil.GetNearestEntity(entity.ServerPos.XYZ, range, (e) => {
                if (!e.Alive || !e.IsInteractable || e.EntityId == this.entity.EntityId) return false;

                for (int i = 0; i < seekEntityCodesExact.Length; i++)
                {
                    if (e.Code.Path == seekEntityCodesExact[i])
                    {
                        if (e.Code.Path == "player")
                        {
                            float rangeMul = e.Stats.GetBlended("animalSeekingRange");

                            IPlayer player = entity.World.PlayerByUid(((EntityPlayer)e).PlayerUID);
                            return
                                (rangeMul == 1 || e.ServerPos.DistanceTo(ownPos) < range * rangeMul) &&
                                (player == null || (player.WorldData.CurrentGameMode != EnumGameMode.Creative && player.WorldData.CurrentGameMode != EnumGameMode.Spectator && (player as IServerPlayer).ConnectionState == EnumClientState.Playing))
                            ;
                        }
                        return true;
                    }
                }


                for (int i = 0; i < seekEntityCodesBeginsWith.Length; i++)
                {
                    if (e.Code.Path.StartsWithFast(seekEntityCodesBeginsWith[i])) return true;
                }

                return false;
            });

            

            if (targetEntity != null)
            {
                if (alarmHerd && entity.HerdId > 0)
                {
                    entity.World.GetNearestEntity(entity.ServerPos.XYZ, range, range, (e) =>
                    {
                        EntityAgent agent = e as EntityAgent;
                        if (e.EntityId != entity.EntityId && agent != null && agent.Alive && agent.HerdId == entity.HerdId)
                        {
                            agent.Notify("seekEntity", targetEntity);
                        }

                        return false;
                    });
                }

                targetPos = targetEntity.ServerPos.XYZ;

                if (entity.ServerPos.SquareDistanceTo(targetPos) <= MinDistanceToTarget())
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        public float MinDistanceToTarget()
        {
            return System.Math.Max(0.1f, targetEntity.CollisionBox.XSize / 2 + entity.CollisionBox.XSize / 4);
        }

        public override void StartExecute()
        {
            base.StartExecute();

            stopNow = false;
            siegeMode = false;

            bool giveUpWhenNoPath = targetPos.SquareDistanceTo(entity.Pos.XYZ) < 12 * 12;
            int searchDepth = 3500;
            // 1 in 20 times we do an expensive search
            if (world.Rand.NextDouble() < 0.05) 
            {
                searchDepth = 10000;
            }

            if (!pathTraverser.NavigateTo(targetPos.Clone(), moveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck, giveUpWhenNoPath, searchDepth, true))
            {
                // If we cannot find a path to the target, let's circle it!
                float angle = (float)Math.Atan2(entity.ServerPos.X - targetPos.X, entity.ServerPos.Z - targetPos.Z);

                double randAngle = angle + 0.5 + world.Rand.NextDouble() / 2;
                
                double distance = 4 + world.Rand.NextDouble() * 6;

                double dx = GameMath.Sin(randAngle) * distance; 
                double dz = GameMath.Cos(randAngle) * distance;
                targetPos = targetPos.AddCopy(dx, 0, dz);

                int tries = 0;
                bool ok = false;
                BlockPos tmp = new BlockPos((int)targetPos.X, (int)targetPos.Y, (int)targetPos.Z);

                int dy = 0;
                while (tries < 5)
                {
                    // Down ok?
                    if (world.BlockAccessor.GetBlock(tmp.X, tmp.Y - dy, tmp.Z).SideSolid[BlockFacing.UP.Index] && !world.CollisionTester.IsColliding(world.BlockAccessor, entity.CollisionBox, new Vec3d(tmp.X + 0.5, tmp.Y - dy + 1, tmp.Z + 0.5), false))
                    {
                        ok = true;
                        targetPos.Y -= dy;
                        targetPos.Y++;
                        siegeMode = true;
                        break;
                    }

                    // Down ok?
                    if (world.BlockAccessor.GetBlock(tmp.X, tmp.Y + dy, tmp.Z).SideSolid[BlockFacing.UP.Index] && !world.CollisionTester.IsColliding(world.BlockAccessor, entity.CollisionBox, new Vec3d(tmp.X + 0.5, tmp.Y + dy + 1, tmp.Z + 0.5), false))
                    {
                        ok = true;
                        targetPos.Y += dy;
                        targetPos.Y++;
                        siegeMode = true;
                        break;

                    }

                    tries++;
                    dy++;
                }



                ok = ok && pathTraverser.NavigateTo(targetPos.Clone(), moveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck, giveUpWhenNoPath, searchDepth, true);

                stopNow = !ok;
            }

            currentFollowTime = 0;
        }


        long jumpedMS = 0;
        float lastPathUpdateSeconds;
        public override bool ContinueExecute(float dt)
        {
            currentFollowTime += dt;
            lastPathUpdateSeconds += dt;

            if (!siegeMode && lastPathUpdateSeconds >= 1 && targetPos.SquareDistanceTo(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z) >= 3*3)
            {
                pathTraverser.NavigateTo(targetPos.Clone(), moveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck, false, 2000, true);
                lastPathUpdateSeconds = 0;
                targetPos.Set(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
            }

            if (jumpAnimOn && entity.World.ElapsedMilliseconds - finishedMs > 2000)
            {
                entity.AnimManager.StopAnimation(leapAnimationCode);
            }

            if (!siegeMode)
            {
                pathTraverser.CurrentTarget.X = targetEntity.ServerPos.X;
                pathTraverser.CurrentTarget.Y = targetEntity.ServerPos.Y;
                pathTraverser.CurrentTarget.Z = targetEntity.ServerPos.Z;
            }

            Cuboidd targetBox = targetEntity.CollisionBox.ToDouble().Translate(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
            Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.CollisionBox.Y2 / 2, 0).Ahead(entity.CollisionBox.XSize / 2, 0, entity.ServerPos.Yaw);
            double distance = targetBox.ShortestDistanceFrom(pos);


            bool inCreativeMode = (targetEntity as EntityPlayer)?.Player?.WorldData.CurrentGameMode == EnumGameMode.Creative;

            if (!inCreativeMode && leapAtTarget && rand.NextDouble() < leapChance)
            {
                bool recentlyJumped = entity.World.ElapsedMilliseconds - jumpedMS < 3000;

                if (distance > 0.5 && distance < 4 && !recentlyJumped && targetEntity.ServerPos.Y + 0.1 >= entity.ServerPos.Y) 
                {
                    double dx = (targetEntity.ServerPos.X + targetEntity.ServerPos.Motion.X * 80 - entity.ServerPos.X) / 30;
                    double dz = (targetEntity.ServerPos.Z + targetEntity.ServerPos.Motion.Z * 80 - entity.ServerPos.Z) / 30;
                    entity.ServerPos.Motion.Add(
                        dx, 
                        leapHeightMul * GameMath.Max(0.13, (targetEntity.ServerPos.Y - entity.ServerPos.Y) / 30), 
                        dz
                    );

                    float yaw = (float)Math.Atan2(dx, dz);
                    entity.ServerPos.Yaw = yaw;


                    jumpedMS = entity.World.ElapsedMilliseconds;
                    finishedMs = entity.World.ElapsedMilliseconds;
                    if (leapAnimationCode != null)
                    {
                        entity.AnimManager.StopAnimation("walk");
                        entity.AnimManager.StopAnimation("run");
                        entity.AnimManager.StartAnimation(new AnimationMetaData() { Animation = leapAnimationCode, Code = leapAnimationCode }.Init());
                        jumpAnimOn = true;
                    }
                }

                if (recentlyJumped && !entity.Collided && distance < 0.5)
                {
                    entity.ServerPos.Motion /= 2;
                }
            }


            float minDist = MinDistanceToTarget();
            float range = lowTempMode ? belowTempSeekingRange : seekingRange;

            return
                currentFollowTime < maxFollowTime &&
                distance < range * range &&
                (distance > minDist || targetEntity.ServerControls.TriesToMove) &&
                targetEntity.Alive &&
                !inCreativeMode &&
                !stopNow
            ;
        }

        


        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);
            finishedMs = entity.World.ElapsedMilliseconds;
            pathTraverser.Stop();
        }


        public override bool Notify(string key, object data)
        {
            if (key == "seekEntity")
            {
                targetEntity = (EntityAgent)data;
                targetPos = targetEntity.ServerPos.XYZ;
                return true;
            }

            return false;
        }


        private void OnStuck()
        {
            stopNow = true;   
        }

        private void OnGoalReached()
        {
            if (!siegeMode)
            {
                pathTraverser.Active = true;
            } else
            {
                stopNow = true;
            }

        }
    }
}
