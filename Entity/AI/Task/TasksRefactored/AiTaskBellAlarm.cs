using Newtonsoft.Json;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

/// <summary>
/// Spawns entities when target is detected.
/// 
/// <br/>
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2<br/>
/// - executionChance default value: 0.05 => 1.0<br/>
/// - seekingRange default value: 15 => 25<br/>
/// - spawnMobs => EntitiesToSpawn<br/>
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskBellAlarmConfig : AiTaskBaseTargetableConfig
{
    /// <summary>
    /// Entities from <see cref="entitiesToSpawn"/> will be spawned at random position inside this range.
    /// </summary>
    [JsonProperty] public int SpawnRange = 12;

    /// <summary>
    /// Entities will be spawned at random intervals from <see cref="SpawnIntervalMinMs"/> to <see cref="SpawnIntervalMaxMs"/>.
    /// </summary>
    [JsonProperty] public int SpawnIntervalMinMs = 1000;

    /// <summary>
    /// Entities will be spawned at random intervals from <see cref="SpawnIntervalMinMs"/> to <see cref="SpawnIntervalMaxMs"/>.
    /// </summary>
    [JsonProperty] public int SpawnIntervalMaxMs = 5000;

    /// <summary>
    /// Each times task spawn entities, it will spawn random amount from 1 to 6
    /// </summary>
    [JsonProperty] public int SpawnMaxQuantity = 6;

    /// <summary>
    /// For each player in <see cref="PlayerSpawnScaleRange"/> max number of entities spawned will increased by another <see cref="SpawnMaxQuantity"/> multiplied by this value and <see cref="IServerConfig.SpawnCapPlayerScaling"/>.
    /// </summary>
    [JsonProperty] public float PlayerScalingFactor = 1f;

    /// <summary>
    /// See <see cref="PlayerScalingFactor"/>.
    /// </summary>
    [JsonProperty] public float PlayerSpawnScaleRange = 15;

    /// <summary>
    /// List of entities that will be spawned.
    /// </summary>
    [JsonProperty] private AssetLocation[]? entitiesToSpawn = [];

    /// <summary>
    /// Sound that will be played on repeat during this task execution.
    /// </summary>
    [JsonProperty] public AssetLocation? RepeatSound = null;

    /// <summary>
    /// When task with this id is NOT active, detection range will be multiplied by <see cref="NotListeningRangeReduction"/>.
    /// </summary>
    [JsonProperty] public string ListenAiTaskId = "listen";

    /// <summary>
    /// See <see cref="ListenAiTaskId"/>.
    /// </summary>
    [JsonProperty] public float NotListeningRangeReduction = 0.5f;

    /// <summary>
    /// When player is not moving and not using hands, detection range will be multiplied by this factor.
    /// </summary>
    [JsonProperty] public float SilentSoundRangeReduction = 0.25f;

    /// <summary>
    /// When player is sneaking and not jumping or using hands, detection range will be multiplied by this factor.
    /// </summary>
    [JsonProperty] public float QuietSoundRangeReduction = 0.5f;

    /// <summary>
    /// Distance to target at which task will stop.
    /// </summary>
    [JsonProperty] public float MaxDistanceToTarget = 20;

    /// <summary>
    /// Will be set as 'origin' attribute in spawned entity.
    /// </summary>
    [JsonProperty] public string Origin = "bellalarm";



    public EntityProperties[] EntitiesToSpawn = [];

    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        if (entitiesToSpawn != null)
        {
            List<EntityProperties> properties = [];
            foreach (AssetLocation entityCode in entitiesToSpawn)
            {
                EntityProperties entityType = entity.World.GetEntityType(entityCode);
                if (entityType == null)
                {
                    entity.World.Logger.Warning($"AiTaskBellAlarm specified '{entityCode}' in 'EntitiesToSpawn', but no such entity type found, will ignore.");
                    continue;
                }

                properties.Add(entityType);
            }
            EntitiesToSpawn = [.. properties];
            entitiesToSpawn = null;
        }

        if (RepeatSound != null)
        {
            RepeatSound = RepeatSound.WithPathPrefixOnce("sounds/");
        }
    }
}

public class AiTaskBellAlarmR : AiTaskBaseTargetableR
{
    private AiTaskBellAlarmConfig Config => GetConfig<AiTaskBellAlarmConfig>();

    protected int nextSpawnIntervalMs;
    protected List<Entity> spawnedEntities = [];
    protected float timeSinceLastSpawnSec;
    protected CollisionTester collisionTester = new();

