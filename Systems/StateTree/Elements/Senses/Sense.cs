using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

public abstract class Sense(Entity entity, bool excludeInanimateEntities)
{
    protected readonly int DebugDrawColorPositive = ColorUtil.ColorFromRgba(0, 255, 0, 255);
    protected readonly int DebugDrawColorNegative = ColorUtil.ColorFromRgba(255, 0, 0, 255);
    protected readonly TimeSpan DebugDrawDuration = TimeSpan.FromSeconds(3);

    protected Entity self { get; } = entity;
    private static TagSetFast inanimateTagSet;

    public static void Initialize(ICoreAPI api)
    {
        api.EntityTagRegistry.TryCreateTagSet(out inanimateTagSet, "inanimate");
    }

    /// <summary>
    /// Returns the sense's maximum range.
    /// </summary>
    public abstract float GetRange();

    /// <summary>
    /// Checks each of the provided entities against this sense, recording them to memory as needed.
    /// </summary>
    /// <param name="entities">The entities to check.</param>
    /// <param name="memory">The host entity's memory.</param>
    public abstract void Check(Entity[] entities, EntityBehaviorMemory memory);

    /// <summary>
    /// Determines whether this sense can sense the given entity.
    /// </summary>
    /// <param name="other">The entity being sensed.</param>
    protected virtual bool CanSense(Entity other)
    {
        return other != self
               && other.Pos.DistanceTo(self.Pos) < GetRange()
               && (!excludeInanimateEntities || !IsInanimate(other));
    }

    public static bool IsInanimate(Entity entity)
    {
        return entity.Tags.Overlaps(inanimateTagSet) || entity is not EntityAgent;
    }
}
