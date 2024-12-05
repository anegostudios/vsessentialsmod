using ProtoBuf;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    [ProtoContract]
    public class ClothPoint
    {
        public static bool PushingPhysics = false;

        [ProtoMember(1)]
        public int PointIndex;
        [ProtoMember(2)]
        public float Mass;
        [ProtoMember(3)]
        public float InvMass;
        [ProtoMember(4)]
        public Vec3d Pos;
        [ProtoMember(5)]
        public Vec3f Velocity = new Vec3f();
        [ProtoMember(6)]
        public Vec3f Tension = new Vec3f();
        [ProtoMember(7)]
        float GravityStrength = 1;
        [ProtoMember(8)]
        bool pinned;
        [ProtoMember(9)]
        public long pinnedToEntityId;
        [ProtoMember(10)]
        BlockPos pinnedToBlockPos;
        [ProtoMember(11)]
        public Vec3f pinnedToOffset;
        [ProtoMember(12)]
        float pinnedToOffsetStartYaw;
        [ProtoMember(13)]
        string pinnedToPlayerUid; // player entity ids change over time >.<


        public bool Dirty { get; internal set; }


        public EnumCollideFlags CollideFlags;
        public float YCollideRestMul;
        Vec4f tmpvec = new Vec4f();
        ClothSystem cs;
        Entity pinnedTo;
        Matrixf pinOffsetTransform;

        // These values are set be the constraints, they should actually get summed up though. 
        // For rope, a single set works though, because we only need the ends, connected by 1 constraint
        // In otherwords: Cloth pulling motion thing is not supported
        public Vec3d TensionDirection = new Vec3d();
        public double extension;


        // Damping factor. Velocities are multiplied by this
        private float dampFactor = 0.9f;

        float accum1s;

        public ClothPoint(ClothSystem cs)
        {
            this.cs = cs;
            Pos = new Vec3d();
            init();
        }

        protected ClothPoint() { }

        public ClothPoint(ClothSystem cs, int pointIndex, double x, double y, double z)
        {
            this.cs = cs;
            this.PointIndex = pointIndex;
            Pos = new Vec3d(x, y, z);
            init();
        }

        public void setMass(float mass)
        {
            this.Mass = mass;
            InvMass = 1 / mass;
        }


        void init()
        {
            setMass(1);
        }

        public Entity PinnedToEntity => pinnedTo;
        public BlockPos PinnedToBlockPos => pinnedToBlockPos;
        public bool Pinned => pinned;


        public void PinTo(Entity toEntity, Vec3f pinOffset)
        {
            pinned = true;
            pinnedTo = toEntity;
            pinnedToEntityId = toEntity.EntityId;
            pinnedToOffset = pinOffset;
            pinnedToOffsetStartYaw = toEntity.SidedPos.Yaw;
            pinOffsetTransform = Matrixf.Create();
            pinnedToBlockPos = null;
            if (toEntity is EntityPlayer eplr) pinnedToPlayerUid = eplr.PlayerUID;

            MarkDirty();
        }

        public void PinTo(BlockPos blockPos, Vec3f offset)
        {
            this.pinnedToBlockPos = blockPos;
            pinnedToOffset = offset;
            pinnedToPlayerUid = null;
            pinned = true;
            Pos.Set(pinnedToBlockPos).Add(pinnedToOffset);
            pinnedTo = null;
            pinnedToEntityId = 0;
            MarkDirty();
        }

        public void UnPin()
        {
            pinned = false;
            pinnedTo = null;
            pinnedToPlayerUid = null;
            pinnedToEntityId = 0;
            MarkDirty();
        }

        public void MarkDirty()
        {
            Dirty = true;
        }

        public void update(float dt, IWorldAccessor world)
        {
            if (pinnedTo == null && pinnedToPlayerUid != null)
            {
                var eplr = world.PlayerByUid(pinnedToPlayerUid)?.Entity;
                if (eplr?.World != null) PinTo(eplr, pinnedToOffset);
            }

            if (pinned) 
            {
                if (pinnedTo != null)
                {
                    Entity pinnedToMounted = pinnedTo;

                    if (pinnedToMounted is EntityAgent pinnedToAgent && pinnedToAgent.MountedOn?.Entity != null)
                    {
                        pinnedToMounted = pinnedToAgent.MountedOn.Entity;
                    }


                    if (pinnedToMounted.ShouldDespawn && pinnedToMounted.DespawnReason?.Reason != EnumDespawnReason.Unload)
                    {
                        UnPin();
                        return;
                    }

                    // New ideas:
                    // don't apply force onto the player/entity on compression
                    // apply huge forces onto the player on strong extension (to prevent massive stretching) (just set player motion to 0 or so. or we add a new countermotion field thats used in EntityControlledPhysics?) 

                    float weight = pinnedToMounted.Properties.Weight;
                    
                    float counterTensionStrength = GameMath.Clamp(50f / weight, 0.1f, 2f);

                    bool extraResist = (pinnedToMounted as EntityAgent)?.Controls.Sneak == true || (pinnedToMounted is EntityPlayer && (pinnedToMounted.AnimManager?.IsAnimationActive("sit") == true || pinnedToMounted.AnimManager?.IsAnimationActive("sleep") == true));
                    float tensionResistStrength = weight / 10f * (extraResist ? 200 : 1);

                    var eplr = pinnedTo as EntityPlayer;
                    var eagent = pinnedTo as EntityAgent;
                    Vec4f outvec;

                    AttachmentPointAndPose apap = eplr?.AnimManager?.Animator?.GetAttachmentPointPose("RightHand");
                    if (apap == null) apap = pinnedTo?.AnimManager?.Animator?.GetAttachmentPointPose("rope");

                    if (apap != null)
                    {
                        Matrixf modelmat = new Matrixf();
                        if (eplr != null) modelmat.RotateY(eagent.BodyYaw + GameMath.PIHALF);
                        else modelmat.RotateY(pinnedTo.SidedPos.Yaw + GameMath.PIHALF);

                        modelmat.Translate(-0.5, 0, -0.5);

                        apap.MulUncentered(modelmat);
                        outvec = modelmat.TransformVector(new Vec4f(0,0,0,1));
                    }
                    else
                    {
                        pinOffsetTransform.Identity();
                        pinOffsetTransform.RotateY(pinnedTo.SidedPos.Yaw - pinnedToOffsetStartYaw);
                        tmpvec.Set(pinnedToOffset.X, pinnedToOffset.Y, pinnedToOffset.Z, 1);
                        outvec = pinOffsetTransform.TransformVector(tmpvec);
                    }

                    EntityPos pos = pinnedTo.SidedPos;
                    Pos.Set(pos.X + outvec.X, pos.Y + outvec.Y, pos.Z + outvec.Z);

                    bool pushable = true;// PushingPhysics && (eplr == null || eplr.Player.WorldData.CurrentGameMode != EnumGameMode.Creative);
                    
                    if (pushable && extension > 0) // Do not act on compressive force
                    {
                        float f = counterTensionStrength * dt * 0.006f;

                        pinnedToMounted.SidedPos.Motion.Add(
                                GameMath.Clamp(Math.Abs(TensionDirection.X) - tensionResistStrength, 0, 400) * f * Math.Sign(TensionDirection.X),
                                GameMath.Clamp(Math.Abs(TensionDirection.Y) - tensionResistStrength, 0, 400) * f * Math.Sign(TensionDirection.Y),
                                GameMath.Clamp(Math.Abs(TensionDirection.Z) - tensionResistStrength, 0, 400) * f * Math.Sign(TensionDirection.Z)
                            );
                    }

                    Velocity.Set(0, 0, 0);
                } else
                {
                    Velocity.Set(0, 0, 0);

                    if (pinnedToBlockPos != null)
                    {
                        accum1s += dt;

                        if (accum1s >= 1)
                        {
                            accum1s = 0;
                            Block block = cs.api.World.BlockAccessor.GetBlock(PinnedToBlockPos);
                            if (!block.HasBehavior<BlockBehaviorRopeTieable>())
                            {
                                UnPin();
                            }
                        }
                    }
                }
                
                return;
            }

            // Calculate the force on this point
            Vec3f force = Tension.Clone();
            force.Y -= GravityStrength * 10;

            // Calculate the acceleration
            Vec3f acceleration = force * (float)InvMass;

            if (CollideFlags == 0)
            {
                acceleration.X += (float)cs.windSpeed.X * InvMass;
            }


            // Update velocity
            Vec3f nextVelocity = Velocity + (acceleration * dt);
            
            // Damp the velocity
            nextVelocity *= dampFactor;

            // Collision detection
            float size = 0.1f;
            cs.pp.HandleBoyancy(Pos, nextVelocity, cs.boyant, GravityStrength, dt, size);
            CollideFlags = cs.pp.UpdateMotion(Pos, nextVelocity, size);

            dt *= 0.99f;
            Pos.Add(nextVelocity.X * dt, nextVelocity.Y * dt, nextVelocity.Z * dt);
            

            Velocity.Set(nextVelocity);
            Tension.Set(0, 0, 0);
        }


        public void restoreReferences(ClothSystem cs, IWorldAccessor world)
        {
            this.cs = cs;

            if (pinnedToEntityId != 0)
            {
                pinnedTo = world.GetEntityById(pinnedToEntityId);
                if (pinnedTo == null)
                {
                   // UnPin();
                }
                else
                {
                    PinTo(pinnedTo, pinnedToOffset);
                }
            }

            if (pinnedToBlockPos != null)
            {
                PinTo(pinnedToBlockPos, pinnedToOffset);
            }
        }
        public void restoreReferences(Entity entity)
        {
            if (pinnedToEntityId == entity.EntityId)
            {
                PinTo(entity, pinnedToOffset);
            }
        }

        public void updateFromPoint(ClothPoint point, IWorldAccessor world)
        {
            PointIndex = point.PointIndex;
            Mass = point.Mass;
            InvMass = point.InvMass;
            Pos.Set(point.Pos);
            Velocity.Set(point.Pos);
            Tension.Set(point.Tension);
            GravityStrength = point.GravityStrength;
            pinned = point.pinned;
            pinnedToEntityId = point.pinnedToEntityId;
            pinnedToPlayerUid = point.pinnedToPlayerUid;
            if (pinnedToEntityId != 0)
            {
                pinnedTo = world.GetEntityById(pinnedToEntityId);
                if (pinnedTo != null)
                {
                    PinTo(pinnedTo, pinnedToOffset);
                }
                else UnPin();
                
            }

            pinnedToBlockPos = pinnedToBlockPos.SetOrCreate(point.pinnedToBlockPos);
            pinnedToOffset = pinnedToOffset.SetOrCreate(point.pinnedToOffset);

            pinnedToOffsetStartYaw = point.pinnedToOffsetStartYaw;
        }
    }

}
 