using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorControlledPhysics : EntityBehavior, IRenderer
    {
        protected float accumulator = 0;
        protected Vec3d outposition = new Vec3d(); // Temporary field
        protected CollisionTester collisionTester = new CollisionTester();

        internal List<EntityLocomotion> Locomotors = new List<EntityLocomotion>();

        public float stepHeight = 0.6f;

        protected Cuboidf smallerCollisionBox = new Cuboidf();
        protected Vec3d prevPos = new Vec3d();

        protected bool duringRenderFrame;
        public double RenderOrder => 0;
        public int RenderRange => 9999;

        ICoreClientAPI capi;

        protected bool smoothStepping;

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

            for (int i = 0; i < Locomotors.Count; i++)
            {
                Locomotors[i].Initialize(properties);
            }

            smallerCollisionBox = entity.CollisionBox.Clone().OmniNotDownGrowBy(-0.1f);

            if (entity.World.Side == EnumAppSide.Client)
            {
                capi = (entity.World.Api as ICoreClientAPI);
                duringRenderFrame = true;
                capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "controlledphysics");

            }

            accumulator = (float)entity.World.Rand.NextDouble() * GlobalConstants.PhysicsFrameTime;
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

            accumulator += deltaTime;

            if (accumulator > 1)
            {
                accumulator = 1;
            }
            
            while (accumulator >= GlobalConstants.PhysicsFrameTime)
            {
                prevPos.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
                GameTick(entity, GlobalConstants.PhysicsFrameTime);
                accumulator -= GlobalConstants.PhysicsFrameTime;
            }
            
            entity.PhysicsUpdateWatcher?.Invoke(accumulator, prevPos);
            entity.World.FrameProfiler.Mark("entity-controlledphysics-end");
        }


        public virtual void GameTick(Entity entity, float dt) {
            EntityControls controls = ((EntityAgent)entity).Controls;
            TickEntityPhysics(entity.ServerPos, controls, dt);  // this was entity.ServerPos - wtf? - apparently needed so they don't glitch through terrain o.O

            if (entity.World.Side == EnumAppSide.Server)
            {
                entity.Pos.SetFrom(entity.ServerPos);
            }
        }


       

        public void TickEntityPhysics(EntityPos pos, EntityControls controls, float dt)
        {
            float dtFac = 60 * dt;

            // This seems to make creatures clip into the terrain. Le sigh. 
            // Since we currently only really need it for when the creature is dead, let's just use it only there

            // Also running animations for all nearby living entities is pretty CPU intensive so
            // the AnimationManager also just ticks if the entity is in view or dead
            if (!entity.Alive)
            {
                AdjustCollisionBoxToAnimation(dtFac);
            }

            
            foreach (EntityLocomotion locomotor in Locomotors)
            {
                if (locomotor.Applicable(entity, pos, controls))
                {
                    locomotor.Apply(dt, entity, pos, controls);
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

            pos.Motion.X = GameMath.Clamp(pos.Motion.X, -10, 10);
            pos.Motion.Y = GameMath.Clamp(pos.Motion.Y, -10, 10);
            pos.Motion.Z = GameMath.Clamp(pos.Motion.Z, -10, 10);

            if (!controls.NoClip)
            {
                DisplaceWithBlockCollision(pos, controls, dt);
            } else
            {
                pos.X += pos.Motion.X * dt * 60f;
                pos.Y += pos.Motion.Y * dt * 60f;
                pos.Z += pos.Motion.Z * dt * 60f;

                entity.Swimming = false;
                entity.FeetInLiquid = false;
                entity.OnGround = false;
            }
            
            

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
            float dtFac = 60 * dt;

            moveDelta.Set(pos.Motion.X * dtFac, pos.Motion.Y * dtFac, pos.Motion.Z * dtFac);

            nextPosition.Set(pos.X + moveDelta.X, pos.Y + moveDelta.Y, pos.Z + moveDelta.Z);
            bool falling = pos.Motion.Y < 0;
            bool feetInLiquidBefore = entity.FeetInLiquid;
            bool onGroundBefore = entity.OnGround;
            bool swimmingBefore = entity.Swimming;

            double prevYMotion = pos.Motion.Y;

            controls.IsClimbing = false;

            if (!onGroundBefore && entity.Properties.CanClimb == true)
            {
                int height = (int)Math.Ceiling(entity.CollisionBox.Y2);

                entityBox.Set(entity.CollisionBox).Translate(pos.X, pos.Y, pos.Z);

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
                //moveDelta.Y = pos.Motion.Y * dt * 60f;
                //nextPosition.Set(pos.X + moveDelta.X, pos.Y + moveDelta.Y, pos.Z + moveDelta.Z);
            }

            collisionTester.ApplyTerrainCollision(entity, pos, dtFac, ref outposition, stepHeight);
            
            if (!entity.Properties.CanClimbAnywhere)
            {
                if (smoothStepping)
                {
                    controls.IsStepping = HandleSteppingOnBlocksSmooth(pos, moveDelta, dtFac, controls);
                } else
                {
                    controls.IsStepping = HandleSteppingOnBlocks(pos, moveDelta, dtFac, controls);
                }
                

            }

            
            HandleSneaking(pos, controls, dt);

            if (entity.CollidedHorizontally && !controls.IsClimbing && !controls.IsStepping)
            {
                if (blockAccess.GetBlock((int)pos.X, (int)(pos.Y), (int)pos.Z).LiquidLevel >= 7 || (blockAccess.GetBlock((int)pos.X, (int)(pos.Y - 0.05), (int)pos.Z).LiquidLevel >= 7))
                {
                    pos.Motion.Y += 0.2 * dt;
                    controls.IsStepping = true;
                }
            }


            if (blockAccess.IsNotTraversable((pos.X + pos.Motion.X * dt * 60f), pos.Y, pos.Z))
            {
                outposition.X = pos.X;
            }
            if (blockAccess.IsNotTraversable(pos.X, (pos.Y + pos.Motion.Y * dt * 60f), pos.Z))
            {
                outposition.Y = pos.Y;
            }
            if (blockAccess.IsNotTraversable(pos.X, pos.Y, (pos.Z + pos.Motion.Z * dt * 60f)))
            {
                outposition.Z = pos.Z;
            }

            pos.SetPos(outposition);

            
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
            Block aboveblock = blockAccess.GetBlock(posX, (int)(pos.Y + 1), posZ);
            Block middleBlock = blockAccess.GetBlock(posX, (int)(pos.Y + entity.SwimmingOffsetY), posZ);


            entity.OnGround = (entity.CollidedVertically && falling && !controls.IsClimbing) || controls.IsStepping;
            entity.FeetInLiquid = block.IsLiquid() && ((block.LiquidLevel + (aboveblock.LiquidLevel > 0 ? 1 : 0)) / 8f >= pos.Y - (int)pos.Y);
            entity.InLava = block.LiquidCode == "lava";
            entity.Swimming = middleBlock.IsLiquid();

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

            Cuboidd testedEntityBox = collisionTester.entityBox;

            for (int y = (int) testedEntityBox.Y1; y <= (int) testedEntityBox.Y2; y++)
            {
                for (int x = (int) testedEntityBox.X1; x <= (int) testedEntityBox.X2; x++)
                {
                    for (int z = (int) testedEntityBox.Z1; z <= (int) testedEntityBox.Z2; z++)
                    {
                        collisionTester.tmpPos.Set(x, y, z);
                        collisionTester.tempCuboid.Set(x, y, z, x + 1, y + 1, z + 1);

                        if (collisionTester.tempCuboid.IntersectsOrTouches(testedEntityBox))
                        {
                            // Saves us a few cpu cycles
                            if (x == (int)pos.X && y == (int)pos.Y && z == (int)pos.Z)
                            {
                                block.OnEntityInside(entity.World, entity, collisionTester.tmpPos);
                                continue;
                            }

                            blockAccess.GetBlock(x, y, z).OnEntityInside(entity.World, entity, collisionTester.tmpPos);
                        }
                    }
                }
            }
        }



        

        private void HandleSneaking(EntityPos pos, EntityControls controls, float dt)
        {
            // Sneak to prevent falling off blocks
            if (controls.Sneak && entity.OnGround && pos.Motion.Y <= 0)
            {
                Vec3d testPosition = new Vec3d();
                testPosition.Set(pos.X, pos.Y - GlobalConstants.GravityPerSecond * dt, pos.Z);

                // Only apply this if he was on the ground in the first place
                if (!collisionTester.IsColliding(entity.World.BlockAccessor, smallerCollisionBox, testPosition))
                {
                    return;
                }
                
                testPosition.Set(outposition.X, outposition.Y - GlobalConstants.GravityPerSecond * dt, pos.Z);

                Block belowBlock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)pos.Y - 1, (int)pos.Z);

                // Test for X
                if (!collisionTester.IsColliding(entity.World.BlockAccessor, smallerCollisionBox, testPosition))
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

                // Test for X
                if (!collisionTester.IsColliding(entity.World.BlockAccessor, smallerCollisionBox, testPosition))
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
        }




        

        private bool HandleSteppingOnBlocksSmooth(EntityPos pos, Vec3d moveDelta, float dtFac, EntityControls controls)
        {

            if (!controls.TriesToMove || (!entity.OnGround && !entity.Swimming)) return false;

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


                Y1 = entity.CollisionBox.Y1 - (entity.CollidedVertically ? 0 : 0.05), //also check below if not on ground
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
                } else
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

            double newYPos = pos.Y;
            bool foundStep = false;
            foreach (var stepableBox in stepableBoxes)
            {
                double heightDiff = stepableBox.Y2 - entityCollisionBox.Y1 + gravityOffset;
                Vec3d steppos = new Vec3d(GameMath.Clamp(outposition.X, stepableBox.MinX, stepableBox.MaxX), outposition.Y + heightDiff, GameMath.Clamp(outposition.Z, stepableBox.MinZ, stepableBox.MaxZ));

                var maxX = Math.Abs(walkVecOrtho.X * 0.3) + 0.001;
                var minX = -maxX;

                var maxZ = Math.Abs(walkVecOrtho.Y * 0.3) + 0.001;
                var minZ = -maxZ;
                
                var col = new Cuboidf((float)minX, entity.CollisionBox.Y1, (float)minZ, (float)maxX, entity.CollisionBox.Y2, (float)maxZ);
                bool canStep = !collisionTester.IsColliding(entity.World.BlockAccessor, col , steppos, false);

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
                                blocks.Add(collBox, blockPos, block);
                                
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

                if (!block.CanStep) continue;

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


        private bool HandleSteppingOnBlocks(EntityPos pos, Vec3d moveDelta, float dtFac, EntityControls controls)
        {
            if (!controls.TriesToMove || (!entity.OnGround && !entity.Swimming)) return false;


            Cuboidd entityCollisionBox = entity.CollisionBox.ToDouble();
            entityCollisionBox.Translate(pos.X, pos.Y, pos.Z);
            entityCollisionBox.Y2 = Math.Max(entityCollisionBox.Y1 + stepHeight, entityCollisionBox.Y2);

            Vec3d walkVec = controls.WalkVector;
            Vec3d testVec = new Vec3d();
            Vec3d testMotion = new Vec3d();

            Cuboidd stepableBox = findSteppableCollisionbox(entityCollisionBox, moveDelta.Y, walkVec);


            if (stepableBox != null)
            {
                return
                    tryStep(pos, testMotion.Set(moveDelta.X, moveDelta.Y, moveDelta.Z), dtFac, stepableBox, entityCollisionBox) ||
                    tryStep(pos, testMotion.Set(moveDelta.X, moveDelta.Y, 0), dtFac, findSteppableCollisionbox(entityCollisionBox, moveDelta.Y, testVec.Set(walkVec.X, walkVec.Y, 0)), entityCollisionBox) ||
                    tryStep(pos, testMotion.Set(0, moveDelta.Y, moveDelta.Z), dtFac, findSteppableCollisionbox(entityCollisionBox, moveDelta.Y, testVec.Set(0, walkVec.Y, walkVec.Z)), entityCollisionBox)
                ;
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

            for (int i = 0; i < collisionTester.CollisionBoxList.Count; i++)
            {
                Cuboidd collisionbox = collisionTester.CollisionBoxList.cuboids[i];
                Block block = collisionTester.CollisionBoxList.blocks[i];

                if (!block.CanStep) continue;

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
                if (heightDiff <= stepHeight)
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

                double reflectX = outposition.X - epos.X - motionX;
                double reflectZ = outposition.Z - epos.Z - motionZ;

                epos.Motion.X = reflectX;
                epos.Motion.Z = reflectZ;

                entity.CollisionBox.Set(entity.OriginCollisionBox);
                entity.CollisionBox.Translate(endVec[0], 0, endVec[2]);
            }
            //Console.WriteLine("{0}/{1}", reflectX, reflectZ);
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
