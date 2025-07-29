//using HarmonyLib;
using ProtoBuf;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Vintagestory.GameContent
{
    [ProtoContract]
    public class ClothPoint
    {
        public float AnimalWeight;

        public static bool PushingPhysics;

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
        private float GravityStrength = 1f;

        [ProtoMember(8)]
        private bool pinned;

        [ProtoMember(9)]
        public long pinnedToEntityId;

        [ProtoMember(10)]
        private BlockPos pinnedToBlockPos;

        [ProtoMember(11)]
        public Vec3f pinnedToOffset;

        [ProtoMember(12)]
        private float pinnedToOffsetStartYaw;

        [ProtoMember(13)]
        private string pinnedToPlayerUid;

        public EnumCollideFlags CollideFlags;

        public float YCollideRestMul;

        private Vec4f tmpvec = new Vec4f();

        private ClothSystem cs;

        private Entity pinnedTo;

        private Matrixf pinOffsetTransform;

        public Vec3d TensionDirection = new Vec3d();

        public double extension;

        private float dampFactor = 0.9f;

        private float accum1s;
        private Entity attachedEntity;

        public bool Dirty { get; internal set; }

        public Entity PinnedToEntity => pinnedTo;

        public BlockPos PinnedToBlockPos => pinnedToBlockPos;

        public bool Pinned => pinned;

        public ClothPoint(ClothSystem cs)
        {
            this.cs = cs;
            Pos = new Vec3d();
            init();
        }

        protected ClothPoint()
        {
        }

        public ClothPoint(ClothSystem cs, int pointIndex, double x, double y, double z)
        {
            this.cs = cs;
            PointIndex = pointIndex;
            Pos = new Vec3d(x, y, z);
            init();
        }

        public ClothPoint(ClothSystem cs, Vec3d pos, Entity attachedEntity) : this(cs)
        {
            Pos = pos;
            this.attachedEntity = attachedEntity;
        }

        public void setMass(float mass)
        {
            Mass = mass;
            InvMass = 1f / mass;
        }

        private void init()
        {
            setMass(1f);
        }

        public void PinTo(Entity toEntity, Vec3f pinOffset) // Here underlies an issue with the vertical player movement SOS 
        {
            pinned = true;
            pinnedTo = toEntity;
            pinnedToEntityId = toEntity.EntityId;
            pinnedToOffset = pinOffset;
            pinnedToOffsetStartYaw = toEntity.SidedPos.Yaw;
            pinOffsetTransform = Matrixf.Create();
            pinnedToBlockPos = null;
            if (toEntity is EntityPlayer entityPlayer)
            {
                pinnedToPlayerUid = entityPlayer.PlayerUID;
            }

            MarkDirty();
        }

        public void PinTo(BlockPos blockPos, Vec3f offset)
        {
            pinnedToBlockPos = blockPos;
            pinnedToOffset = offset;
            pinnedToPlayerUid = null;
            pinned = true;
            Pos.Set(pinnedToBlockPos).Add(pinnedToOffset);
            pinnedTo = null;
            pinnedToEntityId = 0L;
            MarkDirty();
        }

        public void UnPin()
        {
            pinned = false;
            pinnedTo = null;
            pinnedToPlayerUid = null;
            pinnedToEntityId = 0L;
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
                EntityPlayer entityPlayer = world.PlayerByUid(pinnedToPlayerUid)?.Entity;
                if (entityPlayer?.World != null)
                {
                    PinTo(entityPlayer, pinnedToOffset);
                }
            }

            if (pinned)
            {
                if (pinnedTo != null)                                    // Starting here take a look 
                {
                    Entity entity = pinnedTo;
                    if (entity is EntityAgent entityAgent && entityAgent.MountedOn?.Entity != null)
                    {
                        entity = entityAgent.MountedOn.Entity;
                    }

                    if (entity.ShouldDespawn)
                    {
                        EntityDespawnData despawnReason = entity.DespawnReason;
                        if (despawnReason == null || despawnReason.Reason != EnumDespawnReason.Unload)
                        {
                            UnPin();
                            return;
                        }
                    }

                    float weight = entity.Properties.Weight;
                    float num = GameMath.Clamp(50f / weight, 0.1f, 2f);
                    EntityAgent obj = entity as EntityAgent;
                    int num2;
                    if (obj == null || !obj.Controls.Sneak)
                    {
                        if (entity is EntityPlayer)
                        {
                            IAnimationManager animManager = entity.AnimManager;
                            num2 = (((animManager != null && animManager.IsAnimationActive("sit")) || (entity.AnimManager?.IsAnimationActive("sleep") ?? false)) ? 1 : 0);
                        }
                        else
                        {
                            num2 = 0;
                        }
                    }
                    else
                    {
                        num2 = 1;
                    }

                    bool flag = (byte)num2 != 0;
                    float num3 = weight / 10f * (float)((!flag) ? 1 : 200);
                    EntityPlayer entityPlayer2 = pinnedTo as EntityPlayer;
                    EntityAgent entityAgent2 = pinnedTo as EntityAgent;
                    AttachmentPointAndPose attachmentPointAndPose = entityPlayer2?.AnimManager?.Animator?.GetAttachmentPointPose("RightHand");
                    if (attachmentPointAndPose == null)
                    {
                        attachmentPointAndPose = pinnedTo?.AnimManager?.Animator?.GetAttachmentPointPose("rope");
                    }

                    Vec4f vec4f;
                    if (attachmentPointAndPose != null)
                    {
                        Matrixf matrixf = new Matrixf();
                        if (entityPlayer2 != null)
                        {
                            matrixf.RotateY(entityAgent2.BodyYaw + MathF.PI / 2f);
                        }
                        else
                        {
                            matrixf.RotateY(pinnedTo.SidedPos.Yaw + MathF.PI / 2f);
                        }

                        matrixf.Translate(-0.5, 0.0, -0.5);
                        attachmentPointAndPose.MulUncentered(matrixf);
                        vec4f = matrixf.TransformVector(new Vec4f(0f, 0f, 0f, 1f));
                    }
                    else
                    {
                        pinOffsetTransform.Identity();
                        pinOffsetTransform.RotateY(pinnedTo.SidedPos.Yaw - pinnedToOffsetStartYaw);
                        tmpvec.Set(pinnedToOffset.X, pinnedToOffset.Y, pinnedToOffset.Z, 1f);
                        vec4f = pinOffsetTransform.TransformVector(tmpvec);
                    }

                    EntityPos sidedPos = pinnedTo.SidedPos;                                   // SOS SOS SOS SOS SOS SOS SOS SOS SOS this makes the physics magic equation : entity.SidedPos.Motion.Add SOS CUSTOM LOGIC STARTS HERE SOS
                    Pos.Set(sidedPos.X + (double)vec4f.X, sidedPos.Y + (double)vec4f.Y, sidedPos.Z + (double)vec4f.Z);
                    if (true && extension > 0.0)
                    {
                        float num4 = num * dt * 0.006f;
                        Vec3d direction = TensionDirection.Clone();
                        direction.Normalize();

                        double tensionForce = num3 * 1.65f;
                        double gravityForce = num3;

                        // Horizontal direction (XZ only)
                        Vec3d dirXZ = new Vec3d(direction.X, 0, direction.Z);
                        double dirXZLen = dirXZ.Length();

                        if (dirXZLen > 0.001)
                        {
                            dirXZ /= dirXZLen; // Normalize horizontal vector
                        }
                        else
                        {
                            dirXZ.Set(0, 0, 0); // Avoid NaNs
                        }

                        // Vertical direction (Y only)
                        double dirY = direction.Y;

                        // Compute net pull-back (from rope + gravity)
                        double pullBackDiag = Math.Sqrt(tensionForce * tensionForce + gravityForce * gravityForce);

                        // Compute push needed from player
                        double velocityForce = num3 * 1.65f;
                        double extraForce = tensionForce - velocityForce; // extra force to pull the animal 
                        double totalPushForce = velocityForce + extraForce;

                        // Separate horizontal and vertical move forces
                        double horizontalForce = Math.Sqrt(totalPushForce * totalPushForce + gravityForce * gravityForce);
                        double verticalForce = -gravityForce;  // Negative since gravity pulls down

                        // Final motion vectors Not needed yet
                        //Vec3d horizontalMotion = dirXZ * horizontalForce;
                        //double verticalMotion = dirY * verticalForce;





                        bool isGrounded = entity.OnGround;
                        bool isTaut = extension > 0.05;

                        if (isTaut && isGrounded)
                        {
                            Vec3d tensionDrag = new Vec3d(
                                GameMath.Clamp(Math.Abs(TensionDirection.X) + horizontalForce - pullBackDiag, 0.0, 400.0) * Math.Sign(TensionDirection.X),
                                GameMath.Clamp(Math.Abs(TensionDirection.Y) + horizontalForce - pullBackDiag + verticalForce, 0.0, 400.0) * Math.Sign(TensionDirection.Y),
                                GameMath.Clamp(Math.Abs(TensionDirection.Z) + horizontalForce - pullBackDiag, 0.0, 400.0) * Math.Sign(TensionDirection.Z)
                             ) * num4;

                            entity.SidedPos.Motion.Add(tensionDrag);

                        }
                        else if (isTaut)
                        {
                            Vec3d tensionDrag = new Vec3d(
                                GameMath.Clamp(Math.Abs((TensionDirection.X) * 0.1) + (horizontalForce - pullBackDiag) * 0.5, 0.0, 400.0) * Math.Sign(TensionDirection.X),
                                GameMath.Clamp(Math.Abs((TensionDirection.Y) * 0.3) + horizontalForce - pullBackDiag + verticalForce, 0.0, 400.0) * Math.Sign(TensionDirection.Y),
                                GameMath.Clamp(Math.Abs((TensionDirection.Z) * 0.1) + (horizontalForce - pullBackDiag) * 0.5, 0.0, 400.0) * Math.Sign(TensionDirection.Z)
                             ) * num4;

                            entity.SidedPos.Motion.Add(tensionDrag);

                        }
                        else
                        {
                            // No forces applied
                        }
                    }

                    Velocity.Set(0f, 0f, 0f);
                    return;
                }

                Velocity.Set(0f, 0f, 0f);
                if (!(pinnedToBlockPos != null))
                {
                    return;
                }

                accum1s += dt;
                if (accum1s >= 1f)
                {
                    accum1s = 0f;
                    if (!cs.api.World.BlockAccessor.GetBlock(PinnedToBlockPos).HasBehavior<BlockBehaviorRopeTieable>())
                    {
                        UnPin();
                    }
                }
            }
            else
            {
                Vec3f vec3f = Tension.Clone();
                vec3f.Y -= GravityStrength * 10f;
                Vec3f vec3f2 = vec3f * InvMass;
                if (CollideFlags == (EnumCollideFlags)0)
                {
                    vec3f2.X += (float)cs.windSpeed.X * InvMass;
                }

                Vec3f vec3f3 = Velocity + vec3f2 * dt;
                vec3f3 *= dampFactor;
                float num5 = 0.1f;
                cs.pp.HandleBoyancy(Pos, vec3f3, cs.boyant, GravityStrength, dt, num5);
                CollideFlags = cs.pp.UpdateMotion(Pos, vec3f3, num5);
                dt *= 0.99f;
                Pos.Add(vec3f3.X * dt, vec3f3.Y * dt, vec3f3.Z * dt);
                Velocity.Set(vec3f3);
                Tension.Set(0f, 0f, 0f);
            }
        }

        public void restoreReferences(ClothSystem cs, IWorldAccessor world)
        {
            this.cs = cs;
            if (pinnedToEntityId != 0L)
            {
                pinnedTo = world.GetEntityById(pinnedToEntityId);
                if (pinnedTo != null)
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

        

        public void updateFromPoint(ClothPoint originalPoint, IWorldAccessor world)
        {
            PointIndex = originalPoint.PointIndex;
            Mass = originalPoint.Mass;
            InvMass = originalPoint.InvMass;

            Pos.Set(originalPoint.Pos);
            Velocity.Set(originalPoint.Velocity);
            Tension.Set(originalPoint.Tension);

            GravityStrength = originalPoint.GravityStrength;
            pinned = originalPoint.pinned;
            pinnedToPlayerUid = originalPoint.pinnedToPlayerUid;
            pinnedToOffsetStartYaw = originalPoint.pinnedToOffsetStartYaw;

            pinnedToEntityId = originalPoint.pinnedToEntityId;
            pinnedToBlockPos = pinnedToBlockPos.SetOrCreate(originalPoint.PinnedToBlockPos);
            pinnedToOffset = pinnedToOffset.SetOrCreate(originalPoint.pinnedToOffset);

            CollideFlags = originalPoint.CollideFlags;
            YCollideRestMul = originalPoint.YCollideRestMul;

            // Try to re-pin to entity if possible
            if (pinnedToEntityId != 0L)
            {
                pinnedTo = world.GetEntityById(pinnedToEntityId);
                if (pinnedTo != null)
                {
                    PinTo(pinnedTo, pinnedToOffset);
                }
                else
                {
                    UnPin();
                }
            }
            else if (pinnedToBlockPos != null)
            {
                PinTo(pinnedToBlockPos, pinnedToOffset);
            }
        }
    }
}


