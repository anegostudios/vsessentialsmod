using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class AiTaskSeekEntity : AiTaskBase
    {
        EntityAgent targetEntity;
        Vec3d targetPos;

        float moveSpeed = 0.02f;
        float seekingRange = 25f;
        float maxFollowTime = 60;
        
        bool stopNow = false;
        string[] seekEntityCodesExact = new string[] { "player" };
        string[] seekEntityCodesBeginsWith = new string[0];

        float currentFollowTime = 0;

        bool alarmHerd = false;
        bool leapAtTarget = false;

        bool siegeMode;

        long finishedMs;
        bool jumpAnimOn;

        EntityPartitioning partitionUtil;

        public AiTaskSeekEntity(EntityAgent entity) : base(entity)
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
            if (rand.NextDouble() > 0.1f) return false; // was 0.04 before, but that made creatures react very slowly when in emotionstate aggressiveondamage. So lets double the reaction time when in that emotion state

            if (whenInEmotionState != null && !entity.HasEmotionState(whenInEmotionState)) return false;
            if (whenNotInEmotionState != null && entity.HasEmotionState(whenNotInEmotionState)) return false;

            if (whenInEmotionState == null && rand.NextDouble() > 0.5f) return false;


            if (jumpAnimOn && entity.World.ElapsedMilliseconds - finishedMs > 2000)
            {
                entity.AnimManager.StopAnimation("jump");
            }

            if (cooldownUntilMs > entity.World.ElapsedMilliseconds) return false;



            targetEntity = (EntityAgent)partitionUtil.GetNearestEntity(entity.ServerPos.XYZ, seekingRange, (e) => {
                if (!e.Alive || !e.IsInteractable || e.EntityId == this.entity.EntityId) return false;

                for (int i = 0; i < seekEntityCodesExact.Length; i++)
                {
                    if (e.Code.Path == seekEntityCodesExact[i])
                    {
                        if (e.Code.Path == "player")
                        {
                            IPlayer player = entity.World.PlayerByUid(((EntityPlayer)e).PlayerUID);
                            return 
                                player == null || 
                                (player.WorldData.CurrentGameMode != EnumGameMode.Creative && player.WorldData.CurrentGameMode != EnumGameMode.Spectator && (player as IServerPlayer).ConnectionState == EnumClientState.Playing);
                        }
                        return true;
                    }
                }


                for (int i = 0; i < seekEntityCodesBeginsWith.Length; i++)
                {
                    if (e.Code.Path.StartsWith(seekEntityCodesBeginsWith[i])) return true;
                }

                return false;
            });

            if (targetEntity != null)
            {
                if (alarmHerd && entity.HerdId > 0)
                {
                    entity.World.GetNearestEntity(entity.ServerPos.XYZ, seekingRange, seekingRange, (e) =>
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
            return System.Math.Max(0.1f, (targetEntity.CollisionBox.X2 - targetEntity.CollisionBox.X1) / 2 + (entity.CollisionBox.X2 - entity.CollisionBox.X1) / 4);
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
                entity.AnimManager.StopAnimation("jump");
            }

            if (!siegeMode)
            {
                pathTraverser.CurrentTarget.X = targetEntity.ServerPos.X;
                pathTraverser.CurrentTarget.Y = targetEntity.ServerPos.Y;
                pathTraverser.CurrentTarget.Z = targetEntity.ServerPos.Z;
            }

            Cuboidd targetBox = targetEntity.CollisionBox.ToDouble().Translate(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
            Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.CollisionBox.Y2 / 2, 0).Ahead((entity.CollisionBox.X2 - entity.CollisionBox.X1) / 2, 0, entity.ServerPos.Yaw);
            double distance = targetBox.ShortestDistanceFrom(pos);


            bool inCreativeMode = (targetEntity as EntityPlayer)?.Player?.WorldData.CurrentGameMode == EnumGameMode.Creative;

            if (!inCreativeMode && leapAtTarget)
            {
                bool recentlyJumped = entity.World.ElapsedMilliseconds - jumpedMS < 3000;

                if (distance > 0.5 && distance < 4 && !recentlyJumped && targetEntity.ServerPos.Y + 0.1 >= entity.ServerPos.Y) 
                {
                    entity.ServerPos.Motion.Add((targetEntity.ServerPos.X - entity.ServerPos.X) / 20, GameMath.Max(0.15, (targetEntity.ServerPos.Y - entity.ServerPos.Y) / 20), (targetEntity.ServerPos.Z - entity.ServerPos.Z) / 20);
                    jumpedMS = entity.World.ElapsedMilliseconds;
                    entity.AnimManager.StopAnimation("walk");
                    entity.AnimManager.StopAnimation("run");
                    entity.AnimManager.StartAnimation(new AnimationMetaData() { Animation = "jump", Code = "jump" }.Init());
                    finishedMs = entity.World.ElapsedMilliseconds;
                    jumpAnimOn = true;
                }

                if (recentlyJumped && !entity.Collided && distance < 0.5)
                {
                    entity.ServerPos.Motion /= 2;
                }
            }


            float minDist = MinDistanceToTarget();

            return
                currentFollowTime < maxFollowTime &&
                distance < seekingRange * seekingRange &&
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
