using System;
using System.Diagnostics.CodeAnalysis;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent;

/// <summary>
/// Represents a single entity remembered by an entity.
/// A timestamp of -1 means the entity has never been recorded in that timestamp's context.
/// </summary>
public struct EntityMemory()
{
    public long EntityId;
    public FastVec3d LastKnownPosition;
    public long LastUpdatedMs = -1;
    public long ForgetAtMs = -1;
    public long SawAtMs = -1;
    public long HeardAtMs = -1;
    public long SmelledAtMs = -1;
    public long TouchedAtMs = -1;
    public long ObservedByAtMs = -1;
    public long ThreatenedByAtMs = -1;
}

public record TargetEntity
{
    public required Entity Entity { get; init; }
    public FastVec3d LastKnownPosition { get; set; }
    public long SetAtMs { get; init; }
    public long LockUntilMs { get; init; }
    public long ClearAtMs { get; set; }
}

public record TargetBlock
{
    public Block Block { get; init; }
    public BlockPos Position { get; init; }
}

/// <summary>
/// Provides a way of recording and querying entities remembered by this entity.
/// Memories are intended to be updated via State Trees, i.e. other systems should treat them as read-only.
/// </summary>
public class EntityBehaviorMemory(Entity entity) : EntityBehavior(entity)
{
    public override string PropertyName() => "memory";

    private readonly IServerWorldAccessor world = (IServerWorldAccessor)entity.World;

    public enum Category
    {
        Saw,
        Heard,
        Smelled,
        Touched,
        ObservedBy,
        AttackedBy,
        Any,
    }

    /// <summary>
    /// The maximum number of unique entities remembered at once. When exceeded, the entity
    /// that should be forgotten soonest is evicted.
    /// </summary>
    public const int Capacity = 16;

    private const long RememberForMs = 10000;

    public static readonly Category[] AllCategories =
    [
        Category.Saw,
        Category.Heard,
        Category.Smelled,
        Category.Touched,
        Category.ObservedBy,
        Category.AttackedBy
    ];

    private readonly EntityMemory[] memories = new EntityMemory[Capacity];
    private int count;
    private readonly Random random = new();

    public TargetEntity? TargetEntity { get; private set; }

    public TargetBlock? TargetBlock { get; private set; }

    public override void OnGameTick(float deltaTime)
    {
        base.OnGameTick(deltaTime);

        var now = world.GetTotalUnpausedTimeMs();

        var i = 0;
        while (i < count)
        {
            var memory = memories[i];
            if (memory.ForgetAtMs <= now || world.GetEntityById(memory.EntityId) is not { Alive: true })
            {
                RemoveAt(i);
            }
            else
            {
                i++;
            }
        }

        if (TargetEntity != null && TargetEntity.ClearAtMs <= world.GetTotalUnpausedTimeMs())
        {
            ClearTargetEntity();
        }
    }

    public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
    {
        base.OnEntityReceiveDamage(damageSource, ref damage);
        Record(Category.AttackedBy, damageSource.GetCauseEntity());
    }

    /// <summary>
    /// Record entities. See <see cref="Record(Category, Entity, long)"/>.
    /// </summary>
    public void Record(Category category, Entity[] entities, long rememberForMs = RememberForMs)
    {
        if (entities.Length == 0 || category == Category.Any) return;

        var now = world.GetTotalUnpausedTimeMs();
        foreach (var target in entities)
        {
            Record(category, target, now, rememberForMs);
        }
    }

    /// <summary>
    /// Record an entity. If a memory for the entity already exists, its timestamps will be updated.
    /// </summary>
    /// <param name="category">The category to record against. Must not be <see cref="Category.Any"/>.</param>
    /// <param name="entity">The entity to record.</param>
    /// <param name="rememberForMs">How long the memory should last in milliseconds (defaults to <see cref="RememberForMs"/>).</param>
    public void Record(Category category, Entity entity, long rememberForMs = RememberForMs)
    {
        if (category == Category.Any) return;

        var now = world.GetTotalUnpausedTimeMs();
        Record(category, entity, now, rememberForMs);
    }

    private void Record(Category category, Entity entity, long now, long rememberForMs = RememberForMs)
    {
        if (entity is not { Alive: true }) return;

        var index = IndexOf(entity.EntityId);
        if (index < 0)
        {
            if (count == Capacity)
            {
                var evictee = 0;
                for (var i = 1; i < count; i++)
                {
                    if (memories[i].ForgetAtMs < memories[evictee].ForgetAtMs) evictee = i;
                }

                RemoveAt(evictee);
            }

            index = count++;
            memories[index] = new EntityMemory { EntityId = entity.EntityId };
        }

        memories[index].LastUpdatedMs = now;
        memories[index].ForgetAtMs = now + rememberForMs;
        memories[index].LastKnownPosition = entity.Pos.XYZFast;

        if (TargetEntity != null && TargetEntity.Entity.EntityId == entity.EntityId)
        {
            TargetEntity.LastKnownPosition = memories[index].LastKnownPosition;
        }

        switch (category)
        {
            case Category.Saw: memories[index].SawAtMs = now; break;
            case Category.Heard: memories[index].HeardAtMs = now; break;
            case Category.Smelled: memories[index].SmelledAtMs = now; break;
            case Category.Touched: memories[index].TouchedAtMs = now; break;
            case Category.ObservedBy: memories[index].ObservedByAtMs = now; break;
            case Category.AttackedBy: memories[index].ThreatenedByAtMs = now; break;
        }
    }

