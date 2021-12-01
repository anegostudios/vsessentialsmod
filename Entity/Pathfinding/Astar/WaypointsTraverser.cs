using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Vintagestory.Essentials
{
    public class WaypointsTraverser : PathTraverserBase
    {
        float minTurnAnglePerSec;
        float maxTurnAnglePerSec;
        
        Vec3f targetVec = new Vec3f();

        List<Vec3d> waypoints;
        int waypointToReachIndex = 0;
        long lastWaypointIncTotalMs;


        PathfindSystem psys;

        public override Vec3d CurrentTarget {
            get
            {
                return waypoints[waypoints.Count - 1];
            }
        }

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
        }



        public override bool NavigateTo(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck, bool giveUpWhenNoPath = false, int searchDepth = 999, bool allowReachAlmost = false)
        {
            BlockPos startBlockPos = entity.ServerPos.AsBlockPos;
            waypointToReachIndex = 0;

            var bh = entity.GetBehavior<EntityBehaviorControlledPhysics>();
            float stepHeight = bh == null ? 0.6f : bh.stepHeight;
            bool canFallDamage = entity.Properties.FallDamage;

            if (!entity.World.BlockAccessor.IsNotTraversable(startBlockPos))
            {
                waypoints = psys.FindPathAsWaypoints(startBlockPos, target.AsBlockPos, canFallDamage ? 8 : 4, stepHeight, entity.CollisionBox, searchDepth, allowReachAlmost);
            }

            bool nopath = false;

            if (waypoints == null)
            {
                waypoints = new List<Vec3d>();
                nopath = true;

                entity.OnNoPath(target);

                // Debug visualization
                /*List<BlockPos> poses = new List<BlockPos>();
                List<int> colors = new List<int>();
                int i = 0;
                foreach (var node in entity.World.Api.ModLoader.GetModSystem<PathfindSystem>().astar.closedSet)
                {
                    poses.Add(node);
                    colors.Add(ColorUtil.ColorFromRgba(Math.Min(255, i * 2), 0, 0, 150));
                    i++;
                }

                IPlayer player = entity.World.AllOnlinePlayers[0];
                entity.World.HighlightBlocks(player, 2, poses, 
                    colors, 
                    API.Client.EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary
                );*/
            }
            else
            {
                // Debug visualization
                /*List<BlockPos> poses = new List<BlockPos>();
                List<int> colors = new List<int>();
                int i = 0;
                foreach (var node in waypoints)
                {
                    poses.Add(node.AsBlockPos);
                    colors.Add(ColorUtil.ColorFromRgba(128, 128, Math.Min(255, 128 + i*8), 150));
                    i++;
                }

                IPlayer player = entity.World.AllOnlinePlayers[0];
                entity.World.HighlightBlocks(player, 2, poses, 
                    colors, 
                    API.Client.EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary
                );*/
            }

            waypoints.Add(target);

            
            bool ok = base.WalkTowards(target, movingSpeed, targetDistance, OnGoalReached, OnStuck);

            if (nopath && giveUpWhenNoPath)
            {
                Active = false;
                return false;
            }

            return ok;
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
            curTurnRadPerSec *= GameMath.DEG2RAD * 50 * movingSpeed;

            stuckCounter = 0;
            lastWaypointIncTotalMs = entity.World.ElapsedMilliseconds;

            return true;
        }

        Vec3d prevPos = new Vec3d(0, -2000, 0);
        Vec3d prevPrevPos = new Vec3d(0, -1000, 0);
        float prevPosAccum;

        public override void OnGameTick(float dt)
        {
            if (!Active) return;

            int wayPointIndex = Math.Min(waypoints.Count - 1, waypointToReachIndex);
            Vec3d target = waypoints[wayPointIndex];

            // Due to the nature of gravity and going down slope we sometimes end up at the next waypoint. So lets also test for the next waypoint
            // Doesn't seem to fully fix the issue though
            int nextwayPointIndex = Math.Min(waypoints.Count - 1, waypointToReachIndex + 1);
            Vec3d nexttarget = waypoints[nextwayPointIndex];

            // For land dwellers only check horizontal distance
            double sqDistToTarget = Math.Min(
                Math.Min(target.SquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z), target.SquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Y - 1, entity.ServerPos.Z)),       // One block above is also ok
                target.SquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Y + 0.5f, entity.ServerPos.Z) // Half a block below is also okay
            );
            double horsqDistToTarget = target.HorizontalSquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Z);

            double sqDistToNextTarget = Math.Min(
                Math.Min(nexttarget.SquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z), nexttarget.SquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Y - 1, entity.ServerPos.Z)),       // One block above is also ok
                nexttarget.SquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Y + 0.5f, entity.ServerPos.Z) // Half a block below is also okay
            );

            bool nearHorizontally = horsqDistToTarget < 1;
            bool nearAllDirs = sqDistToTarget < targetDistance * targetDistance;

            bool nearAllDirsNext = sqDistToNextTarget < targetDistance * targetDistance;


            if (nearAllDirs || nearAllDirsNext)
            {
                waypointToReachIndex++;
                if (nearAllDirsNext && wayPointIndex != nextwayPointIndex)
                {
                    waypointToReachIndex++;
                }

                lastWaypointIncTotalMs = entity.World.ElapsedMilliseconds;

                if (waypointToReachIndex >= waypoints.Count)
                {
                    Stop();
                    OnGoalReached?.Invoke();
                    return;
                } else
                {
                    target = waypoints[waypointToReachIndex];
                }
            }

            bool stuck =
                (entity.CollidedVertically && entity.Controls.IsClimbing) ||
                (entity.CollidedHorizontally && entity.ServerPos.Motion.Y <= 0) ||
                (nearHorizontally && !nearAllDirs && entity.Properties.Habitat == EnumHabitat.Land) ||
                (entity.CollidedHorizontally && waypoints.Count > 1 && waypointToReachIndex < waypoints.Count && entity.World.ElapsedMilliseconds - lastWaypointIncTotalMs > 2000) // If it takes more than 2 seconds to reach next waypoint (waypoints are always 1 block apart)
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

            stuckCounter = stuck ? (stuckCounter + 1) : 0;
            
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

            float desiredYaw = 0;
            
            if (sqDistToTarget >= 0.01)
            {
                desiredYaw = (float)Math.Atan2(targetVec.X, targetVec.Z);
            }



            float yawDist = GameMath.AngleRadDistance(entity.ServerPos.Yaw, desiredYaw);
            entity.ServerPos.Yaw += GameMath.Clamp(yawDist, -curTurnRadPerSec * dt * GlobalConstants.OverallSpeedMultiplier, curTurnRadPerSec * dt * GlobalConstants.OverallSpeedMultiplier);
            entity.ServerPos.Yaw = entity.ServerPos.Yaw % GameMath.TWOPI;

            

            double cosYaw = Math.Cos(entity.ServerPos.Yaw);
            double sinYaw = Math.Sin(entity.ServerPos.Yaw);
            controls.WalkVector.Set(sinYaw, GameMath.Clamp(targetVec.Y, -1, 1), cosYaw);
            controls.WalkVector.Mul(movingSpeed * GlobalConstants.OverallSpeedMultiplier);// * speedMul);

            // Make it walk along the wall, but not walk into the wall, which causes it to climb
            if (entity.Properties.RotateModelOnClimb && entity.Controls.IsClimbing && entity.ClimbingOnFace != null && entity.Alive)
            {
                BlockFacing facing = entity.ClimbingOnFace;
                if (Math.Sign(facing.Normali.X) == Math.Sign(controls.WalkVector.X))
                {
                    controls.WalkVector.X = 0;
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
                Block inblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y), (int)pos.Z);
                Block aboveblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y + 1), (int)pos.Z);
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

                if (entity.CollidedHorizontally)
                {
                    //TODO
                }
            }
            else if (entity.Swimming)
            {
                controls.FlyVector.Set(controls.WalkVector);

                Vec3d pos = entity.Pos.XYZ;
                Block inblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y), (int)pos.Z);
                Block aboveblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y + 1), (int)pos.Z);
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


        public override void Stop()
        {
            Active = false;
            entity.Controls.Forward = false;
            entity.ServerControls.Forward = false;
            entity.Controls.WalkVector.Set(0, 0, 0);
            stuckCounter = 0;
        }
    }
}
