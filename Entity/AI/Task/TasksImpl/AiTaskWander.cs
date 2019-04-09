using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class AiTaskWander : AiTaskBase
    {
        public Vec3d MainTarget;

        bool done;
        float moveSpeed = 0.03f;
        float wanderChance = 0.015f;
        float maxHeight = 7f;
        float? preferredLightLevel;
        float targetDistance = 0.12f;

        bool awaitReached = true;

        NatFloat wanderRange = NatFloat.createStrongerInvexp(3, 30);
        BlockPos tmpPos = new BlockPos();

        public bool StayCloseToSpawn;
        public Vec3d SpawnPosition;
        public double MaxDistanceToSpawn;
        public bool TeleportWhenOutOfRange = true;
        public double TeleportInGameHours = 1;

        long lastTimeInRangeMs;

        public AiTaskWander(EntityAgent entity) : base(entity)
        {
        }

        public override void OnEntityLoaded()
        {

        }

        public override void OnEntitySpawn()
        {
            entity.Attributes.SetDouble("spawnX", entity.ServerPos.X);
            entity.Attributes.SetDouble("spawnY", entity.ServerPos.Y);
            entity.Attributes.SetDouble("spawnZ", entity.ServerPos.Z);
            SpawnPosition = entity.ServerPos.XYZ;
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            SpawnPosition = new Vec3d(entity.Attributes.GetDouble("spawnX"), entity.Attributes.GetDouble("spawnY"), entity.Attributes.GetDouble("spawnZ"));

            float wanderRangeMin=3, wanderRangeMax=30;

            if (taskConfig["maxDistanceToSpawn"].Exists)
            {
                StayCloseToSpawn = true;
                MaxDistanceToSpawn = taskConfig["maxDistanceToSpawn"].AsDouble(10);

                TeleportWhenOutOfRange = taskConfig["teleportWhenOutOfRange"].AsBool(true);
                TeleportInGameHours = taskConfig["teleportInGameHours"].AsDouble(1);
            }

            if (taskConfig["targetDistance"] != null)
            {
                targetDistance = taskConfig["targetDistance"].AsFloat(0.12f);
            }

            if (taskConfig["movespeed"] != null)
            {
                moveSpeed = taskConfig["movespeed"].AsFloat(0.03f);
            }

            if (taskConfig["wanderChance"] != null)
            {
                wanderChance = taskConfig["wanderChance"].AsFloat(0.015f);
            }

            if (taskConfig["wanderRangeMin"] != null)
            {
                wanderRangeMin = taskConfig["wanderRangeMin"].AsFloat(3);
            }
            if (taskConfig["wanderRangeMax"] != null)
            {
                wanderRangeMax = taskConfig["wanderRangeMax"].AsFloat(30);
            }
            wanderRange = NatFloat.createStrongerInvexp(wanderRangeMin, wanderRangeMax);


            if (taskConfig["maxHeight"] != null)
            {
                maxHeight = taskConfig["maxHeight"].AsFloat(7f);
            }

            if (taskConfig["preferredLightLevel"] != null)
            {
                preferredLightLevel = taskConfig["preferredLightLevel"].AsFloat(-99);
                if (preferredLightLevel < 0) preferredLightLevel = null;
            }

            if (taskConfig["awaitReached"] != null)
            {
                awaitReached = taskConfig["awaitReached"].AsBool(true);
            }
            
        }

        public override bool ShouldExecute()
        {
            if (rand.NextDouble() > wanderChance) return false;            

            double dist = entity.ServerPos.XYZ.SquareDistanceTo(SpawnPosition);
            if (StayCloseToSpawn)
            {
                long ellapsedMs = entity.World.ElapsedMilliseconds;

                if (dist > MaxDistanceToSpawn * MaxDistanceToSpawn)
                {
                    // If after 2 minutes still not at spawn and no player nearby, teleport
                    if (ellapsedMs - lastTimeInRangeMs > 1000 * 60 * 2 && entity.World.GetNearestEntity(entity.ServerPos.XYZ, 15, 15, (e) => e is EntityPlayer) == null)
                    {
                        entity.TeleportTo(SpawnPosition);
                    }

                    MainTarget = SpawnPosition.Clone();
                    return true;
                } else
                {
                    lastTimeInRangeMs = ellapsedMs;
                }
            }
            

            List <Vec3d> goodtargets = new List<Vec3d>();

            int tries = 9;
            while (tries-- > 0)
            {
                int terrainYPos = entity.World.BlockAccessor.GetTerrainMapheightAt(tmpPos);

                float dx = wanderRange.nextFloat() * (rand.Next(2) * 2 - 1);
                float dy = wanderRange.nextFloat() * (rand.Next(2) * 2 - 1);
                float dz = wanderRange.nextFloat() * (rand.Next(2) * 2 - 1);

                MainTarget = entity.ServerPos.XYZ.Add(dx, dy, dz);
                MainTarget.Y = Math.Min(MainTarget.Y, terrainYPos + maxHeight);

                if (StayCloseToSpawn && MainTarget.SquareDistanceTo(SpawnPosition) > MaxDistanceToSpawn * MaxDistanceToSpawn)
                {
                    continue;
                }

                tmpPos.X = (int)MainTarget.X;
                tmpPos.Z = (int)MainTarget.Z;
                
                if ((entity.Controls.IsClimbing && !entity.Properties.FallDamage) || (entity.Properties.Habitat != EnumHabitat.Land))
                {
                    if (entity.Properties.Habitat == EnumHabitat.Sea)
                    {
                        Block block = entity.World.BlockAccessor.GetBlock(tmpPos);
                        return block.IsLiquid();
                    }

                    return true;
                }
                else
                {
                    int yDiff = (int)entity.ServerPos.Y - terrainYPos;

                    double slopeness = yDiff / Math.Max(1, GameMath.Sqrt(MainTarget.HorizontalSquareDistanceTo(entity.ServerPos.XYZ)) - 2);

                    tmpPos.Y = terrainYPos;
                    Block block = entity.World.BlockAccessor.GetBlock(tmpPos);
                    Block belowblock = entity.World.BlockAccessor.GetBlock(tmpPos.X, tmpPos.Y - 1, tmpPos.Z);

                    bool canStep = block.CollisionBoxes == null || block.CollisionBoxes.Max((cuboid) => cuboid.Y2) <= 1f;
                    bool canStand = belowblock.CollisionBoxes != null && belowblock.CollisionBoxes.Length > 0;

                    if (slopeness < 3 && canStand && canStep)
                    {
                        if (preferredLightLevel == null) return true;
                        goodtargets.Add(MainTarget);
                    }
                }
            }

            int smallestdiff = 999;
            Vec3d bestTarget = null;
            for (int i = 0; i < goodtargets.Count; i++)
            {
                int lightdiff = Math.Abs((int)preferredLightLevel - entity.World.BlockAccessor.GetLightLevel(goodtargets[i].AsBlockPos, EnumLightLevelType.MaxLight));

                if (lightdiff < smallestdiff)
                {
                    smallestdiff = lightdiff;
                    bestTarget = goodtargets[i];
                }
            }

            if (bestTarget != null)
            {
                MainTarget = bestTarget;
                return true;
            }

            return false;
        }


        public override void StartExecute()
        {
            base.StartExecute();

            done = false;
            pathTraverser.GoTo(MainTarget, moveSpeed, targetDistance, OnGoalReached, OnStuck);
        }

        public override bool ContinueExecute(float dt)
        {
  //          if (!awaitReached) return false;

            /*entity.World.SpawnParticles(
                1, 
                ColorUtil.WhiteArgb, 
                MainTarget.AddCopy(new Vec3f(-0.1f, -0.1f, -0.1f)), 
                MainTarget.AddCopy(new Vec3f(0.1f, 0.1f, 0.1f)), new Vec3f(), new Vec3f(), 1f, 0f
            );*/

            // If we are a climber dude and encountered a wall, let's not try to get behind the wall
            // We do that by removing the coord component that would make the entity want to walk behind the wall
            if (entity.Controls.IsClimbing && entity.Properties.CanClimbAnywhere && entity.ClimbingOnFace != null)
            {
                BlockFacing facing = entity.ClimbingOnFace;

                if (Math.Sign(facing.Normali.X) == Math.Sign(pathTraverser.CurrentTarget.X - entity.ServerPos.X))
                {
                    pathTraverser.CurrentTarget.X = entity.ServerPos.X;
                }

                if (Math.Sign(facing.Normali.Z) == Math.Sign(pathTraverser.CurrentTarget.Z - entity.ServerPos.Z))
                {
                    pathTraverser.CurrentTarget.Z = entity.ServerPos.Z;
                }
            }

            return !done;
        }

        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);

            if (cancelled)
            {
                pathTraverser.Stop();
            }
        }

        private void OnStuck()
        {
            done = true;
        }

        private void OnGoalReached()
        {
            done = true;
        }
    }
}