    /// <summary>
    /// Count memories of the given category.
    /// </summary>
    public int Count(Category category)
    {
        var total = 0;
        for (var i = 0; i < count; i++)
        {
            if (GetTimestamp(in memories[i], category) > -1) total++;
        }

        return total;
    }

    /// <summary>
    /// Count memories.
    /// <param name="category">The category to count from.</param>
    /// <param name="maxAgeMs">The oldest countable memories, in milliseconds.</param>
    /// <param name="tags">Tag condition that must be met for each memory.</param>
    /// </summary>
    public int Count(Category category, long maxAgeMs, ComplexTagCondition<TagSetFast> tags)
    {
        var now = world.GetTotalUnpausedTimeMs();

        var total = 0;
        for (var i = 0; i < count; i++)
        {
            if (IsRecent(GetTimestamp(in memories[i], category), now, maxAgeMs))
            {
                var e = world.GetEntityById(memories[i].EntityId);
                if (tags.Matches(e.Tags)) total++;
            }
        }

        return total;
    }

    /// <summary>
    /// Get the most recently recorded entity memory from the given category, or null if none exists.
    /// </summary>
    public EntityMemory? Newest(Category category)
    {
        var now = world.GetTotalUnpausedTimeMs();

        EntityMemory? newest = null;
        long newestTimestamp = 0;

        for (var i = 0; i < count; i++)
        {
            var timestamp = GetTimestamp(in memories[i], category);
            if (timestamp > newestTimestamp && IsRecent(timestamp, now, RememberForMs))
            {
                newestTimestamp = timestamp;
                newest = memories[i];
            }
        }

        return newest;
    }

    /// <summary>
    /// Get a random entity memory from the given category, or null if none exists.
    /// </summary>
    public EntityMemory? Random(Category category = Category.Any)
    {
        var now = world.GetTotalUnpausedTimeMs();

        EntityMemory? picked = null;
        var seen = 0;

        for (var i = 0; i < count; i++)
        {
            if (!IsRecent(GetTimestamp(in memories[i], category), now, RememberForMs))
            {
                continue;
            }

            if (random.Next(seen++) == 0)
            {
                picked = memories[i];
            }
        }

        return picked;
    }

    public Entity? GetTargetEntity()
    {
        return TargetEntity?.Entity;
    }

    /// <summary>
    /// Set the current target entity.
    /// </summary>
    /// <param name="target">The remembered entity to target</param>
    /// <param name="lockDurationMs">How long the target should remain locked. Calls to set the target will be ignored until this time has elapsed.</param>
    /// <param name="clearAfterMs">How long until the target should be cleared (nulled).</param>
    public void SetTargetEntity(EntityMemory target, long lockDurationMs = 2000, long clearAfterMs = 10000)
    {
        var elapsedMs = world.GetTotalUnpausedTimeMs();

        if (TargetEntity != null)
        {
            if (TargetEntity.Entity.EntityId == target.EntityId)
            {
                TargetEntity.ClearAtMs = elapsedMs + clearAfterMs;
            }
            else if (TargetEntity.LockUntilMs > elapsedMs)
            {
                return;
            }
        }

        TargetEntity = new TargetEntity
        {
            Entity = world.GetEntityById(target.EntityId),
            LastKnownPosition = target.LastKnownPosition,
            SetAtMs = elapsedMs,
            LockUntilMs = elapsedMs + lockDurationMs,
            ClearAtMs = elapsedMs + clearAfterMs
        };
    }

    public void ClearTargetEntity()
    {
        TargetEntity = null;
    }

    public void SetTargetBlock(Block block, BlockPos position)
    {
        TargetBlock = new TargetBlock
        {
            Block = block,
            Position = position
        };
    }

    public void ClearTargetBlock()
    {
        TargetBlock = null;
    }

    private int IndexOf(long entityId)
    {
        for (var i = 0; i < count; i++)
        {
            if (memories[i].EntityId == entityId) return i;
        }

        return -1;
    }

    private void RemoveAt(int index)
    {
        count--;
        memories[index] = memories[count];
        memories[count] = default;
    }

    private static long GetTimestamp(in EntityMemory memory, Category category)
    {
        return category switch
        {
            Category.Saw => memory.SawAtMs,
            Category.Heard => memory.HeardAtMs,
            Category.Smelled => memory.SmelledAtMs,
            Category.Touched => memory.TouchedAtMs,
            Category.ObservedBy => memory.ObservedByAtMs,
            Category.AttackedBy => memory.ThreatenedByAtMs,
            Category.Any => memory.LastUpdatedMs,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };
    }

    private static bool IsRecent(long timestamp, long now, long maxAgeMs)
    {
        return timestamp != 0 && now - timestamp <= maxAgeMs && now - timestamp <= RememberForMs;
    }

    public static bool TryGet(Entity entity, [NotNullWhen(true)] out EntityBehaviorMemory? behavior)
    {
        behavior = entity.GetBehavior<EntityBehaviorMemory>();
        if (behavior == null)
        {
            entity.World.Logger.Warning($"Failed to retrieve EntityBehaviorMemory for entity '{entity.Code}'");
            return false;
        }

        return true;
    }
}
