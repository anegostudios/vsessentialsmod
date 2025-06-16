using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;


#nullable disable

namespace Vintagestory.GameContent;

public class EntityParticleFish : EntityParticle
{
    public override string Type => "fish";

    protected ICoreClientAPI capi;
    protected float dieAccum = 0;
    protected static Random rand = new Random();

    Vec3d StartingPosition = new Vec3d();
    bool flee;
    float maxspeed;
    public FastVec3i StartPos;

    public static int[][] Colors =
    [
        new[] { 224, 221, 26, 255 }, // yellow
        new[] { 224, 142, 26, 255 }, // orange
        new[] { 224, 86, 26, 255 }, // orange - red
        new[] { 224, 53, 26, 255 }, // red
        //new[] { 160, 191, 187, 255 }, // gray - silver
        new[] { 41, 148, 206, 255 }, // light blue
        new[] { 27, 88, 193, 255 }, // dark blue
        new[] { 157, 88, 193, 255 } // purple
    ];

    public EntityParticleFish[] FriendFishes;

    public EntityParticleFish(ICoreClientAPI capi, double x, double y, double z, Vec3f size, int colorindex, float maxspeed)
    {
        this.capi = capi;
        Position.Set(x, y, z);
        StartingPosition.Set(x, y, z);
        
        this.maxspeed = maxspeed;
        Alive = true;
        SizeX = size.X;
        SizeY = size.Y;
        SizeZ = size.Z;
        GravityStrength = 0;

        ColorBlue = (byte)Colors[colorindex][0];
        ColorGreen = (byte)Colors[colorindex][1];
        ColorRed = (byte)Colors[colorindex][2];
        ColorAlpha = (byte)Colors[colorindex][3];
    }

    public override void TickNow(float dt, float physicsdt, ICoreClientAPI api, ParticlePhysics physicsSim)
    {
        base.TickNow(dt, physicsdt, api, physicsSim);

        Velocity.X = GameMath.Clamp(Velocity.X, -maxspeed, maxspeed);
        Velocity.Y = GameMath.Clamp(Velocity.Y, -maxspeed, maxspeed);
        Velocity.Z = GameMath.Clamp(Velocity.Z, -maxspeed, maxspeed);
    }

    protected override void doSlowTick(ParticlePhysics physicsSim, float dt)
    {
        base.doSlowTick(physicsSim, dt);

        var delta = StartingPosition.SubCopy(Position);
        float dist = (float)delta.Length();
        if (!flee) {
            delta.Normalize();
            float velo = GameMath.Clamp((dist-3) * 0.1f, 0, 0.4f);
            Velocity.Add((float)delta.X * velo, (float)delta.Y * velo, (float)delta.Z * velo);
        }

        DoSchool();

        if (rand.NextDouble() < 0.01)
        {
            var dirx = (float)rand.NextDouble() * 0.66f - 0.33f;
            var diry = (float)rand.NextDouble() * 0.2f - 0.1f;
            var dirz = (float)rand.NextDouble() * 0.66f - 0.33f;
            propel(dirx/3f, diry/3f, dirz/3f);
            DoSchool();
            return;
        }

        var npe = capi.World.NearestPlayer(Position.X, Position.Y, Position.Z).Entity;
        double sqdist = 50 * 50;
        flee = false;
        if (npe != null && (sqdist = npe.Pos.SquareDistanceTo(Position)) < 5 * 5 && (npe.Player.WorldData.CurrentGameMode != EnumGameMode.Creative && npe.Player.WorldData.CurrentGameMode != EnumGameMode.Spectator))
        {
            var vec = npe.Pos.XYZ.Sub(Position).Normalize();
            propel((float)-vec.X, 0f, (float)-vec.Z);
            flee = true;
            DoSchool();
        }

        var block = capi.World.BlockAccessor.GetBlock((int)Position.X, (int)(Position.Y + 0.4f), (int)Position.Z, BlockLayersAccess.Fluid);
        if (!block.IsLiquid())
        {
            Velocity.Y -= 0.1f;
        }
        

        if (npe == null || sqdist > 40 * 40)
        {
            dieAccum += dt;
            if (dieAccum > 15)
            {
                Alive = false;
            }
        }
        else
        {
            dieAccum = 0;
        }
    }

    private void DoSchool()
    {
        if (flee) return;

        var center = new Vec3d();
        var schoolvelocity = new Vec3f();
        int len = FriendFishes.Length;

        for (int i = 0; i < len; i++)
        {
            center.Add(
                FriendFishes[i].Position.X / len,
                FriendFishes[i].Position.Y / len,
                FriendFishes[i].Position.Z / len
            );

            schoolvelocity.Add(FriendFishes[i].Velocity);

            var delta = Position.SubCopy(center);
            var dist = (float)delta.Length();
            float awayVelocity = GameMath.Clamp((0.05f - dist) / 2, 0, 0.03f);

            Velocity.Add(
                (float)Math.Sign(delta.X) * awayVelocity,
                (float)Math.Sign(delta.Y) * awayVelocity,
                (float)Math.Sign(delta.Z) * awayVelocity
            );
        }

        var cdelta = Position.SubCopy(center);
        var cdist = (float)cdelta.Length();
        float towardsVelocity = GameMath.Clamp((cdist - 0.25f) / 1, 0, 0.03f);

        Velocity.Add(
            -(float)Math.Sign(cdelta.X) * towardsVelocity,
            -(float)Math.Sign(cdelta.Y) * towardsVelocity,
            -(float)Math.Sign(cdelta.Z) * towardsVelocity
        );

        Velocity.Add(
            schoolvelocity.X / len / 20f,
            schoolvelocity.Y / len / 20f,
            schoolvelocity.Z / len / 20f
        );
    }

    private void propel(float dirx, float diry, float dirz)
    {
        Velocity.Add(dirx, diry, dirz);
    }
}
