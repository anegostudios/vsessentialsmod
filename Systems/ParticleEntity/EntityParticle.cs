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

        float dirNormalizedX;
        float dirNormalizedY;
        float dirNormalizedZ;


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
            buffer.CustomFloats.Values[posPosition++] = (float)(Position.X - prevPosDeltaX * f - cameraPos.X);
            buffer.CustomFloats.Values[posPosition++] = (float)(Position.Y - prevPosDeltaY * f - cameraPos.Y);
            buffer.CustomFloats.Values[posPosition++] = (float)(Position.Z - prevPosDeltaZ * f - cameraPos.Z);

            buffer.CustomFloats.Values[posPosition++] = SizeX;
            buffer.CustomFloats.Values[posPosition++] = SizeY;
            buffer.CustomFloats.Values[posPosition++] = SizeZ;

            buffer.CustomFloats.Values[posPosition++] = dirNormalizedX;
            buffer.CustomFloats.Values[posPosition++] = dirNormalizedY;
            buffer.CustomFloats.Values[posPosition++] = dirNormalizedZ;
            posPosition++;  // Padding because we cannot do 3 byte aligned data

            buffer.CustomBytes.Values[rgbaPosition++] = (byte)lightrgbs;
            buffer.CustomBytes.Values[rgbaPosition++] = (byte)(lightrgbs >> 8);
            buffer.CustomBytes.Values[rgbaPosition++] = (byte)(lightrgbs >> 16);
            buffer.CustomBytes.Values[rgbaPosition++] = (byte)(lightrgbs >> 24);

            buffer.CustomBytes.Values[rgbaPosition++] = ColorBlue;
            buffer.CustomBytes.Values[rgbaPosition++] = ColorGreen;
            buffer.CustomBytes.Values[rgbaPosition++] = ColorRed;
            buffer.CustomBytes.Values[rgbaPosition++] = ColorAlpha;

            buffer.Flags[flagPosition++] = VertexFlags;
        }
    }
}
