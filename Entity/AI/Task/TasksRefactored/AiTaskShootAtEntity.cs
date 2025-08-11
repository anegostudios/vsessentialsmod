using Newtonsoft.Json;
using OpenTK.Mathematics;
using System;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Throws 'EntityThrownStone' into target.<br/>
/// Use only projectiles with zero drag or add pitch dispersion. When aiming this task does not account for air drag.<br/>
/// <br/>
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2<br/>
/// - executionChance default value: 0.1 => 1<br/>
/// - releaseAtMs => ThrowAtMs<br/>
/// - dont forget to adjust gravity factor, take it from projectile entity<br/>
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskShootAtEntityConfig : AiTaskBaseTargetableConfig
{
    /// <summary>
    /// Only if immobile <see cref="MaxThrowingAngleDeg"/> and <see cref="MaxTurnAngleDeg"/> will be applied.
    /// </summary>
    [JsonProperty] public bool Immobile = false;

    /// <summary>
    /// Maximum angle in degrees between view direction and throwing direction.
    /// </summary>
    [JsonProperty] public float MaxThrowingAngleDeg = 0;

    /// <summary>
    /// Ignore checks for being able to start task, if was recently attacked.
    /// </summary>
    [JsonProperty] public bool RetaliateUnconditionally = true;

    /// <summary>
    /// Maximum angle in degrees for head to turn relative to <see cref="SpawnAngleDeg"/>.
    /// </summary>
    [JsonProperty] public float MaxTurnAngleDeg = 360;

    /// <summary>
    /// Entity head angle when spawned. Can be used to offset starting position that is used to restrict head movement via <see cref="MaxTurnAngleDeg"/>.
    /// </summary>
    [JsonProperty] public float SpawnAngleDeg = 0;

    /// <summary>
    /// Turning speed is taken from entities attributes 'pathfinder: { minTurnAnglePerSec: 250, maxTurnAnglePerSec: 450 }', if they are missing, these default values are used.
    /// </summary>
    [JsonProperty] public float DefaultMinTurnAngleDegPerSec = 250;

    /// <summary>
    /// Turning speed is taken from entities attributes 'pathfinder: { minTurnAnglePerSec: 250, maxTurnAnglePerSec: 450 }', if they are missing, these default values are used.
    /// </summary>
    [JsonProperty] public float DefaultMaxTurnAngleDegPerSec = 450;

    /// <summary>
    /// Projectile will be thrown at this time after task start.
    /// </summary>
    [JsonProperty] public int ThrowAtMs = 1000;

    /// <summary>
    /// Vertical range will be equal to horizontal range multiplied by this factor.<br/>
    /// For horizontal range standard <see cref="AiTaskBaseTargetable"/> is used.
    /// </summary>
    [JsonProperty] public float VerticalRangeFactor = 0.5f;

    /// <summary>
    /// Projectile code. Dont forget to specify domain.
    /// </summary>
    [JsonProperty] public AssetLocation ProjectileCode = new("thrownstone-{rock}");

    /// <summary>
    /// Projectile item stack.
    /// </summary>
    [JsonProperty] public AssetLocation ProjectileItem = new("stone-{rock}");

    /// <summary>
    /// If set to false, <see cref="ProjectileItem"/> can be collected from stuck projectile.
    /// </summary>
    [JsonProperty] public bool NonCollectible = true;

    /// <summary>
    /// Projectile damage.
    /// </summary>
    [JsonProperty] public float ProjectileDamage = 1f;

    /// <summary>
    /// Projectile damage tier.
    /// </summary>
    [JsonProperty] public int ProjectileDamageTier = 0;

    /// <summary>
    /// Projectile damage type.
    /// </summary>
    [JsonProperty] public EnumDamageType ProjectileDamageType = EnumDamageType.BluntAttack;

    /// <summary>
    /// Whether projectile should ignore invulnerability frames.
    /// </summary>
    [JsonProperty] public bool IgnoreInvFrames = true;

    /// <summary>
    /// Horizontal inaccuracy, in degrees.
    /// </summary>
    [JsonProperty] public float YawDispersionDeg = 0f;

    /// <summary>
    /// Vertical inaccuracy, in degrees.
    /// </summary>
    [JsonProperty] public float PitchDispersionDeg = 0f;

    /// <summary>
    /// Type of distribution used in 'NatFloat' to calculate dispersion.<br/>
    /// Average value is set to 0, and variance to 1. Then resulting float is scaled to dispersion values (<see cref="YawDispersionDeg"/> or <see cref="PitchDispersionDeg"/>).
    /// </summary>
    [JsonProperty] public EnumDistribution DispersionDistribution = EnumDistribution.GAUSSIAN;

    /// <summary>
    /// Horizontal inaccuracy, in degrees.
    /// </summary>
    [JsonProperty] public float MaxYawDispersionDeg = 0f;

    /// <summary>
    /// Vertical inaccuracy, in degrees.
    /// </summary>
    [JsonProperty] public float MaxPitchDispersionDeg = 0f;

    /// <summary>
    /// Dispersion will reduce from max value to min value on this amount of degrees at each throw at same entity.
    /// </summary>
    [JsonProperty] public float DispersionReductionSpeedDeg = 0f;

    /// <summary>
    /// If set to true, '{rock}' inside <see cref="ProjectileCode"/> will be replaced with local rock type.
    /// </summary>
    [JsonProperty] public bool ReplaceRockVariant = true;

    /// <summary>
    /// Chance for entity projectile to not be destroyed on impact.
    /// </summary>
    [JsonProperty] public float DropOnImpactChance = 1f;

    /// <summary>
    /// If set to true, projectile ItemStack durability will bre reduced by 1 on impact.
    /// </summary>
    [JsonProperty] public bool DamageStackOnImpact = false;

    /// <summary>
    /// Sound played when projectile is shot.
    /// </summary>
    [JsonProperty] public AssetLocation? ShootSound = null;

    /// <summary>
    /// In blocks per second.
    /// </summary>
    [JsonProperty] public double ProjectileSpeed = 10f;

    /// <summary>
    /// Have to specify it here, because it cant be retrieved from projectile entity itself, due to need to calculate and set velocity of the projectile before it is spawned,
    /// but 'EntityBehaviorPassivePhysics' initialized only after entity is spawned.<br/>
    /// </summary>
    [JsonProperty] public double ProjectileGravityFactor = 1f;



    public float MaxTurnAngleRad => MaxTurnAngleDeg * GameMath.DEG2RAD;

    public float SpawnAngleRad => SpawnAngleDeg * GameMath.DEG2RAD;

    public float MaxThrowingAngleRad => MaxThrowingAngleDeg * GameMath.DEG2RAD;

    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        if (ShootSound != null)
        {
            ShootSound = ShootSound.WithPathPrefixOnce("sounds/");
        }
    }
}

