using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

public class EntityParticleFish : EntityParticle
{
    public override string Type => "fish";

    protected ICoreClientAPI capi;
    protected float swimCooldown = 0;
    protected float dieAccum = 0;
    protected LCGRandom rand;

    private static int[][] Colors = new[]
    {
        new[] { 224, 221, 26, 255 }, // yellow
        new[] { 224, 142, 26, 255 }, // orange
        new[] { 224, 86, 26, 255 }, // orange - red
        new[] { 224, 53, 26, 255 }, // red
        new[] { 160, 191, 187, 255 }, // gray - silver
        new[] { 41, 148, 206, 255 }, // light blue
        new[] { 27, 88, 193, 255 }, // dark blue
        new[] { 157, 88, 193, 255 } // purple
    };

    public EntityParticleFish(ICoreClientAPI capi, double x, double y, double z)
    {
        this.capi = capi;
        Position.Set(x, y, z);
        rand = new LCGRandom(this.capi.World.Seed + 6545);
        rand.InitPositionSeed((int)x, (int)z);


        Alive = true;
        Size = 0.45f + (float)capi.World.Rand.NextDouble() * 0.25f;
        GravityStrength = 0;

        var nextInt = rand.NextInt(Colors.Length);
        ColorBlue = (byte)Colors[nextInt][0];
        ColorGreen = (byte)Colors[nextInt][1];
        ColorRed = (byte)Colors[nextInt][2];
        ColorAlpha = (byte)Colors[nextInt][3];
    }

    public override void TickNow(float dt, float physicsdt, ICoreClientAPI api, ParticlePhysics physicsSim)
    {
        base.TickNow(dt, physicsdt, api, physicsSim);

        Velocity.X *= 0.9f;
        Velocity.Y *= 0.9f;
        Velocity.Z *= 0.9f;
    }

    protected override void doSlowTick(ParticlePhysics physicsSim, float dt)
    {
        base.doSlowTick(physicsSim, dt);

        if (swimCooldown < 0)
        {
            var dirx = (float)rand.NextDouble() * 0.66f - 0.33f;
            var diry = (float)rand.NextDouble() * 0.2f - 0.1f;
            var dirz = (float)rand.NextDouble() * 0.66f - 0.33f;
            propel(dirx, diry, dirz);
        }

        if (swimCooldown > 0)
        {
            swimCooldown = GameMath.Max(0, swimCooldown - dt);
            return;
        }

        if (rand.NextDouble() < 0.2)
        {
            var dirx = (float)rand.NextDouble() * 0.66f - 0.33f;
            var diry = (float)rand.NextDouble() * 0.2f - 0.1f;
            var dirz = (float)rand.NextDouble() * 0.66f - 0.33f;
            propel(dirx, diry, dirz);
            return;
        }

        var npe = capi.World.NearestPlayer(Position.X, Position.Y, Position.Z).Entity;
        double sqdist = 50 * 50;
        if (npe != null && (sqdist = npe.Pos.SquareHorDistanceTo(Position)) < 3 * 3)
        {
            var vec = npe.Pos.XYZ.Sub(Position).Normalize();
            propel((float)-vec.X / 3f, 0f, (float)-vec.Z / 3f);
        }

        var block = capi.World.BlockAccessor.GetBlock((int)Position.X, (int)Position.Y, (int)Position.Z, BlockLayersAccess.Fluid);
        if (!block.IsLiquid())
        {
            Alive = false;
            return;
        }

        if (npe == null || sqdist > 20 * 20)
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

    private void propel(float dirx, float diry, float dirz)
    {
        Velocity.Add(dirx, diry, dirz);
        swimCooldown = 1f;
    }
}