    public AiTaskBellAlarmR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskBellAlarmConfig>(entity, taskConfig, aiConfig);
    }

    public override bool ShouldExecute()
    {
        if (!PreconditionsSatisficed()) return false;

        return SearchForTarget();
    }

    public override void StartExecute()
    {
        if (Config.RepeatSound != null)
        {
            (entity.Api as ICoreServerAPI)?.Network.BroadcastEntityPacket(entity.EntityId, 1025, SerializerUtil.Serialize(Config.RepeatSound));
        }

        nextSpawnIntervalMs = Config.SpawnIntervalMinMs + entity.World.Rand.Next(Config.SpawnIntervalMaxMs - Config.SpawnIntervalMinMs);

        base.StartExecute();
    }

    public override bool ContinueExecute(float dt)
    {
        if (!base.ContinueExecute(dt)) return false;
        if (targetEntity == null) return false;

        timeSinceLastSpawnSec += dt;

        if (timeSinceLastSpawnSec * 1000f > nextSpawnIntervalMs)
        {
            if (entity.Api is not ICoreServerAPI api) return false;

            int numberOfPlayersInRange = entity.World.GetPlayersAround(entity.ServerPos.XYZ, Config.PlayerSpawnScaleRange, Config.PlayerSpawnScaleRange, player => player.Entity.Alive).Length;
            float playerScaling = 1 + (numberOfPlayersInRange - 1) * api.Server.Config.SpawnCapPlayerScaling * Config.PlayerScalingFactor;

            TrySpawnCreatures(GameMath.RoundRandom(Rand, Config.SpawnMaxQuantity * playerScaling - 1) + 1, Config.SpawnRange);

            nextSpawnIntervalMs = Config.SpawnIntervalMinMs + entity.World.Rand.Next(Config.SpawnIntervalMaxMs - Config.SpawnIntervalMinMs);

            timeSinceLastSpawnSec = 0;
        }

        if (targetEntity.Pos.SquareDistanceTo(entity.Pos) > Config.MaxDistanceToTarget * Config.MaxDistanceToTarget)
        {
            return false;
        }

        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        (entity.Api as ICoreServerAPI)?.Network.BroadcastEntityPacket(entity.EntityId, 1026); // stop alarm sound

        base.FinishExecute(cancelled);
    }

    public override void OnEntityDespawn(EntityDespawnData reason)
    {
        (entity.Api as ICoreServerAPI)?.Network.BroadcastEntityPacket(entity.EntityId, 1026);

        base.OnEntityDespawn(reason);
    }

    protected virtual void TrySpawnCreatures(int maxQuantity, int range) // @TODO refactor this mess
    {
        if (entity.Api is not ICoreServerAPI api) return;

        FastVec3d centerPos = new(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        Vec3d spawnPos = new();
        BlockPos spawnPosi = new(0);    // Omit dimension, because dimension will come from the InternalY being used in centerPos and spawnPos
        BlockPos spawnPosBuffer = new(0);

        for (int index = 0; index < spawnedEntities.Count; index++)
        {
            if (spawnedEntities[index] == null || !spawnedEntities[index].Alive)
            {
                spawnedEntities.RemoveAt(index);
                index--;
            }
        }

        if (Config.EntitiesToSpawn.Length == 0)
        {
            return;
        }

        if (spawnedEntities.Count > maxQuantity) return;

        int tries = 50;
        int spawned = 0;
        while (tries-- > 0 && spawned < 1)
        {
            int index = Rand.Next(Config.EntitiesToSpawn.Length);
            EntityProperties type = Config.EntitiesToSpawn[index];

            int rndx = api.World.Rand.Next(2 * range) - range;
            int rndy = api.World.Rand.Next(2 * range) - range;
            int rndz = api.World.Rand.Next(2 * range) - range;

            spawnPos.Set((int)centerPos.X + rndx + 0.5, (int)centerPos.Y + rndy + 0.001, (int)centerPos.Z + rndz + 0.5);

            spawnPosi.Set((int)spawnPos.X, (int)spawnPos.Y, (int)spawnPos.Z);

            while (api.World.BlockAccessor.GetBlockBelow(spawnPosi).Id == 0 && spawnPos.Y > 0)
            {
                spawnPosi.Y--;
                spawnPos.Y--;
            }

            spawnPosBuffer.Set((int)spawnPos.X, (int)spawnPos.Y, (int)spawnPos.Z);
            if (!api.World.BlockAccessor.IsValidPos(spawnPosBuffer)) continue;
            Cuboidf collisionBox = type.SpawnCollisionBox.OmniNotDownGrowBy(0.1f);
            if (collisionTester.IsColliding(api.World.BlockAccessor, collisionBox, spawnPos, false)) continue;

            DoSpawn(type, spawnPos, entity.HerdId);
            spawned++;
        }
    }

    protected virtual void DoSpawn(EntityProperties entityType, Vec3d spawnPosition, long herdId)
    {
        Entity entityToSpawn = entity.Api.ClassRegistry.CreateEntity(entityType);

        if (entityToSpawn is EntityAgent agentToSpawn) agentToSpawn.HerdId = herdId;

        entityToSpawn.ServerPos.SetPosWithDimension(spawnPosition);
        entityToSpawn.ServerPos.SetYaw((float)Rand.NextDouble() * GameMath.TWOPI);
        entityToSpawn.Pos.SetFrom(entityToSpawn.ServerPos);
        entityToSpawn.PositionBeforeFalling.Set(entityToSpawn.ServerPos.X, entityToSpawn.ServerPos.Y, entityToSpawn.ServerPos.Z);

        entityToSpawn.Attributes.SetString("origin", Config.Origin);

        entity.World.SpawnEntity(entityToSpawn);

        spawnedEntities.Add(entityToSpawn);
    }

    protected override bool CheckDetectionRange(Entity target, double range)
    {
        if (!base.CheckDetectionRange(target, range)) return false;

        if (target is not EntityPlayer player) return true;

        double distance = target.Pos.DistanceTo(entity.Pos.XYZ);
        
        bool usingHands = player.ServerControls.LeftMouseDown || player.ServerControls.RightMouseDown || player.ServerControls.HandUse != EnumHandInteract.None;
        bool moving = player.ServerControls.TriesToMove || player.ServerControls.Jump || !player.OnGround;
        bool silent = !usingHands && !moving;
        bool quiet = player.ServerControls.Sneak && !player.ServerControls.Jump && player.OnGround && !usingHands;

        if (silent)
        {
            range *= Config.SilentSoundRangeReduction;
        }
        else if (quiet)
        {
            range *= Config.QuietSoundRangeReduction;
        }

        return distance <= range;
    }

    protected override float GetSeekingRange()
    {
        float range = base.GetSeekingRange();

        bool listening = entity.GetBehavior<EntityBehaviorTaskAI>().TaskManager.IsTaskActive(Config.ListenAiTaskId);
        if (!listening)
        {
            range *= Config.NotListeningRangeReduction;
        }

        return range;
    }
}