public class AiTaskShootAtEntityR : AiTaskBaseTargetableR
{
    protected float minTurnAnglePerSec;
    protected float maxTurnAnglePerSec;
    protected float currentTurnRadPerSec;
    protected bool alreadyThrown;
    protected long previousTargetId = 0;
    protected float currentYawDispersion;
    protected float currentPitchDispersion;

    protected const string defaultRockType = "granite";

    protected readonly NatFloat randomFloat;

    private AiTaskShootAtEntityConfig Config => GetConfig<AiTaskShootAtEntityConfig>();

    public AiTaskShootAtEntityR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskShootAtEntityConfig>(entity, taskConfig, aiConfig);

        randomFloat = new(0, 1, Config.DispersionDistribution);
    }

    public override bool ShouldExecute()
    {
        if (!(PreconditionsSatisficed() || Config.RetaliateUnconditionally && RecentlyAttacked)) return false;

        if (!CheckAndResetSearchCooldown()) return false;

        return SearchForTarget();
    }

    public override void StartExecute()
    {
        if (targetEntity == null) return;

        base.StartExecute();

        ITreeAttribute? pathfinder = entity.Properties.Server?.Attributes?.GetTreeAttribute("pathfinder");
        if (pathfinder != null)
        {
            minTurnAnglePerSec = pathfinder.GetFloat("minTurnAnglePerSec", Config.DefaultMinTurnAngleDegPerSec);
            maxTurnAnglePerSec = pathfinder.GetFloat("maxTurnAnglePerSec", Config.DefaultMaxTurnAngleDegPerSec);
        }
        else
        {
            minTurnAnglePerSec = Config.DefaultMinTurnAngleDegPerSec;
            maxTurnAnglePerSec = Config.DefaultMaxTurnAngleDegPerSec;
        }

        currentTurnRadPerSec = minTurnAnglePerSec + (float)Rand.NextDouble() * (maxTurnAnglePerSec - minTurnAnglePerSec);
        currentTurnRadPerSec *= GameMath.DEG2RAD;

        alreadyThrown = false;
    }

    public override bool ContinueExecute(float dt)
    {
        if (!base.ContinueExecute(dt)) return false;
        if (targetEntity == null) return false;

        AdjustYaw(dt);

        if (entity.World.ElapsedMilliseconds - executionStartTimeMs > Config.ThrowAtMs && !alreadyThrown)
        {
            SetOrAdjustDispersion();

            ShootProjectile();

            alreadyThrown = true;
        }

        return true;
    }

    protected override bool IsTargetableEntity(Entity target, float range)
    {
        if (!base.IsTargetableEntity(target, range)) return false;
        if (!HasDirectContact(target, range, range * Config.VerticalRangeFactor)) return false;
        if (!CanAimAt(target)) return false;

        return true;
    }

    protected virtual float GetAimYaw(Entity targetEntity)
    {
        FastVec3f targetVec = new();

        targetVec.Set(
            (float)(targetEntity.ServerPos.X - entity.ServerPos.X),
            (float)(targetEntity.ServerPos.Y - entity.ServerPos.Y),
            (float)(targetEntity.ServerPos.Z - entity.ServerPos.Z)
        );

        float desiredYaw = MathF.Atan2(targetVec.X, targetVec.Z);

        return desiredYaw;
    }

    protected virtual bool CanAimAt(Entity target)
    {
        if (!Config.Immobile) return true;

        float aimYaw = GetAimYaw(target);

        return aimYaw > Config.SpawnAngleRad - Config.MaxTurnAngleRad - Config.MaxThrowingAngleRad && aimYaw < Config.SpawnAngleRad + Config.MaxTurnAngleRad + Config.MaxThrowingAngleRad;
    }

    protected virtual AssetLocation ReplaceRockType(AssetLocation code)
    {
        if (!Config.ReplaceRockVariant) return code;

        AssetLocation codeCopy = code.Clone();
        string rockType = defaultRockType;
        IMapChunk mapChunk = entity.World.BlockAccessor.GetMapChunkAtBlockPos(entity.Pos.AsBlockPos);
        if (mapChunk != null)
        {
            int localZ = (int)entity.Pos.Z % GlobalConstants.ChunkSize;
            int localX = (int)entity.Pos.X % GlobalConstants.ChunkSize;
            Block rockBlock = entity.World.Blocks[mapChunk.TopRockIdMap[localZ * GlobalConstants.ChunkSize + localX]];
            rockType = rockBlock.Variant["rock"] ?? defaultRockType;
        }
        codeCopy.Path = codeCopy.Path.Replace("{rock}", rockType);

        return codeCopy;
    }

    protected virtual void SetOrAdjustDispersion()
    {
        if (targetEntity == null) return;

        if (targetEntity.EntityId == previousTargetId)
        {
            currentYawDispersion = MathF.Max(Config.YawDispersionDeg, currentYawDispersion - Config.DispersionReductionSpeedDeg);
            currentPitchDispersion = MathF.Max(Config.PitchDispersionDeg, currentPitchDispersion - Config.DispersionReductionSpeedDeg);
        }
        else
        {
            currentYawDispersion = MathF.Max(Config.MaxYawDispersionDeg, Config.YawDispersionDeg);
            currentPitchDispersion = MathF.Max(Config.MaxPitchDispersionDeg, Config.PitchDispersionDeg);
            previousTargetId = targetEntity.EntityId;
        }
    }

    protected virtual void AdjustYaw(float dt)
    {
        if (targetEntity == null) return;

        float desiredYaw = GetAimYaw(targetEntity);
        desiredYaw = GameMath.Clamp(desiredYaw, Config.SpawnAngleRad - Config.MaxTurnAngleRad, Config.SpawnAngleRad + Config.MaxTurnAngleRad);

        float yawDistance = GameMath.AngleRadDistance(entity.ServerPos.Yaw, desiredYaw);
        entity.ServerPos.Yaw += GameMath.Clamp(yawDistance, -currentTurnRadPerSec * dt, currentTurnRadPerSec * dt);
        entity.ServerPos.Yaw %= GameMath.TWOPI;
    }

    protected virtual void ShootProjectile()
    {
        if (targetEntity == null) return;

        CreateProjectile(out Entity projectileEntity, out IProjectile projectile, out Item? projectileItem);

        projectile.FiredBy = entity;
        projectile.Damage = Config.ProjectileDamage;
        projectile.DamageTier = Config.ProjectileDamageTier;
        projectile.DamageType = Config.ProjectileDamageType;
        projectile.IgnoreInvFrames = Config.IgnoreInvFrames;
        projectile.ProjectileStack = new ItemStack(projectileItem);
        projectile.NonCollectible = Config.NonCollectible;
        projectile.DropOnImpactChance = Config.DropOnImpactChance;
        projectile.DamageStackOnImpact = Config.DamageStackOnImpact;

        SetProjectilePositionAndVelocity(projectileEntity, projectile, Config.ProjectileGravityFactor, Config.ProjectileSpeed);

        entity.World.SpawnPriorityEntity(projectileEntity);

        if (Config.ShootSound != null)
        {
            entity.World.PlaySoundAt(Config.ShootSound, entity, null, Config.RandomizePitch, Config.SoundRange, Config.SoundVolume);
        }
    }

    protected virtual void CreateProjectile(out Entity projectileEntity, out IProjectile projectile, out Item projectileItem)
    {
        AssetLocation projectileCode = ReplaceRockType(Config.ProjectileCode);
        AssetLocation projectileItemCode = ReplaceRockType(Config.ProjectileItem);

        EntityProperties? type = entity.World.GetEntityType(projectileCode);
        if (type == null)
        {
            string message = $"Error while running '{Config.Code}' AI task for entity '{entity.Code}': projectile entity with code '{projectileCode}' does not exist";
            throw new ArgumentException(message);
        }

        Entity? createdEntity = entity.World.ClassRegistry.CreateEntity(type);

        if (createdEntity == null)
        {
            string message = $"Error while running '{Config.Code}' AI task for entity '{entity.Code}': unable to create entity with code '{projectileCode}'.";
            throw new ArgumentException(message);
        }

        if (createdEntity is not IProjectile createdProjectile)
        {
            string message = $"Error while running '{Config.Code}' AI task for entity '{entity.Code}': projectile entity '{projectileCode}' should have 'IProjectile' interface.";
            throw new ArgumentException(message);
        }

        projectile = createdProjectile;

        Item? createdProjectileItem = entity.World.GetItem(projectileItemCode);

        if (createdProjectileItem == null)
        {
            string message = $"Error while running '{Config.Code}' AI task for entity '{entity.Code}': projectile item '{projectileItemCode}' does not exist.";
            throw new ArgumentException(message);
        }

        projectileItem = createdProjectileItem;
        projectileEntity = createdEntity;
    }

    protected virtual void SetProjectilePositionAndVelocity(Entity projectileEntity, IProjectile projectile, double gravityFactor, double speed)
    {
        if (targetEntity == null) return;

        Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.LocalEyePos.Y, 0);
        Vec3d targetPos = targetEntity.ServerPos.XYZ.Add(0, targetEntity.LocalEyePos.Y, 0);

        float blocksPerSecondToBlocksPerMinute = 1 / 60f;

        speed *= blocksPerSecondToBlocksPerMinute;

        double gravityAcceleration = gravityFactor * GlobalConstants.GravityPerSecond * blocksPerSecondToBlocksPerMinute;

        double distance = (targetPos - pos).Length();
        double approximateTime = distance / speed;

        targetPos += targetEntity.ServerPos.Motion * approximateTime;

        //entity.World.SpawnParticles(1, ColorUtil.ColorFromRgba(0, 0, 255, 255), targetPos, targetPos, new Vec3f(), new Vec3f(), 3, 0, 1);

        FastVec3d start = new(pos.X, pos.Y, pos.Z);
        FastVec3d target = new(targetPos.X, targetPos.Y, targetPos.Z);
        FastVec3d velocity = new(0, 0, 0);

        bool solvedBallisticArc = false;
        for (int triesCount = 0; triesCount < 30; triesCount++)
        {
            solvedBallisticArc = SolveBallisticArc(out velocity, start, target, speed, gravityAcceleration);

            if (solvedBallisticArc) break;

            speed *= 1.1f;
        }

        if (!solvedBallisticArc)
        {
            FallBackVelocity(out velocity, start, target);
        }

        velocity = ApplyDispersionToVelocity(velocity, currentYawDispersion, currentPitchDispersion);

        projectileEntity.ServerPos.SetPosWithDimension(
            entity.ServerPos.BehindCopy(0.21).XYZ.Add(0, entity.LocalEyePos.Y, 0)
        );

        projectileEntity.ServerPos.Motion.Set(velocity.X, velocity.Y, velocity.Z);

        projectileEntity.Pos.SetFrom(projectileEntity.ServerPos);
        projectileEntity.World = entity.World;

        projectile.PreInitialize();
    }

    protected virtual bool SolveBallisticArc(out FastVec3d velocity, FastVec3d start, FastVec3d target, double speed, double acceleration)
    {
        Vector3d startVec = new(start.X, start.Y, start.Z);
        Vector3d targetVec = new(target.X, target.Y, target.Z);

        bool solved = SolveBallisticArc(out Vector3d velocityVec, startVec, targetVec, speed, acceleration);

        velocity = new(velocityVec.X, velocityVec.Y, velocityVec.Z);

        return solved;
    }

    protected virtual bool SolveBallisticArc(out Vector3d velocity, Vector3d start, Vector3d target, double speed, double acceleration) // @TODO add all the necessary functionality to FastVec3d, make FastVec2d, and refactor this using it
    {
        velocity = Vector3d.Zero;
        Vector3d delta = target - start;

        // Split into horizontal and vertical distances
        Vector2d deltaXZ = new Vector2d(delta.X, delta.Z);
        double horizontalDist = deltaXZ.Length;
        double verticalDist = delta.Y;

        double speedSq = speed * speed;
        double speed4 = speedSq * speedSq;

        double discriminant = speed4 - acceleration * (acceleration * horizontalDist * horizontalDist + 2 * verticalDist * speedSq);

        if (discriminant < 0f)
            return false; // No valid solution

        double sqrtDisc = Math.Sqrt(discriminant);

        // Low angle shot (use the minus root)
        double angle = Math.Atan2(speedSq - sqrtDisc, acceleration * horizontalDist);

        // Build the velocity vector
        double vy = speed * Math.Sin(angle);
        double horizontalSpeed = speed * Math.Cos(angle);

        Vector2d dirXZ = Vector2d.Normalize(deltaXZ);
        double vx = dirXZ.X * horizontalSpeed;
        double vz = dirXZ.Y * horizontalSpeed;

        velocity = new Vector3d(vx, vy, vz);
        return true;
    }

    protected virtual void FallBackVelocity(out FastVec3d velocity, FastVec3d start, FastVec3d target)
    {
        if (targetEntity == null)
        {
            velocity = new(0, 0, 0);
            return;
        }

        Vec3d pos = new(start.X, start.Y, start.Z);
        Vec3d targetPos = new(target.X, target.Y, target.Z);

        double distance = Math.Pow(pos.SquareDistanceTo(targetPos), 0.1);
        Vec3d velocityTemp = (targetPos - pos).Normalize() * GameMath.Clamp(distance - 1f, 0.1f, 1f);

        velocity = new(velocityTemp.X, velocityTemp.Y, velocityTemp.Z);
    }

    protected virtual FastVec3d ApplyDispersionToVelocity(FastVec3d velocity, float yawDispersionDeg, float pitchDispersionDeg)
    {
        Vector3d directionVector = Vector3d.Normalize(new Vector3d(velocity.X, velocity.Y, velocity.Z));
        Vector2 dispersionVector = new(yawDispersionDeg, pitchDispersionDeg);

        Vector3d direction = GetDirectionWithDispersion(directionVector, dispersionVector);

        double speed = velocity.Length();

        return new(direction.X * speed, direction.Y * speed, direction.Z * speed);
    }

    protected virtual Vector3d GetDirectionWithDispersion(Vector3d direction, Vector2 dispersionDeg) // @TODO add all the necessary functionality to FastVec3d and refactor this using it
    {
        float randomPitch = randomFloat.nextFloat() * dispersionDeg.Y * GameMath.DEG2RAD;
        float randomYaw = randomFloat.nextFloat() * dispersionDeg.X * GameMath.DEG2RAD;

        Vector3 verticalAxis = new(0, 0, 1);
        bool directionIsVertical = (verticalAxis - direction).Length < 1E9 || (verticalAxis + direction).Length < 1E9;
        if (directionIsVertical) verticalAxis = new(0, 1, 0);

        Vector3d forwardAxis = Vector3d.Normalize(direction);
        Vector3d yawAxis = Vector3d.Normalize(Vector3d.Cross(forwardAxis, verticalAxis));
        Vector3d pitchAxis = Vector3d.Normalize(Vector3d.Cross(yawAxis, forwardAxis));

        Vector3d yawComponent = yawAxis * Math.Tan(randomYaw);
        Vector3d pitchComponent = pitchAxis * Math.Tan(randomPitch);

        return Vector3d.Normalize(forwardAxis + yawComponent + pitchComponent);
    }
}
