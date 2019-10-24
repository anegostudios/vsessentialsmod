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
        float curTurnRadPerSec;
        Vec3f targetVec = new Vec3f();

        List<Vec3d> waypoints;
        int waypointToReachIndex = 0;
        long lastWaypointIncTotalMs;


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
        }



        public override bool NavigateTo(Vec3d target, float movingSpeed, float targetDistance, API.Common.Action OnGoalReached, API.Common.Action OnStuck, bool giveUpWhenNoPath = false, int searchDepth = 999, bool allowReachAlmost = false)
        {
            waypointToReachIndex = 0;

            var bh = entity.GetBehavior<EntityBehaviorControlledPhysics>();
            float stepHeight = bh == null ? 0.6f : bh.stepHeight;
            bool canFallDamage = entity.Properties.FallDamage;

            waypoints = entity.World.Api.ModLoader.GetModSystem<PathfindSystem>().FindPathAsWaypoints(entity.ServerPos.AsBlockPos, target.AsBlockPos, canFallDamage ? 8 : 4, stepHeight, entity.CollisionBox, searchDepth, allowReachAlmost);
            bool nopath = false;

            if (waypoints == null)
            {
                waypoints = new List<Vec3d>();
                nopath = true;
            } else
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


        public override bool WalkTowards(Vec3d target, float movingSpeed, float targetDistance, API.Common.Action OnGoalReached, API.Common.Action OnStuck)
        {
            waypoints = new List<Vec3d>();
            waypoints.Add(target);

            return base.WalkTowards(target, movingSpeed, targetDistance, OnGoalReached, OnStuck);
        }


        protected override bool BeginGo()
        {
            entity.Controls.Forward = true;
            curTurnRadPerSec = minTurnAnglePerSec + (float)entity.World.Rand.NextDouble() * (maxTurnAnglePerSec - minTurnAnglePerSec);
            curTurnRadPerSec *= GameMath.DEG2RAD * 50 * movingSpeed;

            stuckCounter = 0;

            return true;
        }

        Vec3d prevPos = new Vec3d();

        public override void OnGameTick(float dt)
        {
            if (!Active) return;

            Vec3d target = waypoints[Math.Min(waypoints.Count - 1, waypointToReachIndex)];

            // For land dwellers only check horizontal distance
            double sqDistToTarget = target.SquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            double horsqDistToTarget = target.HorizontalSquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Z);

            bool nearHorizontally = horsqDistToTarget < 1;
            bool nearAllDirs = sqDistToTarget < targetDistance * targetDistance;
            
            //float speedMul = 1;// entity.Properties.Habitat == API.Common.EnumHabitat.Land && waypointToReachIndex >= waypoints.Count - 1 ? Math.Min(1, GameMath.Sqrt(horsqDistToTarget)) : 1;
            //Console.WriteLine(speedMul);

            

            if (nearAllDirs)
            {
                waypointToReachIndex++;
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
                (entity.ServerPos.SquareDistanceTo(prevPos) < 0.005 * 0.005) ||  // This used to test motion, but that makes no sense, we want to test if the entity moved, not if it had motion
                (entity.CollidedHorizontally && entity.ServerPos.Motion.Y <= 0) ||
                (nearHorizontally && !nearAllDirs && entity.Properties.Habitat == API.Common.EnumHabitat.Land) ||
                (waypoints.Count > 1 && waypointToReachIndex < waypoints.Count && entity.World.ElapsedMilliseconds - lastWaypointIncTotalMs > 2000) // If it takes more than 2 seconds to reach next waypoint (waypoints are always 1 block apart)
            ;

            prevPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);

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


            if (entity.Swimming)
            {
                controls.FlyVector.Set(controls.WalkVector);
                controls.FlyVector.Mul(0.7f);
                if (entity.CollidedHorizontally)
                {
                    controls.FlyVector.Y = -0.05f;
                }
            }
        }


        public override void Stop()
        {
            Active = false;
            entity.Controls.Forward = false;
            entity.Controls.WalkVector.Set(0, 0, 0);
            stuckCounter = 0;
        }
    }
}
