using Newtonsoft.Json;
using System;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Wanders<br/><br/>
/// 
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2<br/>
/// - wanderChance => executionChance (also default value: 0.02 => 1)
/// - targetDistance => minDistanceToTarget
/// </summary
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskWanderConfig : AiTaskBaseConfig
{
    /// <summary>
    /// If set to value greater than 0, and entity get further away from spawn than this distance, it will try to get back.<br/>
    /// If no players are in range <see cref="NoPlayersRange"/> and entity spent more than <see cref="TeleportToSpawnTimeout"/> time outside of the range, it will teleport to spawn.<br/>
    /// Entity will also try to not wander away from spawn further than this distance.
    /// </summary>
    [JsonProperty] public double MaxDistanceToSpawn = 0;

    /// <summary>
    /// If no players are in range <see cref="NoPlayersRange"/> and entity spent more than this time outside of the range <see cref="MaxDistanceToSpawn"/>, it will teleport to spawn.
    /// </summary>
    [JsonProperty] public int TeleportToSpawnTimeout = 1000 * 60 * 2;

    /// <summary>
    /// Entity will only teleport if there is no players in this range.
    /// </summary>
    [JsonProperty] public float NoPlayersRange = 15;

    /// <summary>
    /// Entity movement speed.
    /// </summary>
    [JsonProperty] public float MoveSpeed = 0.03f;

    /// <summary>
    /// Entity will consider that it reached its wonder target if entity is this close to the target.
    /// </summary>
    [JsonProperty] public float MinDistanceToTarget = 0.12f;

    /// <summary>
    /// IFf entity habitat is 'Air', it wont go this many blocks above the surface.
    /// </summary>
    [JsonProperty] public float MaxHeight = 7f;

    /// <summary>
    /// If higher or equal to 0, entity will prefer to wonder to blocks with light level closer to this value.
    /// </summary>
    [JsonProperty] public int PreferredLightLevel = -1;

    /// <summary>
    /// Type of light level used for <see cref="PreferredLightLevel"/>.
    /// </summary>
    [JsonProperty] public EnumLightLevelType PreferredLightLevelType = EnumLightLevelType.MaxTimeOfDayLight;

    /// <summary>
    /// Min horizontal range to search for wander target.
    /// </summary>
    [JsonProperty] private float wanderRangeMin = 3;

    /// <summary>
    /// Max horizontal range to search for wander target.
    /// </summary>
    [JsonProperty] private float wanderRangeMax = 30;

    /// <summary>
    /// Min vertical range to search for wander target.
    /// </summary>
    [JsonProperty] private float wanderVerticalRangeMin = 3;

    /// <summary>
    /// Max vertical range to search for wander target.
    /// </summary>
    [JsonProperty] private float wanderVerticalRangeMax = 10;

    /// <summary>
    /// If set to true, wander range will have 5% to be multiplied by 3, and additional 5% chance to be multiplied by another 1.5.
    /// Ask Tyron for why it is needed and is hardcoded. It will be turned this off by default.
    /// </summary>
    [JsonProperty] public bool DoRandomWanderRangeChanges = false;

    /// <summary>
    /// Max blocks checked before best one is selected. Block are selected at random, so you might want to increase this number for <see cref="PreferredLightLevel"/> check to be more accurate.
    /// </summary>
    [JsonProperty] public int MaxBlocksChecked = 9;


    public NatFloat WanderRangeHorizontal = new(0, 0, EnumDistribution.UNIFORM);

    public NatFloat WanderRangeVertical = new(0, 0, EnumDistribution.UNIFORM);

    public bool IgnoreLightLevel;

    public bool StayCloseToSpawn;



    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        WanderRangeHorizontal = new NatFloat(wanderRangeMin, wanderRangeMax, EnumDistribution.INVEXP);
        WanderRangeVertical = new NatFloat(wanderVerticalRangeMin, wanderVerticalRangeMax, EnumDistribution.INVEXP);
        StayCloseToSpawn = MaxDistanceToSpawn > 0;
        IgnoreLightLevel = PreferredLightLevel < 0;
    }
}

public class AiTaskWanderR : AiTaskBaseR
{
    private AiTaskWanderConfig Config => GetConfig<AiTaskWanderConfig>();

    protected float WanderRangeMul
    {
        get { return entity.Attributes.GetFloat("wanderRangeMul", 1); }
        set { entity.Attributes.SetFloat("wanderRangeMul", value); }
    }

    protected int FailedConsecutivePathfinds
    {
        get { return entity.Attributes.GetInt("failedConsecutivePathfinds", 0); }
        set { entity.Attributes.SetInt("failedConsecutivePathfinds", value); }
    }

