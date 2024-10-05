using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Vintagestory.Essentials
{
    public class WaypointsTraverser : PathTraverserBase
    {
        float minTurnAnglePerSec;
        float maxTurnAnglePerSec;
        
        Vec3f targetVec = new Vec3f();

        List<Vec3d> waypoints;
        List<Vec3d> newWaypoints;
        private PathfinderTask asyncSearchObject;

        int waypointToReachIndex = 0;
        long lastWaypointIncTotalMs;
        Vec3d desiredTarget;

        PathfindSystem psys;
        private PathfindingAsync asyncPathfinder;

        public bool PathFindDebug = false;

        public override Vec3d CurrentTarget {
            get
            {
                return waypoints[waypoints.Count - 1];
            }
        }

        public override bool Ready
        {
            get
            {
                return waypoints != null && asyncSearchObject == null;
            }
        }

        // These next five fields are used to save parameters, ready for the AfterPathFound() call which might be next tick or even later, after asynchronous pathfinding has finished
        private Action OnNoPath;
        private Action OnGoalReached_New;
        private Action OnStuck_New;
        private float movingSpeed_New;
        private float targetDistance_New;

        public WaypointsTraverser(EntityAgent entity) : base(entity)
        {
            if (entity?.Properties.Server?.Attributes?.GetTreeAttribute("pathfinder") != null)
            {
                minTurnAnglePerSec = (float)entity.Properties.Server.Attributes.GetTreeAttribute("pathfinder").GetDecimal("minTurnAnglePerSec", 250);
                maxTurnAnglePerSec = (float)entity.Properties.Server.Attributes.GetTreeAttribute("pathfinder").GetDecimal("maxTurnAnglePerSec", 450);
            } else
            {
                minTurnAnglePerSec = 250;
                maxTurnAnglePerSec = 450;
            }

            psys = entity.World.Api.ModLoader.GetModSystem<PathfindSystem>();
            asyncPathfinder = entity.World.Api.ModLoader.GetModSystem<PathfindingAsync>();
        }


        public override bool NavigateTo(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck, Action onNoPath = null, bool giveUpWhenNoPath = false, int searchDepth = 999, int mhdistanceTolerance = 0)
        {
            this.desiredTarget = target;
            this.OnNoPath = onNoPath;
            this.OnStuck_New = OnStuck;
            this.OnGoalReached_New = OnGoalReached;
            this.movingSpeed_New = movingSpeed;
            this.targetDistance_New = targetDistance;

            BlockPos startBlockPos = entity.ServerPos.AsBlockPos;
            if (entity.World.BlockAccessor.IsNotTraversable(startBlockPos))
            {
                HandleNoPath();
                return false;
            }

            FindPath(startBlockPos, target.AsBlockPos, searchDepth, mhdistanceTolerance);

            return AfterFoundPath();
        }


        public override bool NavigateTo_Async(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck, Action onNoPath = null, int searchDepth = 999, int mhdistanceTolerance = 0)
        {
            if (this.asyncSearchObject != null) return false;  //Allow the one in progress to finish before trying another - maybe more than one AI task in the same tick tries to find a path?

            this.desiredTarget = target;

            // these all have to be saved because they are local parameters, but not used until we call AfterFoundPath()
            this.OnNoPath = onNoPath;
            this.OnGoalReached_New = OnGoalReached;
            this.OnStuck_New = OnStuck;
            this.movingSpeed_New = movingSpeed;
            this.targetDistance_New = targetDistance;

            BlockPos startBlockPos = entity.ServerPos.AsBlockPos;
            if (entity.World.BlockAccessor.IsNotTraversable(startBlockPos))
            {
                HandleNoPath();
                return false;
            }

            FindPath_Async(startBlockPos, target.AsBlockPos, searchDepth, mhdistanceTolerance);

            return true;
        }


        private void FindPath(BlockPos startBlockPos, BlockPos targetBlockPos, int searchDepth, int mhdistanceTolerance = 0)
        {
            waypointToReachIndex = 0;

            var bh = entity.GetBehavior<EntityBehaviorControlledPhysics>();
            float stepHeight = bh == null ? 0.6f : bh.StepHeight;
            int maxFallHeight = entity.Properties.FallDamage ? Math.Min(8, (int)Math.Round(3.51 / Math.Max(0.01, entity.Properties.FallDamageMultiplier))) - (int)(movingSpeed * 30) : 8;   // fast moving entities cannot safely fall so far (might miss target block below due to outward drift)

            newWaypoints = psys.FindPathAsWaypoints(startBlockPos, targetBlockPos, maxFallHeight, stepHeight, entity.CollisionBox, searchDepth, mhdistanceTolerance);
        }


        public PathfinderTask PreparePathfinderTask(BlockPos startBlockPos, BlockPos targetBlockPos, int searchDepth = 999, int mhdistanceTolerance = 0)
        {
            var bh = entity.GetBehavior<EntityBehaviorControlledPhysics>();
            float stepHeight = bh == null ? 0.6f : bh.StepHeight;
            bool avoidFall = entity.Properties.FallDamage && entity.Properties.Attributes?["reckless"].AsBool(false) != true;
            int maxFallHeight = avoidFall ? 4 - (int)(movingSpeed * 30) : 12;   // fast moving entities cannot safely fall so far (might miss target block below due to outward drift)

            return new PathfinderTask(startBlockPos, targetBlockPos, maxFallHeight, stepHeight, entity.CollisionBox, searchDepth, mhdistanceTolerance);
        }


        private void FindPath_Async(BlockPos startBlockPos, BlockPos targetBlockPos, int searchDepth, int mhdistanceTolerance = 0)
        {
            waypointToReachIndex = 0;
            asyncSearchObject = PreparePathfinderTask(startBlockPos, targetBlockPos, searchDepth, mhdistanceTolerance);
            asyncPathfinder.EnqueuePathfinderTask(asyncSearchObject);
        }


        public bool AfterFoundPath()
        {
            if (asyncSearchObject != null)
            {
                newWaypoints = asyncSearchObject.waypoints;
                asyncSearchObject = null;
            }

            if (newWaypoints == null /*|| newWaypoints.Count == 0 - uh no. this is a successful search*/)
            {
                HandleNoPath();
                return false;
            }

            waypoints = newWaypoints;

            // Debug visualization
            if (PathFindDebug)
            {
                List<BlockPos> poses = new List<BlockPos>();
                List<int> colors = new List<int>();
                int i = 0;
                foreach (var node in waypoints)
                {
                    poses.Add(node.AsBlockPos);
                    colors.Add(ColorUtil.ColorFromRgba(128, 128, Math.Min(255, 128 + i * 8), 150));
                    i++;
                }

                poses.Add(desiredTarget.AsBlockPos);
                colors.Add(ColorUtil.ColorFromRgba(128, 0, 255, 255));

                IPlayer player = entity.World.AllOnlinePlayers[0];
                entity.World.HighlightBlocks(player, 2, poses,
                    colors,
                    API.Client.EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary
                );
            }

            waypoints.Add(desiredTarget);

            base.WalkTowards(desiredTarget, movingSpeed_New, targetDistance_New, OnGoalReached_New, OnStuck_New);

            return true;
        }


        public void HandleNoPath()
        {
            waypoints = new List<Vec3d>();

            if (PathFindDebug)
            {
                // Debug visualization
                List<BlockPos> poses = new List<BlockPos>();
                List<int> colors = new List<int>();
                int i = 0;
                foreach (var node in entity.World.Api.ModLoader.GetModSystem<PathfindSystem>().astar.closedSet)
                {
                    poses.Add(node);
                    colors.Add(ColorUtil.ColorFromRgba(Math.Min(255, i * 4), 0, 0, 150));
                    i++;
                }

                IPlayer player = entity.World.AllOnlinePlayers[0];
                entity.World.HighlightBlocks(player, 2, poses,
                    colors,
                    API.Client.EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary
                );
            }

            waypoints.Add(desiredTarget);

            base.WalkTowards(desiredTarget, movingSpeed_New, targetDistance_New, OnGoalReached_New, OnStuck_New);

            if (OnNoPath != null)
            {
                Active = false;
                OnNoPath.Invoke();
            }
        }


        public override bool WalkTowards(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck)
        {
            waypoints = new List<Vec3d>();
            waypoints.Add(target);

            return base.WalkTowards(target, movingSpeed, targetDistance, OnGoalReached, OnStuck);
        }


        protected override bool BeginGo()
        {
            entity.Controls.Forward = true;
            entity.ServerControls.Forward = true;
            curTurnRadPerSec = minTurnAnglePerSec + (float)entity.World.Rand.NextDouble() * (maxTurnAnglePerSec - minTurnAnglePerSec);
            curTurnRadPerSec *= GameMath.DEG2RAD * 50;

            stuckCounter = 0;
            waypointToReachIndex = 0;
            lastWaypointIncTotalMs = entity.World.ElapsedMilliseconds;
            distCheckAccum = 0;
            prevPosAccum = 0;

            return true;
        }

        Vec3d prevPos = new Vec3d(0, -2000, 0);
        Vec3d prevPrevPos = new Vec3d(0, -1000, 0);
        float prevPosAccum;
        float sqDistToTarget;

        float distCheckAccum = 0;
        float lastDistToTarget = 0;


        public override void OnGameTick(float dt)
        {
            if (asyncSearchObject != null)
            {
                if (!asyncSearchObject.Finished) return;

                AfterFoundPath();
            }

            if (!Active) return;

            bool nearHorizontally = false;
            int offset = 0;
            bool nearAllDirs =
                IsNearTarget(offset++, ref nearHorizontally)
                || IsNearTarget(offset++, ref nearHorizontally)
                || IsNearTarget(offset++, ref nearHorizontally)
            ;

            if (nearAllDirs)
            {
                waypointToReachIndex += offset;
                lastWaypointIncTotalMs = entity.World.ElapsedMilliseconds;
            }

            target = waypoints[Math.Min(waypoints.Count - 1, waypointToReachIndex)];

            bool onlastWaypoint = waypointToReachIndex == waypoints.Count - 1;

            if (waypointToReachIndex >= waypoints.Count)
            {
                Stop();
                OnGoalReached?.Invoke();
                return;
            }

            bool stuckBelowOrAbove = (nearHorizontally && !nearAllDirs && entity.Properties.Habitat == EnumHabitat.Land);

            bool stuck =
                (entity.CollidedVertically && entity.Controls.IsClimbing)
                || (entity.CollidedHorizontally && entity.ServerPos.Motion.Y <= 0)
                || stuckBelowOrAbove
                || (entity.CollidedHorizontally && waypoints.Count > 1 && waypointToReachIndex < waypoints.Count && entity.World.ElapsedMilliseconds - lastWaypointIncTotalMs > 2000)    // If it takes more than 2 seconds to reach next waypoint (waypoints are always 1 block apart)
            ;

            // This used to test motion, but that makes no sense, we want to test if the entity moved, not if it had motion
            double distsq = prevPrevPos.SquareDistanceTo(prevPos);
            stuck |= (distsq < 0.01 * 0.01) ? (entity.World.Rand.NextDouble() < GameMath.Clamp(1 - distsq * 1.2, 0.1, 0.9)) : false;


            // Test movement progress between two points in 150 millisecond intervalls
            prevPosAccum += dt;
            if (prevPosAccum > 0.2)
            {
                prevPosAccum = 0;
                prevPrevPos.Set(prevPos);
                prevPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            }

            // Long duration tests to make sure we're not just wobbling around in the same spot
            distCheckAccum += dt;
            if (distCheckAccum > 2)
            {
                distCheckAccum = 0;
                if (Math.Abs(sqDistToTarget - lastDistToTarget) < 0.1)
                {
                    stuck = true;
                    stuckCounter += 30;
                }
                else if (!stuck) stuckCounter = 0;    // Only reset the stuckCounter in same tick as doing this test; otherwise the stuckCounter gets set to 0 every 2 or 3 ticks even if the entity collided horizontally (because motion vecs get set to 0 after the collision, so won't collide in the successive tick)
                lastDistToTarget = sqDistToTarget;
            }

            if (stuck)
            {
                stuckCounter++;
            }
            
            if (GlobalConstants.OverallSpeedMultiplier > 0 && stuckCounter > 60 / GlobalConstants.OverallSpeedMultiplier)
            {
                //entity.World.SpawnParticles(10, ColorUtil.WhiteArgb, prevPos, prevPos, new Vec3f(0, 0, 0), new Vec3f(0, -1, 0), 1, 1);
                Stop();
                OnStuck?.Invoke();
                return;
            }

            EntityControls controls = entity.MountedOn == null ? entity.Controls : entity.MountedOn.Controls;
            if (controls == null) return;

            targetVec.Set(
                (float)(target.X - entity.ServerPos.X),
                (float)(target.Y - entity.ServerPos.Y),
                (float)(target.Z - entity.ServerPos.Z)
            );
            targetVec.Normalize();

            //entity.World.SpawnParticles(1, ColorUtil.WhiteArgb, target, target, new Vec3f(), new Vec3f(), 0.2f, 0, 1);

            float desiredYaw = 0;
            
            if (sqDistToTarget >= 0.01)
            {
                desiredYaw = (float)Math.Atan2(targetVec.X, targetVec.Z);
            }

            float nowMoveSpeed = movingSpeed;

            if (sqDistToTarget < 1)
            {
                nowMoveSpeed = Math.Max(0.005f, movingSpeed * Math.Max(sqDistToTarget, 0.2f));
            }

            float yawDist = GameMath.AngleRadDistance(entity.ServerPos.Yaw, desiredYaw);
            float turnSpeed = curTurnRadPerSec * dt * GlobalConstants.OverallSpeedMultiplier * movingSpeed;
            entity.ServerPos.Yaw += GameMath.Clamp(yawDist, -turnSpeed, turnSpeed);
            entity.ServerPos.Yaw = entity.ServerPos.Yaw % GameMath.TWOPI;

            

            double cosYaw = Math.Cos(entity.ServerPos.Yaw);
            double sinYaw = Math.Sin(entity.ServerPos.Yaw);
            controls.WalkVector.Set(sinYaw, GameMath.Clamp(targetVec.Y, -1, 1), cosYaw);
            controls.WalkVector.Mul(nowMoveSpeed * GlobalConstants.OverallSpeedMultiplier / Math.Max(1, Math.Abs(yawDist)*3));

            // Make it walk along the wall, but not walk into the wall, which causes it to climb
            if (entity.Properties.RotateModelOnClimb && entity.Controls.IsClimbing && entity.ClimbingIntoFace != null && entity.Alive)
            {
                BlockFacing facing = entity.ClimbingIntoFace;
                if (Math.Sign(facing.Normali.X) == Math.Sign(controls.WalkVector.X))
                {
                    controls.WalkVector.X = 0;
                }

                if (Math.Sign(facing.Normali.Y) == Math.Sign(controls.WalkVector.Y))
                {
                    controls.WalkVector.Y = -controls.WalkVector.Y;
                }

                if (Math.Sign(facing.Normali.Z) == Math.Sign(controls.WalkVector.Z))
                {
                    controls.WalkVector.Z = 0;
                }
            }

            //   entity.World.SpawnParticles(0.3f, ColorUtil.WhiteAhsl, target, target, new Vec3f(), new Vec3f(), 0.1f, 0.1f, 3f, EnumParticleModel.Cube);

            if (entity.Properties.Habitat == EnumHabitat.Underwater)
            {
                controls.FlyVector.Set(controls.WalkVector);

                Vec3d pos = entity.Pos.XYZ;
                Block inblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y), (int)pos.Z, BlockLayersAccess.Fluid);
                Block aboveblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y + 1), (int)pos.Z, BlockLayersAccess.Fluid);
                float waterY = (int)pos.Y + inblock.LiquidLevel / 8f + (aboveblock.IsLiquid() ? 9 / 8f : 0);
                float bottomSubmergedness = waterY - (float)pos.Y;

                // 0 = at swim line  1 = completely submerged
                float swimlineSubmergedness = GameMath.Clamp(bottomSubmergedness - ((float)entity.SwimmingOffsetY), 0, 1);
                swimlineSubmergedness = 1f - Math.Min(1f, swimlineSubmergedness + 0.5f);
                if (swimlineSubmergedness > 0f)
                {
                    //Push the fish back underwater if part is poking out ...  (may need future adaptation for sharks[?], probably by changing SwimmingOffsetY)
                    controls.FlyVector.Y = GameMath.Clamp(controls.FlyVector.Y, -0.04f, -0.02f) * (1f - swimlineSubmergedness);
                }
                else
                {
                    float factor = movingSpeed * GlobalConstants.OverallSpeedMultiplier / (float) Math.Sqrt(targetVec.X * targetVec.X + targetVec.Z * targetVec.Z);
                    controls.FlyVector.Y = targetVec.Y * factor;
                }
            }
            else if (entity.Swimming)
            {
                controls.FlyVector.Set(controls.WalkVector);

                Vec3d pos = entity.Pos.XYZ;
                Block inblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y), (int)pos.Z, BlockLayersAccess.Fluid);
                Block aboveblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y + 1), (int)pos.Z, BlockLayersAccess.Fluid);
                float waterY = (int)pos.Y + inblock.LiquidLevel / 8f + (aboveblock.IsLiquid() ? 9 / 8f : 0);
                float bottomSubmergedness = waterY - (float)pos.Y;

                // 0 = at swim line
                // 1 = completely submerged
                float swimlineSubmergedness = GameMath.Clamp(bottomSubmergedness - ((float)entity.SwimmingOffsetY), 0, 1);
                swimlineSubmergedness = Math.Min(1, swimlineSubmergedness + 0.5f);
                controls.FlyVector.Y = GameMath.Clamp(controls.FlyVector.Y, 0.02f, 0.04f) * swimlineSubmergedness;


                if (entity.CollidedHorizontally)
                {
                    controls.FlyVector.Y = 0.05f;
                }
            }
        }


        bool IsNearTarget(int waypointOffset, ref bool nearHorizontally)
        {
            if (waypoints.Count - 1 < waypointToReachIndex + waypointOffset) return false;

            int wayPointIndex = Math.Min(waypoints.Count - 1, waypointToReachIndex + waypointOffset);
            Vec3d target = waypoints[wayPointIndex];

            double curPosY = entity.ServerPos.Y;
            sqDistToTarget = target.HorizontalSquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Z);

            var vdistsq = (target.Y - curPosY) * (target.Y - curPosY);
            bool above = curPosY > target.Y;
            sqDistToTarget += (float)Math.Max(0, vdistsq - (above ? 1 : 0.5)); // Ok to be up to 1 block above or 0.5 blocks below

            if (!nearHorizontally)
            {
                double horsqDistToTarget = target.HorizontalSquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Z);
                nearHorizontally = horsqDistToTarget < targetDistance * targetDistance;
            }

            return sqDistToTarget < targetDistance * targetDistance;
        }

        private float DiffSquared(double y1, double y2)
        {
            double diff = y1 - y2;
            return (float)(diff * diff);
        }

        public override void Stop()
        {
            Active = false;
            entity.Controls.Forward = false;
            entity.ServerControls.Forward = false;
            entity.Controls.WalkVector.Set(0, 0, 0);
            stuckCounter = 0;
            distCheckAccum = 0;
            prevPosAccum = 0;
            asyncSearchObject = null;
        }

        public override void Retarget()
        {
            Active = true;
            distCheckAccum = 0;
            prevPosAccum = 0;

            waypointToReachIndex = waypoints.Count - 1;
        }
    }
}
