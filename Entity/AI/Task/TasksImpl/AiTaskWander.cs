using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.GameContent
{
    public class AiTaskWander : AiTaskBase
    {
        public Vec3d MainTarget;

        bool done;
        float moveSpeed = 0.03f;
        float wanderChance = 0.02f;
        float maxHeight = 7f;
        float? preferredLightLevel;
        float targetDistance = 0.12f;

        NatFloat wanderRangeHorizontal = NatFloat.createStrongerInvexp(3, 40);
        NatFloat wanderRangeVertical = NatFloat.createStrongerInvexp(3, 10);

        public bool StayCloseToSpawn;
        public Vec3d SpawnPosition;
        public double MaxDistanceToSpawn;

        long lastTimeInRangeMs;
        int failedWanders;

        public float WanderRangeMul
        {
            get { return entity.Attributes.GetFloat("wanderRangeMul", 1); }
            set { entity.Attributes.SetFloat("wanderRangeMul", value); }
        }

        public int FailedConsecutivePathfinds
        {
            get { return entity.Attributes.GetInt("failedConsecutivePathfinds", 0); }
            set { entity.Attributes.SetInt("failedConsecutivePathfinds", value); }
        }


        public AiTaskWander(EntityAgent entity) : base(entity)
        {
        }

        public override void OnEntityLoaded()
        {
            if (SpawnPosition == null && !entity.Attributes.HasAttribute("spawnX"))
            {
                OnEntitySpawn();
            }
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
            }

            targetDistance = taskConfig["targetDistance"].AsFloat(0.12f);
            moveSpeed = taskConfig["movespeed"].AsFloat(0.03f);
            wanderChance = taskConfig["wanderChance"].AsFloat(0.02f);
            wanderRangeMin = taskConfig["wanderRangeMin"].AsFloat(3);
            wanderRangeMax = taskConfig["wanderRangeMax"].AsFloat(30);
            wanderRangeHorizontal = NatFloat.createInvexp(wanderRangeMin, wanderRangeMax);
            maxHeight = taskConfig["maxHeight"].AsFloat(7f);

            preferredLightLevel = taskConfig["preferredLightLevel"].AsFloat(-99);
            if (preferredLightLevel < 0) preferredLightLevel = null;
        }


        // Requirements:
        // - ✔ Try to not move a lot vertically
        // - ✔ If territorial: Stay close to the spawn point
        // - ✔ If air habitat: Don't go above maxHeight blocks above surface
        // - ✔ If land habitat: Don't walk into water, prefer surface
        // - ~~If cave habitat: Prefer caves~~
        // - ✔ If water habitat: Don't walk onto land
        // - ✔ Try not to fall from very large heights. Try not to fall from any large heights if entity has FallDamage
        // - ✔ Prefer preferredLightLevel
        // - ✔ If land habitat: Must be above a block the entity can stand on
        // - ✔ if failed searches is high, reduce wander range
        public Vec3d loadNextWanderTarget()
        {
            EnumHabitat habitat = entity.Properties.Habitat;
            bool canFallDamage = entity.Properties.FallDamage;
            bool territorial = StayCloseToSpawn;
            int tries = 9;
            Vec4d bestTarget = null;
            Vec4d curTarget = new Vec4d();
            BlockPos tmpPos = new BlockPos();

            if (FailedConsecutivePathfinds > 10)
            {
                WanderRangeMul = Math.Max(0.1f, WanderRangeMul * 0.9f);
            } else
            {
                WanderRangeMul = Math.Min(1, WanderRangeMul * 1.1f);
                if (rand.NextDouble() < 0.05) WanderRangeMul = Math.Min(1, WanderRangeMul * 1.5f);
            }

            float wRangeMul = WanderRangeMul;
            double dx, dy, dz;

            if (rand.NextDouble() < 0.05) wRangeMul *= 3;

            while (tries-- > 0)
            {
                dx = wanderRangeHorizontal.nextFloat() * (rand.Next(2) * 2 - 1) * wRangeMul;
                dy = wanderRangeVertical.nextFloat() * (rand.Next(2) * 2 - 1) * wRangeMul;
                dz = wanderRangeHorizontal.nextFloat() * (rand.Next(2) * 2 - 1) * wRangeMul;

                curTarget.X = entity.ServerPos.X + dx;
                curTarget.Y = entity.ServerPos.Y + dy;
                curTarget.Z = entity.ServerPos.Z + dz;
                curTarget.W = 1;

                if (StayCloseToSpawn)
                {
                    double distToEdge = curTarget.SquareDistanceTo(SpawnPosition) / (MaxDistanceToSpawn * MaxDistanceToSpawn);
                    // Prefer staying close to spawn
                    curTarget.W = 1 - distToEdge;
                }

                Block waterorIceBlock;


                switch (habitat)
                {
                    case EnumHabitat.Air:
                        int rainMapY = world.BlockAccessor.GetRainMapHeightAt((int)curTarget.X, (int)curTarget.Z);
                        // Don't fly above max height
                        curTarget.Y = Math.Min(curTarget.Y, rainMapY + maxHeight);

                        // Cannot be in water
                        waterorIceBlock = entity.World.BlockAccessor.GetBlock((int)curTarget.X, (int)curTarget.Y, (int)curTarget.Z, BlockLayersAccess.Fluid);
                        if (waterorIceBlock.IsLiquid()) curTarget.W = 0;
                        break;

                    case EnumHabitat.Land:
                        curTarget.Y = moveDownToFloor((int)curTarget.X, curTarget.Y, (int)curTarget.Z);
                        // No floor found
                        if (curTarget.Y < 0) curTarget.W = 0;
                        else
                        {
                            // Does not like water
                            waterorIceBlock = entity.World.BlockAccessor.GetBlock((int)curTarget.X, (int)curTarget.Y, (int)curTarget.Z, BlockLayersAccess.Fluid);
                            if (waterorIceBlock.IsLiquid()) curTarget.W /= 2;

                            // Lets make a straight line plot to see if we would fall off a cliff
                            bool stop = false;
                            bool willFall = false;

                            float angleHor = (float)Math.Atan2(dx, dz) + GameMath.PIHALF;
                            Vec3d target1BlockAhead = curTarget.XYZ.Ahead(1, 0, angleHor);
                            Vec3d startAhead = entity.ServerPos.XYZ.Ahead(1, 0, angleHor); // Otherwise they are forever stuck if they stand over the edge

                            int prevY = (int)startAhead.Y;

                            GameMath.BresenHamPlotLine2d((int)startAhead.X, (int)startAhead.Z, (int)target1BlockAhead.X, (int)target1BlockAhead.Z, (x, z) =>
                            {
                                if (stop) return;

                                double nowY = moveDownToFloor(x, prevY, z);

                                // Not more than 4 blocks down
                                if (nowY < 0 || prevY - nowY > 4)
                                {
                                    willFall = true;
                                    stop = true;
                                }

                                // Not more than 2 blocks up
                                if (nowY - prevY > 2)
                                {
                                    stop = true;
                                }

                                prevY = (int)nowY;
                            });

                            if (willFall) curTarget.W = 0;
                            
                        }
                        break;

                    case EnumHabitat.Sea:
                        waterorIceBlock = entity.World.BlockAccessor.GetBlock((int)curTarget.X, (int)curTarget.Y, (int)curTarget.Z, BlockLayersAccess.Fluid);
                        if (!waterorIceBlock.IsLiquid()) curTarget.W = 0;
                        break;

                    case EnumHabitat.Underwater:
                        waterorIceBlock = entity.World.BlockAccessor.GetBlock((int)curTarget.X, (int)curTarget.Y, (int)curTarget.Z, BlockLayersAccess.Fluid);
                        if (!waterorIceBlock.IsLiquid()) curTarget.W = 0;
                        else curTarget.W = 1 / (Math.Abs(dy) + 1);  //prefer not too much vertical change when underwater

                        //TODO: reject (or de-weight) targets not in direct line of sight (avoiding terrain)

                        break;
                }

                if (curTarget.W > 0)
                {
                    // Try to not hug the wall so much
                    for (int i = 0; i < BlockFacing.HORIZONTALS.Length; i++)
                    {
                        BlockFacing face = BlockFacing.HORIZONTALS[i];
                        if (entity.World.BlockAccessor.IsSideSolid((int)curTarget.X + face.Normali.X, (int)curTarget.Y, (int)curTarget.Z + face.Normali.Z, face.Opposite))
                        {
                            curTarget.W *= 0.5;
                        }
                    }
                }


                if (preferredLightLevel != null && curTarget.W != 0)
                {
                    tmpPos.Set((int)curTarget.X, (int)curTarget.Y, (int)curTarget.Z);
                    int lightdiff = Math.Abs((int)preferredLightLevel - entity.World.BlockAccessor.GetLightLevel(tmpPos, EnumLightLevelType.MaxLight));

                    curTarget.W /= Math.Max(1, lightdiff);
                }

                if (bestTarget == null || curTarget.W > bestTarget.W)
                {
                    bestTarget = new Vec4d(curTarget.X, curTarget.Y, curTarget.Z, curTarget.W);
                    if (curTarget.W >= 1.0) break;  //have a good enough target, no need for further tries
                }
            }


            if (bestTarget.W > 0)
            {
                //double bla = bestTarget.Y;
                //bestTarget.Y += 1;
                //dx = bestTarget.X - entity.ServerPos.X;
                //dz = bestTarget.Z - entity.ServerPos.Z;
                //Vec3d sadf = bestTarget.XYZ.Ahead(1, 0, (float)Math.Atan2(dx, dz) + GameMath.PIHALF);

                /*(entity.Api as ICoreServerAPI).World.HighlightBlocks(world.AllOnlinePlayers[0], 10, new List<BlockPos>() {
                new BlockPos((int)bestTarget.X, (int)bestTarget.Y, (int)bestTarget.Z) }, new List<int>() { ColorUtil.ColorFromRgba(0, 255, 0, 80) }, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
                (entity.Api as ICoreServerAPI).World.HighlightBlocks(world.AllOnlinePlayers[0], 11, new List<BlockPos>() {
                new BlockPos((int)sadf.X, (int)sadf.Y, (int)sadf.Z) }, new List<int>() { ColorUtil.ColorFromRgba(0, 255, 255, 180) }, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);*/

                //bestTarget.Y = bla;


                FailedConsecutivePathfinds = Math.Max(FailedConsecutivePathfinds - 3, 0);
                return bestTarget.XYZ;
            }

            FailedConsecutivePathfinds++;
            return null;
        }

        private double moveDownToFloor(int x, double y, int z)
        {
            int tries = 5;
            while (tries-- > 0)
            {
                if (world.BlockAccessor.IsSideSolid(x, (int)y, z, BlockFacing.UP)) return y + 1;
                y--;
            }

            return -1;
        }


        bool needsToTele = false;
        public override bool ShouldExecute()
        {
            if (rand.NextDouble() > (failedWanders > 0 ? (1 - wanderChance * 4 * failedWanders) : wanderChance))    // if a wander failed (got stuck) initially greatly increase the chance of trying again, but eventually give up
            {
                failedWanders = 0;
                return false;
            }

            needsToTele = false;

            double dist = entity.ServerPos.XYZ.SquareDistanceTo(SpawnPosition);
            if (StayCloseToSpawn)
            {
                long ellapsedMs = entity.World.ElapsedMilliseconds;

                if (dist > MaxDistanceToSpawn * MaxDistanceToSpawn)
                {
                    // If after 2 minutes still not at spawn and no player nearby, teleport
                    if (ellapsedMs - lastTimeInRangeMs > 1000 * 60 * 2 && entity.World.GetNearestEntity(entity.ServerPos.XYZ, 15, 15, (e) => e is EntityPlayer) == null)
                    {
                        needsToTele = true;
                    }

                    MainTarget = SpawnPosition.Clone();
                    return true;
                } else
                {
                    lastTimeInRangeMs = ellapsedMs;
                }
            }

            MainTarget = loadNextWanderTarget();
            return MainTarget != null;
        }


        public override void StartExecute()
        {
            base.StartExecute();

            if (needsToTele)
            {
                entity.TeleportTo(SpawnPosition);
                done = true;
                return;
            }

            done = false;
            pathTraverser.WalkTowards(MainTarget, moveSpeed, targetDistance, OnGoalReached, OnStuck);

            tryStartAnimAgain = 0.1f;
        }

        float tryStartAnimAgain = 0.1f;

        public override bool 
            ContinueExecute(float dt)
        {

            base.ContinueExecute(dt);
            
            //Check if time is still valid for task.
            if (!IsInValidDayTimeHours(false)) return false;

            // We have a bug with the animation sync server->client where the wander right after spawn is not synced. this is a workaround
            if (animMeta != null && tryStartAnimAgain > 0 && (tryStartAnimAgain -= dt) <= 0) 
            {
                entity.AnimManager.StartAnimation(animMeta);
            }

            /*entity.World.SpawnParticles(
                1, 
                ColorUtil.WhiteArgb, 
                MainTarget.AddCopy(new Vec3f(-0.1f, -0.1f, -0.1f)), 
                MainTarget.AddCopy(new Vec3f(0.1f, 0.1f, 0.1f)), new Vec3f(), new Vec3f(), 1f, 0f
            );*/

            // If we are a climber dude and encountered a wall, let's not try to get behind the wall
            // We do that by removing the coord component that would make the entity want to walk behind the wall
            if (entity.Controls.IsClimbing && entity.Properties.CanClimbAnywhere && entity.ClimbingIntoFace != null)
            {
                BlockFacing facing = entity.ClimbingIntoFace; // ?? entity.ClimbingOnFace;

                if (Math.Sign(facing.Normali.X) == Math.Sign(pathTraverser.CurrentTarget.X - entity.ServerPos.X))
                {
                    pathTraverser.CurrentTarget.X = entity.ServerPos.X;
                }

                if (Math.Sign(facing.Normali.Y) == Math.Sign(pathTraverser.CurrentTarget.Y - entity.ServerPos.Y))
                {
                    pathTraverser.CurrentTarget.Y = entity.ServerPos.Y;
                }

                if (Math.Sign(facing.Normali.Z) == Math.Sign(pathTraverser.CurrentTarget.Z - entity.ServerPos.Z))
                {
                    pathTraverser.CurrentTarget.Z = entity.ServerPos.Z;
                }
            }

            if (MainTarget.HorizontalSquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Z) < 0.5)
            {
                pathTraverser.Stop();
                return false;
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
            failedWanders++;
        }

        private void OnGoalReached()
        {
            done = true;
            failedWanders = 0;
        }

        
    }
}
