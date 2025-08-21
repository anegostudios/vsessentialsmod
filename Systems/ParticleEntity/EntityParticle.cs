using System;
using System.Drawing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{

    public abstract class EntityParticle : ParticleBase
    {
        public abstract string Type { get; }
        protected float Size
        {
            set { SizeX = SizeY = SizeZ = value; }
        }
        protected float SizeX { get; set; } = 1f;
        protected float SizeY { get; set; } = 1f;
        protected float SizeZ { get; set; } = 1f;
        protected float GravityStrength { get; set; } = 1f;

        protected float dirNormalizedX;
        protected float dirNormalizedY;
        protected float dirNormalizedZ;


        protected bool SwimOnLiquid;

        public void OnSpawned(ParticlePhysics physicsSim)
        {
            lightrgbs = physicsSim.BlockAccess.GetLightRGBsAsInt((int)Position.X, (int)Position.Y, (int)Position.Z);
        }

        public override void TickNow(float dt, float physicsdt, ICoreClientAPI api, ParticlePhysics physicsSim)
        {
            motion.Set(Velocity.X * dt, Velocity.Y * dt, Velocity.Z * dt);

            float s = SizeY / 4f;

            updatePositionWithCollision(dt, api, physicsSim, s);

            Velocity.Y -= GravityStrength * dt;
            physicsSim.HandleBoyancy(Position, Velocity, SwimOnLiquid, GravityStrength, dt, s);

            tickCount++;
            if (tickCount > 2)
            {
                doSlowTick(physicsSim, dt * 3);
            }

            float len = Velocity.Length();
            if (len > 0.05)
            {
                dirNormalizedX = Velocity.X;
                dirNormalizedY = Velocity.Y;
                dirNormalizedZ = Velocity.Z;
            }
        }

        

        protected virtual void doSlowTick(ParticlePhysics physicsSim, float dt)
        {
            lightrgbs = physicsSim.BlockAccess.GetLightRGBsAsInt((int)Position.X, (int)Position.Y, (int)Position.Z);
            tickCount = 0;
        }

        public override void UpdateBuffers(MeshData buffer, Vec3d cameraPos, ref int posPosition, ref int rgbaPosition, ref int flagPosition)
        {
            float f = 1 - prevPosAdvance;
            float[] CustomFloats = buffer.CustomFloats.Values;
            CustomFloats[posPosition++] = (float)(Position.X - prevPosDeltaX * f - cameraPos.X);
            CustomFloats[posPosition++] = (float)(Position.Y - prevPosDeltaY * f - cameraPos.Y);
            CustomFloats[posPosition++] = (float)(Position.Z - prevPosDeltaZ * f - cameraPos.Z);

            CustomFloats[posPosition++] = SizeX;
            CustomFloats[posPosition++] = SizeY;
            CustomFloats[posPosition++] = SizeZ;

            posPosition = UpdateAngles(CustomFloats, posPosition);

            byte[] CustomBytes = buffer.CustomBytes.Values;
            CustomBytes[rgbaPosition++] = (byte)lightrgbs;
            CustomBytes[rgbaPosition++] = (byte)(lightrgbs >> 8);
            CustomBytes[rgbaPosition++] = (byte)(lightrgbs >> 16);
            CustomBytes[rgbaPosition++] = (byte)(lightrgbs >> 24);

            CustomBytes[rgbaPosition++] = ColorBlue;
            CustomBytes[rgbaPosition++] = ColorGreen;
            CustomBytes[rgbaPosition++] = ColorRed;
            CustomBytes[rgbaPosition++] = ColorAlpha;

            buffer.Flags[flagPosition++] = VertexFlags;
        }

        public virtual int UpdateAngles(float[] customFloats, int posPosition)
        {
            customFloats[posPosition++] = dirNormalizedX;
            customFloats[posPosition++] = dirNormalizedY;
            customFloats[posPosition++] = dirNormalizedZ;
            customFloats[posPosition++] = 0;  // Padding because it's a vec4 on the shader

            return posPosition;
        }
    }
}