    /// <summary>
    /// Increases when get stuck.
    /// </summary>
    protected int failedWanders = 0;
    protected long lastTimeInRangeMs = 0;
    protected bool spawnPositionSet = false;
    protected Vec3d mainTarget = new();
    protected Vec3d spawnPosition = new();
    protected bool needsToTeleport = false;

    #region Variables to reduce heap allocations in LoadNextWanderTarget method cause we dont use structs
    private readonly Vec4d bestTarget = new();
    private readonly Vec4d currentTarget = new();
    private readonly BlockPos blockPosBuffer = new(0);
    #endregion


    public AiTaskWanderR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskWanderConfig>(entity, taskConfig, aiConfig);

        spawnPosition = new Vec3d(entity.Attributes.GetDouble("spawnX"), entity.Attributes.GetDouble("spawnY"), entity.Attributes.GetDouble("spawnZ"));
    }

    public override bool ShouldExecute()
    {
        if (!PreconditionsSatisficed()) return false;

        failedWanders = 0;
        needsToTeleport = false;

        if (!Config.StayCloseToSpawn)
        {
            return LoadNextWanderTarget();
        }

        long currentTimeMs = entity.World.ElapsedMilliseconds;
        double distanceToSpawnSquared = entity.ServerPos.XYZ.SquareDistanceTo(spawnPosition.X, spawnPosition.Y, spawnPosition.Z);

        if (distanceToSpawnSquared <= Config.MaxDistanceToSpawn * Config.MaxDistanceToSpawn)
        {
            lastTimeInRangeMs = currentTimeMs;
            return false;
        }

        // If after 2 minutes still not at spawn and no player nearby, teleport
        if (currentTimeMs - lastTimeInRangeMs > Config.TeleportToSpawnTimeout && entity.World.GetNearestEntity(entity.ServerPos.XYZ, Config.NoPlayersRange, Config.NoPlayersRange, target => target is EntityPlayer) == null)
        {
            needsToTeleport = true;
        }

        mainTarget = spawnPosition;
        return true;
    }

    public override void StartExecute()
    {
        base.StartExecute();

        if (needsToTeleport)
        {
            entity.TeleportTo((int)spawnPosition.X, (int)spawnPosition.Y, (int)spawnPosition.Z);
            stopTask = true;
            return;
        }

        pathTraverser.WalkTowards(mainTarget, Config.MoveSpeed, Config.MinDistanceToTarget, OnGoalReached, OnStuck);
    }

    public override bool ContinueExecute(float dt)
    {
        if (!base.ContinueExecute(dt)) return false;

        /*
        // Commented out, this fix seems not to work anyway, @TODO look into this bug
        // We have a bug with the animation sync server->client where the wander right after spawn is not synced. this is a workaround
        if (animationMeta != null && tryStartAnimAgain > 0 && (tryStartAnimAgain -= dt) <= 0)
        {
            entity.AnimManager.StartAnimation(animationMeta);
        }
        */

        // If we are a climber dude and encountered a wall, let's not try to get behind the wall
        // We do that by removing the coord component that would make the entity want to walk behind the wall
        if (entity.Controls.IsClimbing && entity.Properties.CanClimbAnywhere && entity.ClimbingIntoFace != null)
        {
            BlockFacing facing = entity.ClimbingIntoFace;

            if (Math.Sign(facing.Normali.X) == Math.Sign(pathTraverser.CurrentTarget.X - entity.ServerPos.X))
            {
                pathTraverser.CurrentTarget.X = entity.ServerPos.X;
            }

            if (Math.Sign(facing.Normali.Y) == Math.Sign(pathTraverser.CurrentTarget.Y - entity.ServerPos.Y))
            {
                pathTraverser.CurrentTarget.Y = entity.ServerPos.Y;
            }

            if (Math.Sign(facing.Normali.Z) == Math.Sign(pathTraverser.CurrentTarget.Z - entity.ServerPos.Z))
            {
                pathTraverser.CurrentTarget.Z = entity.ServerPos.Z;
            }
        }

        if (mainTarget.HorizontalSquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Z) < 0.5)
        {
            pathTraverser.Stop();
            return false;
        }

        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);

        if (cancelled)
        {
            pathTraverser.Stop();
        }
    }

    public override void OnEntityLoaded()
    {
        if (!entity.Attributes.HasAttribute("spawnX"))
        {
            OnEntitySpawn();
        }
    }

    public override void OnEntitySpawn()
    {
        entity.Attributes.SetDouble("spawnX", entity.ServerPos.X);
        entity.Attributes.SetDouble("spawnY", entity.ServerPos.Y);
        entity.Attributes.SetDouble("spawnZ", entity.ServerPos.Z);
        spawnPosition.Set(entity.ServerPos.XYZ);
    }

    /// <summary>
    /// If a wander failed (got stuck) initially greatly increase the chance of trying again, but eventually give up
    /// </summary>
    /// <returns></returns>
    protected override bool CheckExecutionChance()
    {
        return Rand.NextDouble() <= (failedWanders > 0 ? (1 - Config.ExecutionChance * 4 * failedWanders) : Config.ExecutionChance);
    }

    // Requirements:
    // - ✔ Try to not move a lot vertically
    // - ✔ If territorial: Stay close to the spawn point
    // - ✔ If air habitat: Don't go above maxHeight blocks above surface
    // - ✔ If land habitat: Don't walk into water, prefer surface
    // - ~~If cave habitat: Prefer caves~~
    // - ✔ If water habitat: Don't walk onto land
    // - ✔ Try not to fall from very large heights. Try not to fall from any large heights if entity has FallDamage
    // - ✔ Prefer preferredLightLevel
    // - ✔ If land habitat: Must be above a block the entity can stand on
    // - ✔ if failed searches is high, reduce wander range
    protected virtual bool LoadNextWanderTarget()
    {
        EnumHabitat habitat = entity.Properties.Habitat;

        bool targetFound = false;

        if (FailedConsecutivePathfinds > 10)
        {
            WanderRangeMul = Math.Max(0.1f, WanderRangeMul * 0.9f);
        }
        else
        {
            WanderRangeMul = Math.Min(1, WanderRangeMul * 1.1f);
            if (Config.DoRandomWanderRangeChanges && Rand.NextDouble() < 0.05)
            {
                WanderRangeMul = Math.Min(1, WanderRangeMul * 1.5f);
            }
        }

        float wRangeMul = WanderRangeMul;
        double dx, dy, dz;

        if (Config.DoRandomWanderRangeChanges && Rand.NextDouble() < 0.05) wRangeMul *= 3;

        for (int check = 0; check < Config.MaxBlocksChecked; check++)
        {
            dx = Config.WanderRangeHorizontal.nextFloat() * (Rand.Next(2) * 2 - 1) * wRangeMul;
            dy = Config.WanderRangeVertical.nextFloat() * (Rand.Next(2) * 2 - 1) * wRangeMul;
            dz = Config.WanderRangeHorizontal.nextFloat() * (Rand.Next(2) * 2 - 1) * wRangeMul;

            currentTarget.X = entity.ServerPos.X + dx;
            currentTarget.Y = entity.ServerPos.InternalY + dy;
            currentTarget.Z = entity.ServerPos.Z + dz;
            currentTarget.W = 1;

            if (Config.StayCloseToSpawn)
            {
                double distToEdge = currentTarget.SquareDistanceTo(spawnPosition) / (Config.MaxDistanceToSpawn * Config.MaxDistanceToSpawn);
                // Prefer staying close to spawn
                currentTarget.W = 1 - distToEdge;
            }

            Block? waterOrIceBlock = null;

            switch (habitat)
            {
                case EnumHabitat.Air:
                    int rainMapY = world.BlockAccessor.GetRainMapHeightAt((int)currentTarget.X, (int)currentTarget.Z);
                    // Don't fly above max height
                    currentTarget.Y = Math.Min(currentTarget.Y, rainMapY + Config.MaxHeight);

                    // Cannot be in water
                    waterOrIceBlock = entity.World.BlockAccessor.GetBlockRaw((int)currentTarget.X, (int)currentTarget.Y, (int)currentTarget.Z, BlockLayersAccess.Fluid);
                    if (waterOrIceBlock.IsLiquid()) currentTarget.W = 0;
                    break;

                case EnumHabitat.Land:
                    currentTarget.Y = MoveDownToFloor((int)currentTarget.X, currentTarget.Y, (int)currentTarget.Z);
                    // No floor found
                    if (currentTarget.Y < 0)
                    {
                        currentTarget.W = 0;
                    }
                    else
                    {
                        // Does not like water
                        waterOrIceBlock = entity.World.BlockAccessor.GetBlockRaw((int)currentTarget.X, (int)currentTarget.Y, (int)currentTarget.Z, BlockLayersAccess.Fluid);
                        if (waterOrIceBlock.IsLiquid()) currentTarget.W /= 2;

                        // Lets make a straight line plot to see if we would fall off a cliff
                        bool stop = false;
                        bool willFall = false;

                        float angleHor = (float)Math.Atan2(dx, dz) + GameMath.PIHALF;

                        Vec3d target1BlockAhead = new Vec3d(currentTarget.X, currentTarget.Y, currentTarget.Z).Ahead(1, 0, angleHor);
                        Vec3d startAhead = new Vec3d(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(1, 0, angleHor); // Otherwise they are forever stuck if they stand over the edge

                        int prevY = (int)startAhead.Y;

                        GameMath.BresenHamPlotLine2d((int)startAhead.X, (int)startAhead.Z, (int)target1BlockAhead.X, (int)target1BlockAhead.Z, (x, z) =>
                        {
                            if (stop) return;

                            double nowY = MoveDownToFloor(x, prevY, z);

                            // Not more than 4 blocks down
                            if (nowY < 0 || prevY - nowY > 4)
                            {
                                willFall = true;
                                stop = true;
                            }

                            // Not more than 2 blocks up
                            if (nowY - prevY > 2)
                            {
                                stop = true;
                            }

                            prevY = (int)nowY;
                        });

                        if (willFall) currentTarget.W = 0;

                    }
                    break;

                case EnumHabitat.Sea:
                    waterOrIceBlock = entity.World.BlockAccessor.GetBlockRaw((int)currentTarget.X, (int)currentTarget.Y, (int)currentTarget.Z, BlockLayersAccess.Fluid);
                    if (!waterOrIceBlock.IsLiquid()) currentTarget.W = 0;
                    break;

                case EnumHabitat.Underwater:
                    waterOrIceBlock = entity.World.BlockAccessor.GetBlockRaw((int)currentTarget.X, (int)currentTarget.Y, (int)currentTarget.Z, BlockLayersAccess.Fluid);
                    if (!waterOrIceBlock.IsLiquid()) currentTarget.W = 0;
                    else currentTarget.W = 1 / (Math.Abs(dy) + 1);  //prefer not too much vertical change when underwater

                    //TODO: reject (or de-weight) targets not in direct line of sight (avoiding terrain)

                    break;
            }

            if (currentTarget.W > 0)
            {
                // Try to not hug the wall so much
                for (int i = 0; i < BlockFacing.HORIZONTALS.Length; i++)
                {
                    if (entity.World.BlockAccessor.IsSideSolid((int)currentTarget.X + BlockFacing.HORIZONTALS[i].Normali.X, (int)currentTarget.Y, (int)currentTarget.Z + BlockFacing.HORIZONTALS[i].Normali.Z, BlockFacing.HORIZONTALS[i].Opposite))
                    {
                        currentTarget.W *= 0.5;
                    }
                }
            }

            if (!Config.IgnoreLightLevel && currentTarget.W != 0)
            {
                blockPosBuffer.Set((int)currentTarget.X, (int)currentTarget.Y, (int)currentTarget.Z);
                int lightDiff = Math.Abs(Config.PreferredLightLevel - entity.World.BlockAccessor.GetLightLevel(blockPosBuffer, Config.PreferredLightLevelType));

                currentTarget.W /= Math.Max(1, lightDiff);

                //Debug.WriteLine($"{check}: {entity.World.BlockAccessor.GetLightLevel(blockPosBuffer, Config.PreferredLightLevelType)}  ({entity.World.BlockAccessor.GetBlock(blockPosBuffer).Code}) - {currentTarget} - {Config.PreferredLightLevelType}");
            }

            if (!targetFound || currentTarget.W > bestTarget.W)
            {
                targetFound = true;
                bestTarget.Set(currentTarget.X, currentTarget.Y, currentTarget.Z, currentTarget.W);
                if (currentTarget.W >= 1.0) break;  //have a good enough target, no need for further tries
            }
        }

        if (bestTarget.W > 0)
        {
            //blockPosBuffer.Set((int)bestTarget.X, (int)bestTarget.Y, (int)bestTarget.Z);
            //Debug.WriteLine($"Best: {entity.World.BlockAccessor.GetLightLevel(blockPosBuffer, Config.PreferredLightLevelType)} ({entity.World.BlockAccessor.GetBlock(blockPosBuffer).Code}) - {bestTarget}");

            FailedConsecutivePathfinds = Math.Max(FailedConsecutivePathfinds - 3, 0);

            mainTarget.Set(bestTarget.X, bestTarget.Y, bestTarget.Z);
            return true;
        }

        FailedConsecutivePathfinds++;
        return false;
    }

    protected virtual double MoveDownToFloor(int x, double y, int z)
    {
        int tries = 5;
        while (tries-- > 0)
        {
            if (world.BlockAccessor.IsSideSolid(x, (int)y, z, BlockFacing.UP)) return y + 1;
            y--;
        }

        return -1;
    }

    protected virtual void OnStuck()
    {
        stopTask = true;
        failedWanders++;
    }

    protected virtual void OnGoalReached()
    {
        stopTask = true;
        failedWanders = 0;
    }
}
