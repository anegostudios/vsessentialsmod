using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public enum EnumAttackPattern
    {
        DirectAttack,
        BesiegeTarget,
        CircleTarget,
        TacticalRetreat  // ( ͡° ͜ʖ ͡°)
    }

    public class AiTaskSeekEntity : AiTaskBaseTargetable
    {
        protected Vec3d targetPos;
        private readonly Vec3d ownPos = new Vec3d();

        protected float moveSpeed = 0.02f;
        protected float seekingRange = 25f;
        protected float tacticalRetreatRange = 20f;
        protected float belowTempSeekingRange = 25f;
        protected float belowTempThreshold = -999;
        protected float maxFollowTime = 60;

        protected bool stopNow = false;
        protected bool active = false;
        protected float currentFollowTime = 0;

        protected bool alarmHerd = false;
        protected bool leapAtTarget = false;
        protected float leapHeightMul = 1f;
        protected string leapAnimationCode ="jump";
        protected float leapChance = 1f;

        protected EnumAttackPattern attackPattern;

        protected long finishedMs;
        protected bool jumpAnimOn;

        protected long lastSearchTotalMs;
        protected long tacticalRetreatBeginTotalMs;
        protected long attackModeBeginTotalMs;
        protected long lastHurtByTargetTotalMs;

        protected float extraTargetDistance = 0f;

        protected bool lowTempMode;
        protected bool lastPathfindOk;

        protected int searchWaitMs = 4000;

        public float NowSeekRange => lowTempMode? belowTempSeekingRange : seekingRange;

        protected bool RecentlyHurt => entity.World.ElapsedMilliseconds - lastHurtByTargetTotalMs < 10000;
        protected bool RecentlyAttacked => entity.World.ElapsedMilliseconds - attackedByEntityMs < 30000;
        protected bool RemainInTacticalRetreat => entity.World.ElapsedMilliseconds - tacticalRetreatBeginTotalMs < 20000;
        protected bool RemainInOffensiveMode => entity.World.ElapsedMilliseconds - attackModeBeginTotalMs < 20000;

        protected Vec3d lastGoalReachedPos;
        protected Dictionary<long, int> futilityCounters;

        public AiTaskSeekEntity(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            leapAnimationCode = taskConfig["leapAnimation"].AsString("jump");
            leapChance = taskConfig["leapChance"].AsFloat(1);
            leapHeightMul = taskConfig["leapHeightMul"].AsFloat(1);
            moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
            extraTargetDistance = taskConfig["extraTargetDistance"].AsFloat(0f);
            seekingRange = taskConfig["seekingRange"].AsFloat(25);
            belowTempSeekingRange = taskConfig["belowTempSeekingRange"].AsFloat(25);
            belowTempThreshold = taskConfig["belowTempThreshold"].AsFloat(-999);
            maxFollowTime = taskConfig["maxFollowTime"].AsFloat(60);
            alarmHerd = taskConfig["alarmHerd"].AsBool(false);
            leapAtTarget = taskConfig["leapAtTarget"].AsBool(false);
            retaliateAttacks = taskConfig["retaliateAttacks"].AsBool(true);
        }


        public override bool ShouldExecute()
        {
            if (noEntityCodes && (attackedByEntity == null || !retaliateAttacks)) return false;

            // React immediately on hurt, otherwise only 1/10 chance of execution
            if (rand.NextDouble() > 0.1f && (whenInEmotionState == null || IsInEmotionState(whenInEmotionState) != true) && !RecentlyAttacked) return false;

            if (!EmotionStatesSatisifed()) return false;
            if (lastSearchTotalMs + searchWaitMs > entity.World.ElapsedMilliseconds) return false;
            if (whenInEmotionState == null && rand.NextDouble() > 0.5f) return false;
            if (jumpAnimOn && entity.World.ElapsedMilliseconds - finishedMs > 2000)
            {
                entity.AnimManager.StopAnimation("jump");
            }

            if (cooldownUntilMs > entity.World.ElapsedMilliseconds && !RecentlyAttacked) return false;

            if (belowTempThreshold > -99)
            {
                float temperature = entity.World.BlockAccessor.GetClimateAt(entity.Pos.AsBlockPos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, entity.World.Calendar.TotalDays).Temperature;
                lowTempMode = temperature <= belowTempThreshold;
            }

            float range = NowSeekRange;

            lastSearchTotalMs = entity.World.ElapsedMilliseconds;

            if (!RecentlyAttacked)
            {
                attackedByEntity = null;
            }

            if (retaliateAttacks && attackedByEntity != null && attackedByEntity.Alive && attackedByEntity.IsInteractable && IsTargetableEntity(attackedByEntity, range, true))
            {
                targetEntity = attackedByEntity;
                targetPos = targetEntity.ServerPos.XYZ;
                return true;
            }
            else
            {
                ownPos.Set(entity.ServerPos);
                targetEntity = partitionUtil.GetNearestEntity(ownPos, range, (e) => IsTargetableEntity(e, range), EnumEntitySearchType.Creatures);

                if (targetEntity != null)
                {
                    if (alarmHerd && entity.HerdId > 0)
                    {
                        entity.World.GetNearestEntity(ownPos, range, range, (e) =>
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
            }

            return false;
        }

        public float MinDistanceToTarget()
        {
            return extraTargetDistance + Math.Max(0.1f, targetEntity.SelectionBox.XSize / 2 + entity.SelectionBox.XSize / 4);
        }

        public override void StartExecute()
        {
            stopNow = false;
            active = true;
            currentFollowTime = 0;

            if (RemainInTacticalRetreat)
            {
                tryTacticalRetreat();
                return;
            }

            attackPattern = EnumAttackPattern.DirectAttack;
            
            // 1 in 20 times we do an expensive search
            int searchDepth = 3500;
            if (world.Rand.NextDouble() < 0.05) 
            {
                searchDepth = 10000;
            }

            pathTraverser.NavigateTo_Async(targetPos.Clone(), moveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck, OnSeekUnable, searchDepth, 1);

            
        }

        private void OnSeekUnable()
        {
            //Console.WriteLine("unable to seek");

            attackPattern = EnumAttackPattern.BesiegeTarget;
            pathTraverser.NavigateTo_Async(targetPos.Clone(), moveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck, OnSiegeUnable, 3500, 3);
        }

        private void OnSiegeUnable()
        {
            if (targetPos.DistanceTo(entity.ServerPos.XYZ) < NowSeekRange)
            {
                if (!TryCircleTarget())
                {
                    OnCircleTargetUnable();
                }
            }
        }

        public void OnCircleTargetUnable()
        {
            tryTacticalRetreat();
        }

        private bool TryCircleTarget()
        {
            bool giveUpWhenNoPath = targetPos.SquareDistanceTo(entity.Pos) < 12 * 12;
            int searchDepth = 3500;
            attackPattern = EnumAttackPattern.CircleTarget;
            lastPathfindOk = false;

            // If we cannot find a path to the target, let's circle it!
            float angle = (float)Math.Atan2(entity.ServerPos.X - targetPos.X, entity.ServerPos.Z - targetPos.Z);
            
            for (int i = 0; i < 3; i++)
            {
                // We need to avoid crossing the path of the target, so we do only small angle variation between us and the target 
                double randAngle = angle + 0.5 + world.Rand.NextDouble() / 2;

                double distance = 4 + world.Rand.NextDouble() * 6;

                double dx = GameMath.Sin(randAngle) * distance;
                double dz = GameMath.Cos(randAngle) * distance;
                targetPos.Add(dx, 0, dz);

                int tries = 0;
                bool ok = false;
                BlockPos tmp = new BlockPos((int)targetPos.X, (int)targetPos.Y, (int)targetPos.Z);

                int dy = 0;
                while (tries < 5)
                {
                    // Down ok?
                    if (world.BlockAccessor.GetBlock(tmp.X, tmp.Y - dy, tmp.Z).SideSolid[BlockFacing.UP.Index] && !world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, new Vec3d(tmp.X + 0.5, tmp.Y - dy + 1, tmp.Z + 0.5), false))
                    {
                        ok = true;
                        targetPos.Y -= dy;
                        targetPos.Y++;
                        break;
                    }

                    // Up ok?
                    if (world.BlockAccessor.GetBlock(tmp.X, tmp.Y + dy, tmp.Z).SideSolid[BlockFacing.UP.Index] && !world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, new Vec3d(tmp.X + 0.5, tmp.Y + dy + 1, tmp.Z + 0.5), false))
                    {
                        ok = true;
                        targetPos.Y += dy;
                        targetPos.Y++;
                        break;
                    }

                    tries++;
                    dy++;
                }

                if (ok)
                {
                    pathTraverser.NavigateTo_Async(targetPos.Clone(), moveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck, OnCircleTargetUnable, searchDepth, 1);
                    return true;
                }
            }

            return false;
        }

        private void tryTacticalRetreat()
        {
            if (RemainInOffensiveMode) return;

            //Console.WriteLine("doTacticalRetreatIfHurt");

            // Found no circling path, let's run away then
            if (RecentlyHurt || RemainInTacticalRetreat)
            {
               // Console.WriteLine("tactical retreat!");

                updateTargetPosFleeMode(targetPos);
                float size = targetEntity.SelectionBox.XSize;
                pathTraverser.WalkTowards(targetPos, moveSpeed, size + 0.2f, OnGoalReached, OnStuck);
                if (attackPattern != EnumAttackPattern.TacticalRetreat) tacticalRetreatBeginTotalMs = entity.World.ElapsedMilliseconds;

                attackPattern = EnumAttackPattern.TacticalRetreat;
                attackedByEntity = null;
            }
        }


        public override bool CanContinueExecute()
        {
            if (pathTraverser.Ready)
            {
                attackModeBeginTotalMs = entity.World.ElapsedMilliseconds;
                lastPathfindOk = true;
            }

            return pathTraverser.Ready || attackPattern == EnumAttackPattern.TacticalRetreat;
        }

        long jumpedMS = 0;
        float lastPathUpdateSeconds;
        public override bool ContinueExecute(float dt)
        {
            if (currentFollowTime == 0)  // quick and dirty test for whether this is the first continue after StartExecute()
            {
                // make sounds if appropriate
                if (!stopNow || world.Rand.NextDouble() < 0.25)
                {
                    base.StartExecute();
                }
            }

            tacticalRetreatRange = Math.Max(20f, tacticalRetreatRange - dt/4f);

            currentFollowTime += dt;
            lastPathUpdateSeconds += dt;

            if (attackPattern == EnumAttackPattern.TacticalRetreat && world.Rand.NextDouble() < 0.2)
            {
                updateTargetPosFleeMode(targetPos);
                pathTraverser.CurrentTarget.X = targetPos.X;
                pathTraverser.CurrentTarget.Y = targetPos.Y;
                pathTraverser.CurrentTarget.Z = targetPos.Z;
            }

            if (attackPattern != EnumAttackPattern.TacticalRetreat)
            {
                if (RecentlyHurt && !lastPathfindOk)
                {
                    tryTacticalRetreat();
                }

                if (attackPattern == EnumAttackPattern.DirectAttack && lastPathUpdateSeconds >= 0.75f && targetPos.SquareDistanceTo(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z) >= 3 * 3)
                {
                    targetPos.Set(targetEntity.ServerPos.X + targetEntity.ServerPos.Motion.X * 10, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z + targetEntity.ServerPos.Motion.Z * 10);

                    pathTraverser.NavigateTo(targetPos, moveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck, false, 2000, 1);
                    lastPathUpdateSeconds = 0;
                }

                if (leapAtTarget && !entity.AnimManager.IsAnimationActive(animMeta.Code) && entity.AnimManager.Animator.GetAnimationState(leapAnimationCode)?.Active != true)
                {
                    animMeta.EaseInSpeed = 1f;
                    animMeta.EaseOutSpeed = 1f;
                    entity.AnimManager.StartAnimation(animMeta);
                }

                if (jumpAnimOn && entity.World.ElapsedMilliseconds - finishedMs > 2000)
                {
                    entity.AnimManager.StopAnimation(leapAnimationCode);
                    animMeta.EaseInSpeed = 1f;
                    animMeta.EaseOutSpeed = 1f;
                    entity.AnimManager.StartAnimation(animMeta);
                }

                if (attackPattern == EnumAttackPattern.DirectAttack || attackPattern == EnumAttackPattern.BesiegeTarget)
                {
                    pathTraverser.CurrentTarget.X = targetEntity.ServerPos.X;
                    pathTraverser.CurrentTarget.Y = targetEntity.ServerPos.Y;
                    pathTraverser.CurrentTarget.Z = targetEntity.ServerPos.Z;
                }
            }

            Cuboidd targetBox = targetEntity.SelectionBox.ToDouble().Translate(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
            Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.ServerPos.Yaw);
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
            bool doContinue = targetEntity.Alive && !stopNow && !inCreativeMode && pathTraverser.Active;

            if (attackPattern == EnumAttackPattern.TacticalRetreat)
            {
                return doContinue && currentFollowTime < 9 && distance < tacticalRetreatRange;
                
            } else
            {
                return 
                    doContinue &&
                    currentFollowTime < maxFollowTime &&
                    distance < NowSeekRange &&
                    (distance > minDist || (targetEntity is EntityAgent ea && ea.ServerControls.TriesToMove))
                ;
            }
        }

        


        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);
            finishedMs = entity.World.ElapsedMilliseconds;
            pathTraverser.Stop();
            active = false;
        }


        public override bool Notify(string key, object data)
        {
            if (key == "seekEntity")
            {
                targetEntity = (Entity)data;
                targetPos = targetEntity.ServerPos.XYZ;
                return true;
            }

            return false;
        }

        public override void OnEntityHurt(DamageSource source, float damage)
        {
            base.OnEntityHurt(source, damage);

            if (targetEntity == source.GetCauseEntity() || !active)
            {
                lastHurtByTargetTotalMs = entity.World.ElapsedMilliseconds;
                float dist = targetEntity == null ? 0 : (float)targetEntity.ServerPos.DistanceTo(entity.ServerPos);
                //Console.WriteLine("max of {0} and {1}", tacticalRetreatRange, (int)dist + 15);
                tacticalRetreatRange = Math.Max(tacticalRetreatRange, dist + 15);
            }
        }

        private void OnStuck()
        {
            stopNow = true;
            //Console.WriteLine("stuck!");
        }

        private void OnGoalReached()
        {
            if (attackPattern == EnumAttackPattern.DirectAttack || attackPattern == EnumAttackPattern.BesiegeTarget)
            {
                if (lastGoalReachedPos != null && lastGoalReachedPos.SquareDistanceTo(entity.Pos) < 0.001)   // Basically we haven't moved since last time, so we are stuck somehow: e.g. bears chasing small creatures into a hole or crevice
                {
                    if (futilityCounters == null) futilityCounters = new Dictionary<long, int>();
                    else
                    {
                        futilityCounters.TryGetValue(targetEntity.EntityId, out int futilityCounter);
                        futilityCounter++;
                        futilityCounters[targetEntity.EntityId] = futilityCounter;
                        if (futilityCounter > 19) return;
                    }
                }
                lastGoalReachedPos = new Vec3d(entity.Pos);
                pathTraverser.Retarget();
                return;
            } else
            {
                //stopNow = true; - doesn't improve ai behavior
            }

        }

        public override bool CanSense(Entity e, double range)
        {
            bool result = base.CanSense(e, range);

            // Do not target entities which have a positive futility value, but slowly decrease that value so that they can eventually be retargeted
            if (result && futilityCounters != null && futilityCounters.TryGetValue(e.EntityId, out int futilityCounter) && futilityCounter > 0)
            {
                futilityCounter -= 2;
                futilityCounters[e.EntityId] = futilityCounter;
                return false;
            }
            return result;
        }
    }
}
