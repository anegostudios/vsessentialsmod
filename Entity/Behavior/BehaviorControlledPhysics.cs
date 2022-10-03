using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorControlledPhysics : EntityBehavior, IRenderer
    {
        protected float accumulator = 0;
        protected Vec3d outposition = new Vec3d(); // Temporary field
        protected CachingCollisionTester collisionTester = new CachingCollisionTester();

        internal List<EntityLocomotion> Locomotors = new List<EntityLocomotion>();

        public float stepHeight = 0.6f;

        protected Cuboidf sneakTestCollisionbox = new Cuboidf();
        protected Vec3d prevPos = new Vec3d();

        protected bool duringRenderFrame;
        public double RenderOrder => 0;
        public int RenderRange => 9999;

        ICoreClientAPI capi;

        protected bool smoothStepping;

        protected float accum1s;
        protected Vec3d windForce = new Vec3d();

        bool applyWindForce;

        public override void OnEntityDespawn(EntityDespawnReason despawn)
        {
            (entity.World.Api as ICoreClientAPI)?.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            Dispose();
        }

        public EntityBehaviorControlledPhysics(Entity entity) : base(entity)
        {
            Locomotors.Add(new EntityOnGround());
            Locomotors.Add(new EntityInLiquid());
            Locomotors.Add(new EntityInAir());
            Locomotors.Add(new EntityApplyGravity());
            Locomotors.Add(new EntityMotionDrag());
        }


        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            stepHeight = typeAttributes["stepHeight"].AsFloat(0.6f);

            JsonObject physics = properties?.Attributes?["physics"];
            for (int i = 0; i < Locomotors.Count; i++)
            {
                Locomotors[i].Initialize(physics);
            }

            sneakTestCollisionbox = entity.CollisionBox.Clone().OmniNotDownGrowBy(-0.1f);
            sneakTestCollisionbox.Y2 /= 2; // We really don't need to test anthing at the upper half of the creature (this also fixes sneak fall down when sneaking under a half slab)

            if (entity.World.Side == EnumAppSide.Client)
            {
                capi = entity.World.Api as ICoreClientAPI;
                duringRenderFrame = true;
                capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "controlledphysics");
            }

            accumulator = (float)entity.World.Rand.NextDouble() * GlobalConstants.PhysicsFrameTime;

            applyWindForce = entity.World.Config.GetBool("windAffectedEntityMovement", false);
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (capi.IsGamePaused) return;

            onPhysicsTick(deltaTime);
        }


        public override void OnGameTick(float deltaTime)
        {
            if (!duringRenderFrame)
            {
                onPhysicsTick(deltaTime);
            }
        }

        public virtual void onPhysicsTick(float deltaTime)
        {
            if (entity.State == EnumEntityState.Inactive)
            {
                return;
            }

            entity.World.FrameProfiler.Enter("controlledphysics");

            accum1s += deltaTime;
            if (accum1s > 1.5f)
            {
                accum1s = 0;
                updateWindForce();
            }

            accumulator += deltaTime;

            if (accumulator > GlobalConstants.MaxPhysicsIntervalInSlowTicks)
            {
                accumulator = GlobalConstants.MaxPhysicsIntervalInSlowTicks;
            }


            collisionTester.NewTick();
            while (accumulator >= GlobalConstants.PhysicsFrameTime)
            {
                TickEntityPhysicsPre(entity, GlobalConstants.PhysicsFrameTime);
                accumulator -= GlobalConstants.PhysicsFrameTime;
            }

            entity.PhysicsUpdateWatcher?.Invoke(accumulator, prevPos);

            entity.World.FrameProfiler.Leave();
        }

        protected virtual void updateWindForce()
        {
            if (!entity.Alive || !applyWindForce)
            {
                windForce.Set(0, 0, 0);
                return;
            }

            int rainy = entity.World.BlockAccessor.GetRainMapHeightAt((int)entity.Pos.X, (int)entity.Pos.Z);
            if (rainy > entity.Pos.Y)
            {
                windForce.Set(0, 0, 0);
                return;
            }

            Vec3d windSpeed = entity.World.BlockAccessor.GetWindSpeedAt(entity.Pos.XYZ);
            windForce.X = Math.Max(0, Math.Abs(windSpeed.X) - 0.8) / 40f * Math.Sign(windSpeed.X);
            windForce.Y = Math.Max(0, Math.Abs(windSpeed.Y) - 0.8) / 40f * Math.Sign(windSpeed.Y);
            windForce.Z = Math.Max(0, Math.Abs(windSpeed.Z) - 0.8) / 40f * Math.Sign(windSpeed.Z);
        }

        public virtual void TickEntityPhysicsPre(Entity entity, float dt)
        {
            prevPos.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            EntityControls controls = ((EntityAgent)entity).Controls;
            TickEntityPhysics(entity.ServerPos, controls, dt);  // this was entity.ServerPos - wtf? - apparently needed so they don't glitch through terrain o.O

            if (entity.World.Side == EnumAppSide.Server)
            {
                entity.Pos.SetFrom(entity.ServerPos);
            }
        }




        public void TickEntityPhysics(EntityPos pos, EntityControls controls, float dt)
        {
            FrameProfilerUtil profiler = entity.World.FrameProfiler;
            profiler.Mark("init");
            float dtFac = 60 * dt;

            // This seems to make creatures clip into the terrain. Le sigh. 
            // Since we currently only really need it for when the creature is dead, let's just use it only there

            // Also running animations for all nearby living entities is pretty CPU intensive so
            // the AnimationManager also just ticks if the entity is in view or dead
            if (!entity.Alive)
            {
                AdjustCollisionBoxToAnimation(dtFac);
            }


            if (controls.TriesToMove && entity.OnGround && !entity.Swimming)
            {
                pos.Motion.Add(windForce);
            }


            foreach (EntityLocomotion locomotor in Locomotors)
            {
                if (locomotor.Applicable(entity, pos, controls))
                {
                    locomotor.Apply(dt, entity, pos, controls);
                }
            }

            profiler.Mark("locomotors");

            int knockbackState = entity.Attributes.GetInt("dmgkb");
            if (knockbackState > 0)
            {
                float acc = entity.Attributes.GetFloat("dmgkbAccum") + dt;
                entity.Attributes.SetFloat("dmgkbAccum", acc);

                if (knockbackState == 1)
                {
                    float str = 1 * 30 * dt * (entity.OnGround ? 1 : 0.5f);
                    pos.Motion.X += entity.WatchedAttributes.GetDouble("kbdirX") * str;
                    pos.Motion.Y += entity.WatchedAttributes.GetDouble("kbdirY") * str;
                    pos.Motion.Z += entity.WatchedAttributes.GetDouble("kbdirZ") * str;
                    entity.Attributes.SetInt("dmgkb", 2);
                }

                if (acc > 2 / 30f)
                {
                    entity.Attributes.SetInt("dmgkb", 0);
                    entity.Attributes.SetFloat("dmgkbAccum", 0);
                    float str = 0.5f * 30 * dt;
                    pos.Motion.X -= entity.WatchedAttributes.GetDouble("kbdirX") * str;
                    pos.Motion.Y -= entity.WatchedAttributes.GetDouble("kbdirY") * str;
                    pos.Motion.Z -= entity.WatchedAttributes.GetDouble("kbdirZ") * str;
                }
            }

            EntityAgent agent = entity as EntityAgent;
            if (agent?.MountedOn != null)
            {
                pos.SetFrom(agent.MountedOn.MountPosition);
                pos.Motion.X = 0;
                pos.Motion.Y = 0;
                pos.Motion.Z = 0;
                return;
            }

            profiler.Mark("knockback-and-mountedcheck");
            profiler.Enter("collision");

            if (pos.Motion.LengthSq() > 100D)
            {
                pos.Motion.X = GameMath.Clamp(pos.Motion.X, -10, 10);
                pos.Motion.Y = GameMath.Clamp(pos.Motion.Y, -10, 10);
                pos.Motion.Z = GameMath.Clamp(pos.Motion.Z, -10, 10);
            }

            if (!controls.NoClip)
            {
                DisplaceWithBlockCollision(pos, controls, dt);
            }
            else
            {
                pos.X += pos.Motion.X * dt * 60f;
                pos.Y += pos.Motion.Y * dt * 60f;
                pos.Z += pos.Motion.Z * dt * 60f;

                entity.Swimming = false;
                entity.FeetInLiquid = false;
                entity.OnGround = false;
            }


            profiler.Leave();


            // Shake the player violently when falling at high speeds
            /*if (movedy < -50)
            {
                pos.X += (rand.NextDouble() - 0.5) / 5 * (-movedy / 50f);
                pos.Z += (rand.NextDouble() - 0.5) / 5 * (-movedy / 50f);
            }
            */

            //return result;
        }



        Vec3d nextPosition = new Vec3d();
        Vec3d moveDelta = new Vec3d();
        BlockPos tmpPos = new BlockPos();
        Cuboidd entityBox = new Cuboidd();

        public void DisplaceWithBlockCollision(EntityPos pos, EntityControls controls, float dt)
        {
            IBlockAccessor blockAccess = entity.World.BlockAccessor;
            FrameProfilerUtil profiler = entity.World.FrameProfiler;
            float dtFac = 60 * dt;
            double prevYMotion = pos.Motion.Y;

            moveDelta.Set(pos.Motion.X * dtFac, prevYMotion * dtFac, pos.Motion.Z * dtFac);

            nextPosition.Set(pos.X + moveDelta.X, pos.Y + moveDelta.Y, pos.Z + moveDelta.Z);
            bool falling = prevYMotion < 0;
            bool feetInLiquidBefore = entity.FeetInLiquid;
            bool onGroundBefore = entity.OnGround;
            bool swimmingBefore = entity.Swimming;

            controls.IsClimbing = false;
            entity.ClimbingOnFace = null;
            entity.ClimbingIntoFace = null;


            if (/*!onGroundBefore &&*/ entity.Properties.CanClimb == true)
            {
                int height = (int)Math.Ceiling(entity.CollisionBox.Y2);

                entityBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);

                for (int dy = 0; dy < height; dy++)
                {
                    tmpPos.Set((int)pos.X, (int)pos.Y + dy, (int)pos.Z);
                    Block nblock = blockAccess.GetBlock(tmpPos);
                    if (!nblock.Climbable && !entity.Properties.CanClimbAnywhere) continue;

                    Cuboidf[] collBoxes = nblock.GetCollisionBoxes(blockAccess, tmpPos);
                    if (collBoxes == null) continue;

                    for (int i = 0; i < collBoxes.Length; i++)
                    {
                        double dist = entityBox.ShortestDistanceFrom(collBoxes[i], tmpPos);
                        controls.IsClimbing |= dist < entity.Properties.ClimbTouchDistance;

                        if (controls.IsClimbing)
                        {
                            entity.ClimbingOnFace = null;
                            break;
                        }
                    }
                }

                if (controls.WalkVector.LengthSq() > 0.00001 && entity.Properties.CanClimbAnywhere && entity.Alive)
                {
                    var walkIntoFace = BlockFacing.FromVector(controls.WalkVector.X, controls.WalkVector.Y, controls.WalkVector.Z);
                    if (walkIntoFace != null)
                    {
                        tmpPos.Set((int)pos.X + walkIntoFace.Normali.X, (int)pos.Y + walkIntoFace.Normali.Y, (int)pos.Z + walkIntoFace.Normali.Z);
                        Block nblock = blockAccess.GetBlock(tmpPos);

                        Cuboidf[] icollBoxes = nblock.GetCollisionBoxes(blockAccess, tmpPos);
                        entity.ClimbingIntoFace = (icollBoxes != null && icollBoxes.Length != 0) ? walkIntoFace : null;
                    }
                }

                for (int i = 0; !controls.IsClimbing && i < BlockFacing.HORIZONTALS.Length; i++)
                {
                    BlockFacing facing = BlockFacing.HORIZONTALS[i];
                    for (int dy = 0; dy < height; dy++)
                    {
                        tmpPos.Set((int)pos.X + facing.Normali.X, (int)pos.Y + dy, (int)pos.Z + facing.Normali.Z);
                        Block nblock = blockAccess.GetBlock(tmpPos);
                        if (!nblock.Climbable && !(entity.Properties.CanClimbAnywhere && entity.Alive)) continue;

                        Cuboidf[] collBoxes = nblock.GetCollisionBoxes(blockAccess, tmpPos);
                        if (collBoxes == null) continue;

                        for (int j = 0; j < collBoxes.Length; j++)
                        {
                            double dist = entityBox.ShortestDistanceFrom(collBoxes[j], tmpPos);
                            controls.IsClimbing |= dist < entity.Properties.ClimbTouchDistance;

                            if (controls.IsClimbing)
                            {
                                entity.ClimbingOnFace = facing;
                                entity.ClimbingOnCollBox = collBoxes[j];
                                break;
                            }
                        }
                    }
                }
            }


            if (controls.IsClimbing)
            {
                if (controls.WalkVector.Y == 0)
                {
                    pos.Motion.Y = controls.Sneak ? Math.Max(-0.07, pos.Motion.Y - 0.07) : pos.Motion.Y;
                    if (controls.Jump) pos.Motion.Y = 0.035 * dt * 60f;
                }


                // what was this for? it causes jitter
                // moveDelta.Y = pos.Motion.Y * dt * 60f;
                // nextPosition.Set(pos.X + moveDelta.X, pos.Y + moveDelta.Y, pos.Z + moveDelta.Z);
            }

            profiler.Mark("prep");

            collisionTester.ApplyTerrainCollision(entity, pos, dtFac, ref outposition, stepHeight);

            profiler.Mark("terraincollision");

            if (!entity.Properties.CanClimbAnywhere)
            {
                if (smoothStepping)
                {
                    controls.IsStepping = HandleSteppingOnBlocksSmooth(pos, moveDelta, dtFac, controls);
                }
                else
                {
                    controls.IsStepping = HandleSteppingOnBlocks(pos, moveDelta, dtFac, controls);
                }
            }

            profiler.Mark("stepping-checks");

            HandleSneaking(pos, controls, dt);

            if (entity.CollidedHorizontally && !controls.IsClimbing && !controls.IsStepping && entity.Properties.Habitat != EnumHabitat.Underwater)
            {
                if (blockAccess.GetBlock((int)pos.X, (int)(pos.Y + 0.5), (int)pos.Z).LiquidLevel >= 7 || blockAccess.GetBlock((int)pos.X, (int)(pos.Y), (int)pos.Z).LiquidLevel >= 7 || (blockAccess.GetBlock((int)pos.X, (int)(pos.Y - 0.05), (int)pos.Z).LiquidLevel >= 7))
                {
                    pos.Motion.Y += 0.2 * dt;
                    controls.IsStepping = true;
                }
                else   // attempt to prevent endless collisions
                {
                    double absX = Math.Abs(pos.Motion.X);
                    double absZ = Math.Abs(pos.Motion.Z);
                    if (absX > absZ)
                    {
                        if (absZ < 0.001) pos.Motion.Z += pos.Motion.Z < 0 ? -0.0025 : 0.0025;
                    }
                    else
                    {
                        if (absX < 0.001) pos.Motion.X += pos.Motion.X < 0 ? -0.0025 : 0.0025;
                    }
                }
            }


            if (outposition.X != pos.X && blockAccess.IsNotTraversable((pos.X + pos.Motion.X * dt * 60f), pos.Y, pos.Z))
            {
                outposition.X = pos.X;
            }
            if (outposition.Y != pos.Y && blockAccess.IsNotTraversable(pos.X, (pos.Y + pos.Motion.Y * dt * 60f), pos.Z))
            {
                outposition.Y = pos.Y;
            }
            if (outposition.Z != pos.Z && blockAccess.IsNotTraversable(pos.X, pos.Y, (pos.Z + pos.Motion.Z * dt * 60f)))
            {
                outposition.Z = pos.Z;
            }

            pos.SetPos(outposition);

            profiler.Mark("apply-motion");

            // Set the motion to zero if he collided.

            if ((nextPosition.X < outposition.X && pos.Motion.X < 0) || (nextPosition.X > outposition.X && pos.Motion.X > 0))
            {
                pos.Motion.X = 0;
            }

            if ((nextPosition.Y < outposition.Y && pos.Motion.Y < 0) || (nextPosition.Y > outposition.Y && pos.Motion.Y > 0))
            {
                pos.Motion.Y = 0;
            }

            if ((nextPosition.Z < outposition.Z && pos.Motion.Z < 0) || (nextPosition.Z > outposition.Z && pos.Motion.Z > 0))
            {
                pos.Motion.Z = 0;
            }

            float offX = entity.CollisionBox.X2 - entity.OriginCollisionBox.X2;
            float offZ = entity.CollisionBox.Z2 - entity.OriginCollisionBox.Z2;

            int posX = (int)(pos.X + offX);
            int posZ = (int)(pos.Z + offZ);

            Block block = blockAccess.GetBlock(posX, (int)(pos.Y), posZ);
            Block waterOrIce = blockAccess.GetBlock(posX, (int)(pos.Y), posZ, BlockLayersAccess.Fluid);
            Block middleWOIBlock = blockAccess.GetBlock(posX, (int)(pos.Y + entity.SwimmingOffsetY), posZ, BlockLayersAccess.Fluid);

            entity.OnGround = (entity.CollidedVertically && falling && !controls.IsClimbing) || controls.IsStepping;
            entity.FeetInLiquid = false;
            if (waterOrIce.IsLiquid())
            {
                Block aboveblock = blockAccess.GetBlock(posX, (int)(pos.Y + 1), posZ, BlockLayersAccess.Fluid);
                entity.FeetInLiquid = ((waterOrIce.LiquidLevel + (aboveblock.LiquidLevel > 0 ? 1 : 0)) / 8f >= pos.Y - (int)pos.Y);
            }
            entity.InLava = block.LiquidCode == "lava";
            entity.Swimming = middleWOIBlock.IsLiquid();

            if (!onGroundBefore && entity.OnGround)
            {
                entity.OnFallToGround(prevYMotion);
            }

            if ((!entity.Swimming && !feetInLiquidBefore && entity.FeetInLiquid) || (!entity.FeetInLiquid && !swimmingBefore && entity.Swimming))
            {
                entity.OnCollideWithLiquid();
            }

            if ((swimmingBefore && !entity.Swimming && !entity.FeetInLiquid) || (feetInLiquidBefore && !entity.FeetInLiquid && !entity.Swimming))
            {
                entity.OnExitedLiquid();
            }

            if (!falling || entity.OnGround || controls.IsClimbing)
            {
                entity.PositionBeforeFalling.Set(outposition);
            }

            profiler.Mark("apply-collisionandflags");

            Cuboidd testedEntityBox = collisionTester.entityBox;

            int xMax = (int)testedEntityBox.X2;
            int yMax = (int)testedEntityBox.Y2;
            int zMax = (int)testedEntityBox.Z2;
            int zMin = (int)testedEntityBox.Z1;
            for (int y = (int)testedEntityBox.Y1; y <= yMax; y++)
            {
                for (int x = (int)testedEntityBox.X1; x <= xMax; x++)
                {
                    for (int z = zMin; z <= zMax; z++)
                    {
                        collisionTester.tmpPos.Set(x, y, z);
                        collisionTester.tempCuboid.Set(x, y, z, x + 1, y + 1, z + 1);

                        if (collisionTester.tempCuboid.IntersectsOrTouches(testedEntityBox))
                        {
                            // Saves us a few cpu cycles
                            if (x == (int)pos.X && z == (int)pos.Z && y == (int)pos.Y)
                            {
                                block.OnEntityInside(entity.World, entity, collisionTester.tmpPos);
                                continue;
                            }

                            blockAccess.GetBlock(x, y, z).OnEntityInside(entity.World, entity, collisionTester.tmpPos);
                        }
                    }
                }
            }
            profiler.Mark("trigger-insideblock");
        }

        private void HandleSneaking(EntityPos pos, EntityControls controls, float dt)
        {
            if (!controls.Sneak || !entity.OnGround || pos.Motion.Y > 0) return;

            // Sneak to prevent falling off blocks
            Vec3d testPosition = new Vec3d();
            testPosition.Set(pos.X, pos.Y - GlobalConstants.GravityPerSecond * dt, pos.Z);

            // Only apply this if he was on the ground in the first place
            if (!collisionTester.IsColliding(entity.World.BlockAccessor, sneakTestCollisionbox, testPosition))
            {
                return;
            }

            Block belowBlock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)pos.Y - 1, (int)pos.Z);


            // Test for X
            testPosition.Set(outposition.X, outposition.Y - GlobalConstants.GravityPerSecond * dt, pos.Z);
            if (!collisionTester.IsColliding(entity.World.BlockAccessor, sneakTestCollisionbox, testPosition))
            {
                // Weird hack so you can climb down ladders more easily
                if (belowBlock.Climbable)
                {
                    outposition.X += (pos.X - outposition.X) / 10;
                }
                else
                {
                    outposition.X = pos.X;
                }
            }


            // Test for Z
            testPosition.Set(pos.X, outposition.Y - GlobalConstants.GravityPerSecond * dt, outposition.Z);
            if (!collisionTester.IsColliding(entity.World.BlockAccessor, sneakTestCollisionbox, testPosition))
            {
                // Weird hack so you can climb down ladders more easily
                if (belowBlock.Climbable)
                {
                    outposition.Z += (pos.Z - outposition.Z) / 10;
                }
                else
                {
                    outposition.Z = pos.Z;
                }
            }
        }




        private bool HandleSteppingOnBlocksSmooth(EntityPos pos, Vec3d moveDelta, float dtFac, EntityControls controls)
        {
            if (!controls.TriesToMove || (!entity.OnGround && !entity.Swimming) || entity.Properties.Habitat == EnumHabitat.Underwater) return false;

            Cuboidd entityCollisionBox = entity.CollisionBox.ToDouble();

            //how far ahead to scan for steppable blocks 
            //TODO needs to be increased for large and fast creatures (e.g. wolves)
            double max = 0.75;//Math.Max(entityCollisionBox.MaxX - entityCollisionBox.MinX, entityCollisionBox.MaxZ - entityCollisionBox.MinZ);
            double searchBoxLength = max + (controls.Sprint ? 0.25 : controls.Sneak ? 0.05 : 0.2);

            Vec2d center = new Vec2d((entityCollisionBox.X1 + entityCollisionBox.X2) / 2, (entityCollisionBox.Z1 + entityCollisionBox.Z2) / 2);
            var searchHeight = Math.Max(entityCollisionBox.Y1 + stepHeight, entityCollisionBox.Y2);
            entityCollisionBox.Translate(pos.X, pos.Y, pos.Z);

            Vec3d walkVec = controls.WalkVector.Clone();
            Vec3d walkVecNormalized = walkVec.Clone().Normalize();

            Cuboidd entitySensorBox;

            var outerX = walkVecNormalized.X * searchBoxLength;
            var outerZ = walkVecNormalized.Z * searchBoxLength;

            entitySensorBox = new Cuboidd
            {
                X1 = Math.Min(0, outerX),
                X2 = Math.Max(0, outerX),

                Z1 = Math.Min(0, outerZ),
                Z2 = Math.Max(0, outerZ),


                //Y1 = entity.CollisionBox.Y1 - (entity.CollidedVertically ? 0 : 0.05), //also check below if not on ground
                Y1 = entity.CollisionBox.Y1 + 0.01 - (!entity.CollidedVertically && !controls.Jump ? 0.05 : 0), //also check below if not on ground and not jumping 

                Y2 = searchHeight
            };

            entitySensorBox.Translate(center.X, 0, center.Y);
            entitySensorBox.Translate(pos.X, pos.Y, pos.Z);

            Vec3d testVec = new Vec3d();
            Vec2d testMotion = new Vec2d();


            List<Cuboidd> stepableBoxes = FindSteppableCollisionboxSmooth(entityCollisionBox, entitySensorBox, moveDelta.Y, walkVec);

            if (stepableBoxes != null && stepableBoxes.Count > 0)
            {
                if (TryStepSmooth(controls, pos, testMotion.Set(walkVec.X, walkVec.Z), dtFac, stepableBoxes, entityCollisionBox)) return true;

                Cuboidd entitySensorBoxXAligned = entitySensorBox.Clone();
                if (entitySensorBoxXAligned.Z1 == pos.Z + center.Y)
                {
                    entitySensorBoxXAligned.Z2 = entitySensorBoxXAligned.Z1;
                }
                else
                {
                    entitySensorBoxXAligned.Z1 = entitySensorBoxXAligned.Z2;
                }



                if (TryStepSmooth(controls, pos, testMotion.Set(walkVec.X, 0), dtFac, FindSteppableCollisionboxSmooth(entityCollisionBox, entitySensorBoxXAligned, moveDelta.Y, testVec.Set(walkVec.X, walkVec.Y, 0)), entityCollisionBox)) return true;


                Cuboidd entitySensorBoxZAligned = entitySensorBox.Clone();
                if (entitySensorBoxZAligned.X1 == pos.X + center.X)
                {
                    entitySensorBoxZAligned.X2 = entitySensorBoxZAligned.X1;
                }
                else
                {
                    entitySensorBoxZAligned.X1 = entitySensorBoxZAligned.X2;
                }


                if (TryStepSmooth(controls, pos, testMotion.Set(0, walkVec.Z), dtFac, FindSteppableCollisionboxSmooth(entityCollisionBox, entitySensorBoxZAligned, moveDelta.Y, testVec.Set(0, walkVec.Y, walkVec.Z)), entityCollisionBox)) return true;

            }

            return false;
        }


        private bool TryStepSmooth(EntityControls controls, EntityPos pos, Vec2d walkVec, float dtFac, List<Cuboidd> stepableBoxes, Cuboidd entityCollisionBox)
        {
            if (stepableBoxes == null || stepableBoxes.Count == 0) return false;
            double gravityOffset = 0.03; // This added constant value is a fugly hack because outposition has gravity added, but has not adjusted its position to the ground floor yet

            var walkVecOrtho = new Vec2d(walkVec.Y, -walkVec.X).Normalize();

            var maxX = Math.Abs(walkVecOrtho.X * 0.3) + 0.001;
            var minX = -maxX;
            var maxZ = Math.Abs(walkVecOrtho.Y * 0.3) + 0.001;
            var minZ = -maxZ;
            var col = new Cuboidf((float)minX, entity.CollisionBox.Y1, (float)minZ, (float)maxX, entity.CollisionBox.Y2, (float)maxZ);

            double newYPos = pos.Y;
            bool foundStep = false;
            foreach (var stepableBox in stepableBoxes)
            {
                double heightDiff = stepableBox.Y2 - entityCollisionBox.Y1 + gravityOffset;
                Vec3d steppos = new Vec3d(GameMath.Clamp(outposition.X, stepableBox.MinX, stepableBox.MaxX), outposition.Y + heightDiff, GameMath.Clamp(outposition.Z, stepableBox.MinZ, stepableBox.MaxZ));

                bool canStep = !collisionTester.IsColliding(entity.World.BlockAccessor, col, steppos, false);

                if (canStep)
                {
                    double elevateFactor = controls.Sprint ? 0.10 : controls.Sneak ? 0.025 : 0.05;
                    if (!stepableBox.IntersectsOrTouches(entityCollisionBox))
                    {
                        newYPos = Math.Max(newYPos, Math.Min(pos.Y + elevateFactor * dtFac, stepableBox.Y2 - entity.CollisionBox.Y1 + gravityOffset));
                    }
                    else
                    {
                        newYPos = Math.Max(newYPos, pos.Y + elevateFactor * dtFac);
                    }
                    foundStep = true;

                }
            }
            if (foundStep)
            {
                pos.Y = newYPos;
                collisionTester.ApplyTerrainCollision(entity, pos, dtFac, ref outposition);
            }
            return foundStep;
        }

        /// <summary>
        /// Get all blocks colliding with entityBoxRel
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="entityBoxRel"></param>
        /// <param name="pos"></param>
        /// <param name="blocks">The found blocks</param>
        /// <param name="alsoCheckTouch"></param>
        /// <returns>If any blocks have been found</returns>
        public bool GetCollidingCollisionBox(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, out CachedCuboidList blocks, bool alsoCheckTouch = true)
        {
            blocks = new CachedCuboidList();
            BlockPos blockPos = new BlockPos();
            Vec3d blockPosVec = new Vec3d();
            Cuboidd entityBox = entityBoxRel.ToDouble().Translate(pos);


            int minX = (int)(entityBoxRel.MinX + pos.X);
            int minY = (int)(entityBoxRel.MinY + pos.Y - 1);  // -1 for the extra high collision box of fences
            int minZ = (int)(entityBoxRel.MinZ + pos.Z);
            int maxX = (int)Math.Ceiling(entityBoxRel.MaxX + pos.X);
            int maxY = (int)Math.Ceiling(entityBoxRel.MaxY + pos.Y);
            int maxZ = (int)Math.Ceiling(entityBoxRel.MaxZ + pos.Z);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        Block block = blockAccessor.GetBlock(x, y, z);
                        blockPos.Set(x, y, z);
                        blockPosVec.Set(x, y, z);

                        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, blockPos);

                        for (int i = 0; collisionBoxes != null && i < collisionBoxes.Length; i++)
                        {
                            Cuboidf collBox = collisionBoxes[i];
                            if (collBox == null) continue;

                            bool colliding = alsoCheckTouch ? entityBox.IntersectsOrTouches(collBox, blockPosVec) : entityBox.Intersects(collBox, blockPosVec);
                            if (colliding)
                            {
                                blocks.Add(collBox, x, y, z, block);

                            }
                        }
                    }
                }
            }

            return blocks.Count > 0;
        }

        private List<Cuboidd> FindSteppableCollisionboxSmooth(Cuboidd entityCollisionBox, Cuboidd entitySensorBox, double motionY, Vec3d walkVector)
        {

            var stepableBoxes = new List<Cuboidd>();
            GetCollidingCollisionBox(entity.World.BlockAccessor, entitySensorBox.ToFloat(), new Vec3d(), out var blocks, true);

            for (int i = 0; i < blocks.Count; i++)
            {
                Cuboidd collisionbox = blocks.cuboids[i];
                Block block = blocks.blocks[i];

                if (!block.CanStep)
                {
                    // Blocks which are low relative to this entity (e.g. small troughs are low for the player) can still be stepped on
                    if (entity.CollisionBox.Height < 5 * block.CollisionBoxes[0].Height) continue;
                }

                EnumIntersect intersect = CollisionTester.AabbIntersect(collisionbox, entityCollisionBox, walkVector);
                //if (intersect == EnumIntersect.NoIntersect) continue;

                // Already stuck somewhere? Can't step stairs
                // Would get stuck vertically if I go up? Can't step up either
                if ((intersect == EnumIntersect.Stuck && !block.AllowStepWhenStuck) || (intersect == EnumIntersect.IntersectY && motionY > 0))
                {
                    return null;
                }

                double heightDiff = collisionbox.Y2 - entityCollisionBox.Y1;

                //if (heightDiff <= -0.02 || !IsBoxInFront(entityCollisionBox, walkVector, collisionbox)) continue;
                if (heightDiff <= (entity.CollidedVertically ? 0 : -0.05)) continue;
                if (heightDiff <= stepHeight)
                {
                    //if (IsBoxInFront(entityCollisionBox, walkVector, collisionbox))
                    {
                        stepableBoxes.Add(collisionbox);
                    }
                }
            }

            return stepableBoxes;
        }


        Cuboidd steppingCollisionBox = new Cuboidd();
        Vec3d steppingTestVec = new Vec3d();
        Vec3d steppingTestMotion = new Vec3d();
        private bool HandleSteppingOnBlocks(EntityPos pos, Vec3d moveDelta, float dtFac, EntityControls controls)
        {
            if (controls.WalkVector.X == 0 && controls.WalkVector.Z == 0) return false;
            if ((!entity.OnGround && !entity.Swimming) || entity.Properties.Habitat == EnumHabitat.Underwater) return false;


            Cuboidd steppingCollisionBox = this.steppingCollisionBox;
            steppingCollisionBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);
            steppingCollisionBox.Y2 = Math.Max(steppingCollisionBox.Y1 + stepHeight, steppingCollisionBox.Y2);

            Vec3d walkVec = controls.WalkVector;
            Cuboidd stepableBox = findSteppableCollisionbox(steppingCollisionBox, moveDelta.Y, walkVec);

            if (stepableBox != null)
            {
                Vec3d testMotion = this.steppingTestMotion;
                testMotion.Set(moveDelta.X, moveDelta.Y, moveDelta.Z);
                if (tryStep(pos, testMotion, dtFac, stepableBox, steppingCollisionBox)) return true;

                Vec3d testVec = this.steppingTestVec;
                testMotion.Z = 0;
                if (tryStep(pos, testMotion, dtFac, findSteppableCollisionbox(steppingCollisionBox, moveDelta.Y, testVec.Set(walkVec.X, walkVec.Y, 0)), steppingCollisionBox)) return true;

                testMotion.Set(0, moveDelta.Y, moveDelta.Z);
                if (tryStep(pos, testMotion, dtFac, findSteppableCollisionbox(steppingCollisionBox, moveDelta.Y, testVec.Set(0, walkVec.Y, walkVec.Z)), steppingCollisionBox)) return true;

                return false;
            }

            return false;
        }

        private bool tryStep(EntityPos pos, Vec3d moveDelta, float dtFac, Cuboidd stepableBox, Cuboidd entityCollisionBox)
        {
            if (stepableBox == null) return false;

            double heightDiff = stepableBox.Y2 - entityCollisionBox.Y1 + 0.01 * 3f; // This added constant value is a fugly hack because outposition has gravity added, but has not adjusted its position to the ground floor yet
            Vec3d steppos = outposition.OffsetCopy(moveDelta.X, heightDiff, moveDelta.Z);
            bool canStep = !collisionTester.IsColliding(entity.World.BlockAccessor, entity.CollisionBox, steppos, false);

            if (canStep)
            {
                pos.Y += 0.07 * dtFac;
                collisionTester.ApplyTerrainCollision(entity, pos, dtFac, ref outposition);
                return true;
            }

            return false;
        }

        private Cuboidd findSteppableCollisionbox(Cuboidd entityCollisionBox, double motionY, Vec3d walkVector)
        {
            Cuboidd stepableBox = null;

            int maxCount = collisionTester.CollisionBoxList.Count;
            for (int i = 0; i < maxCount; i++)
            {
                Block block = collisionTester.CollisionBoxList.blocks[i];

                if (!block.CanStep)
                {
                    // Blocks which are low relative to this entity (e.g. small troughs are low for the player) can still be stepped on
                    if (entity.CollisionBox.Height < 5 * block.CollisionBoxes[0].Height) continue;
                }

                Cuboidd collisionbox = collisionTester.CollisionBoxList.cuboids[i];
                EnumIntersect intersect = CollisionTester.AabbIntersect(collisionbox, entityCollisionBox, walkVector);
                if (intersect == EnumIntersect.NoIntersect) continue;

                // Already stuck somewhere? Can't step stairs
                // Would get stuck vertically if I go up? Can't step up either
                if ((intersect == EnumIntersect.Stuck && !block.AllowStepWhenStuck) || (intersect == EnumIntersect.IntersectY && motionY > 0))
                {
                    return null;
                }

                double heightDiff = collisionbox.Y2 - entityCollisionBox.Y1;

                if (heightDiff <= 0) continue;
                if (heightDiff <= stepHeight && (stepableBox == null || stepableBox.Y2 < collisionbox.Y2))
                {
                    stepableBox = collisionbox;
                }
            }

            return stepableBox;
        }







        Matrixf tmpModelMat = new Matrixf();

        /// <summary>
        /// If an attachment point called "Center" exists, then this method
        /// offsets the creatures collision box so that the Center attachment point is the center of the collision box.
        /// </summary>
        public void AdjustCollisionBoxToAnimation(float dtFac)
        {
            float[] hitboxOff = new float[4] { 0, 0, 0, 1 };

            AttachmentPointAndPose apap = entity.AnimManager.Animator.GetAttachmentPointPose("Center");

            if (apap == null)
            {
                return;
            }

            AttachmentPoint ap = apap.AttachPoint;

            float rotX = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateX : 0;
            float rotY = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateY : 0;
            float rotZ = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateZ : 0;

            float[] ModelMat = Mat4f.Create();
            Mat4f.Identity(ModelMat);
            Mat4f.Translate(ModelMat, ModelMat, 0, entity.CollisionBox.Y2 / 2, 0);

            double[] quat = Quaterniond.Create();
            Quaterniond.RotateX(quat, quat, entity.Pos.Pitch + rotX * GameMath.DEG2RAD);
            Quaterniond.RotateY(quat, quat, entity.Pos.Yaw + (rotY + 90) * GameMath.DEG2RAD);
            Quaterniond.RotateZ(quat, quat, entity.Pos.Roll + rotZ * GameMath.DEG2RAD);

            float[] qf = new float[quat.Length];
            for (int k = 0; k < quat.Length; k++) qf[k] = (float)quat[k];
            Mat4f.Mul(ModelMat, ModelMat, Mat4f.FromQuat(Mat4f.Create(), qf));

            float scale = entity.Properties.Client.Size;

            Mat4f.Translate(ModelMat, ModelMat, 0, -entity.CollisionBox.Y2 / 2, 0f);
            Mat4f.Scale(ModelMat, ModelMat, new float[] { scale, scale, scale });
            Mat4f.Translate(ModelMat, ModelMat, -0.5f, 0, -0.5f);

            tmpModelMat
                .Set(ModelMat)
                .Mul(apap.AnimModelMatrix)
                .Translate(ap.PosX / 16f, ap.PosY / 16f, ap.PosZ / 16f)
            ;

            EntityPos epos = entity.SidedPos;

            float[] endVec = Mat4f.MulWithVec4(tmpModelMat.Values, hitboxOff);

            float motionX = endVec[0] - (entity.CollisionBox.X1 - entity.OriginCollisionBox.X1);
            float motionZ = endVec[2] - (entity.CollisionBox.Z1 - entity.OriginCollisionBox.Z1);

            if (Math.Abs(motionX) > 0.00001 || Math.Abs(motionZ) > 0.00001)
            {
                EntityPos posMoved = epos.Copy();
                posMoved.Motion.X = motionX;
                posMoved.Motion.Z = motionZ;

                moveDelta.Set(posMoved.Motion.X, posMoved.Motion.Y, posMoved.Motion.Z);

                collisionTester.ApplyTerrainCollision(entity, posMoved, dtFac, ref outposition);

                double reflectX = (outposition.X - epos.X) / dtFac - motionX;
                double reflectZ = (outposition.Z - epos.Z) / dtFac - motionZ;

                epos.Motion.X = reflectX;
                epos.Motion.Z = reflectZ;

                entity.CollisionBox.Set(entity.OriginCollisionBox);
                entity.CollisionBox.Translate(endVec[0], 0, endVec[2]);


                entity.SelectionBox.Set(entity.OriginSelectionBox);
                entity.SelectionBox.Translate(endVec[0], 0, endVec[2]);
            }
        }



        public override string PropertyName()
        {
            return "controlledentityphysics";
        }
        public void Dispose()
        {

        }

    }
}
