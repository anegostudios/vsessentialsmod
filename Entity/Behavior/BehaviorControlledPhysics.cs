using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
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
            if (entity.World is IServerWorldAccessor)
            {
                entity.Pos.SetFrom(entity.ServerPos);
            }
        }


       

        public void TickEntityPhysics(EntityPos pos, EntityControls controls, float dt)
        {
            // This seems to make creatures clip into the terrain. Le sigh. 
            // Since we currently only really need it for when the creature is dead, let's just use it only there

            // Also running animations for all nearby living entities is pretty CPU intensive so
            // the AnimationManager also just ticks if the entity is in view or dead
            if (!entity.Alive)
            {
                AdjustCollisionBoxToAnimation();
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
                pos.X += pos.Motion.X;
                pos.Y += pos.Motion.Y;
                pos.Z += pos.Motion.Z;

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
        BlockPos tmpPos = new BlockPos();
        Cuboidd entityBox = new Cuboidd();

        public void DisplaceWithBlockCollision(EntityPos pos, EntityControls controls, float dt)
        {
            IBlockAccessor blockAccess = entity.World.BlockAccessor;

            nextPosition.Set(pos.X + pos.Motion.X, pos.Y + pos.Motion.Y, pos.Z + pos.Motion.Z);
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
                    if (controls.Jump) pos.Motion.Y = 0.04;
                }

                nextPosition.Set(pos.X + pos.Motion.X, pos.Y + pos.Motion.Y, pos.Z + pos.Motion.Z);
            }

            collisionTester.ApplyTerrainCollision(entity, pos, ref outposition, !(entity is EntityPlayer));

            if (!entity.Properties.CanClimbAnywhere)
            {
                controls.IsStepping = HandleSteppingOnBlocks(pos, controls);
            }
            
            HandleSneaking(pos, controls, dt);



            if (blockAccess.IsNotTraversable((pos.X + pos.Motion.X), pos.Y, pos.Z))
            {
                outposition.X = pos.X;
            }
            if (blockAccess.IsNotTraversable(pos.X, (pos.Y + pos.Motion.Y), pos.Z))
            {
                outposition.Y = pos.Y;
            }
            if (blockAccess.IsNotTraversable(pos.X, pos.Y, (pos.Z + pos.Motion.Z)))
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

        private bool HandleSteppingOnBlocks(EntityPos pos, EntityControls controls)
        {
            if (!controls.TriesToMove || (!entity.OnGround && !entity.Swimming)) return false;

            Cuboidd entityCollisionBox = entity.CollisionBox.ToDouble();
            entityCollisionBox.Translate(pos.X, pos.Y, pos.Z);

            Vec3d walkVec = controls.WalkVector;
            Vec3d testVec = new Vec3d();
            Vec3d testMotion = new Vec3d();

            Cuboidd stepableBox = findSteppableCollisionbox(entityCollisionBox, pos.Motion.Y, walkVec);
            
            // Must have walked into a slab
            if (stepableBox != null)
            {
                return
                    tryStep(pos, testMotion.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z), stepableBox, entityCollisionBox) ||
                    tryStep(pos, testMotion.Set(pos.Motion.X, pos.Motion.Y, 0), findSteppableCollisionbox(entityCollisionBox, pos.Motion.Y, testVec.Set(walkVec.X, walkVec.Y, 0)), entityCollisionBox) ||
                    tryStep(pos, testMotion.Set(0, pos.Motion.Y, pos.Motion.Z), findSteppableCollisionbox(entityCollisionBox, pos.Motion.Y, testVec.Set(0, walkVec.Y, walkVec.Z)), entityCollisionBox)
                ;
            }

            return false;
        }

        private bool tryStep(EntityPos pos, Vec3d motion, Cuboidd stepableBox, Cuboidd entityCollisionBox)
        {
            if (stepableBox == null) return false;

            double heightDiff = stepableBox.Y2 - entityCollisionBox.Y1 + 0.01;
            Vec3d steppos = outposition.OffsetCopy(motion.X, heightDiff, motion.Z);
            bool canStep = !collisionTester.IsColliding(entity.World.BlockAccessor, entity.CollisionBox, steppos, false);

            if (canStep)
            {
                pos.Y += 0.07;
                //pos.Motion.Y = 0.001;
                collisionTester.ApplyTerrainCollision(entity, pos, ref outposition, !(entity is EntityPlayer));
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

                if (!collisionTester.CollisionBoxList.blocks[i].CanStep) continue;

                EnumIntersect intersect = CollisionTester.AabbIntersect(collisionbox, entityCollisionBox, walkVector);
                if (intersect == EnumIntersect.NoIntersect) continue;

                // Already stuck somewhere? Can't step stairs
                // Would get stuck vertically if I go up? Can't step up either
                if (intersect == EnumIntersect.Stuck || (intersect == EnumIntersect.IntersectY && motionY > 0))
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
        public void AdjustCollisionBoxToAnimation()
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
            Mat4f.Scale(ModelMat, ModelMat, new float[] { scale, scale, scale });
            Mat4f.Translate(ModelMat, ModelMat, -0.5f, -entity.CollisionBox.Y2 / 2, -0.5f);

            tmpModelMat
                .Set(ModelMat)
                .Mul(apap.AnimModelMatrix)
                .Translate(ap.PosX / 16f, ap.PosY / 16f, ap.PosZ / 16f)
            ;

            EntityPos epos = entity.LocalPos;

            float[] endVec = Mat4f.MulWithVec4(tmpModelMat.Values, hitboxOff);

            float motionX = endVec[0] - (entity.CollisionBox.X1 - entity.OriginCollisionBox.X1);
            float motionZ = endVec[2] - (entity.CollisionBox.Z1 - entity.OriginCollisionBox.Z1);

            if (Math.Abs(motionX) > 0.00001 || Math.Abs(motionZ) > 0.00001)
            {

                EntityPos posMoved = epos.Copy();
                posMoved.Motion.X = motionX;
                posMoved.Motion.Z = motionZ;

                collisionTester.ApplyTerrainCollision(entity, posMoved, ref outposition, !(entity is EntityPlayer));

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
