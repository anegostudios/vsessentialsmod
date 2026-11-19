using System;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Simulates sight via a cone. This is primarily designed for players to notify NPCs when they're seen, since NPCs have
/// their own sight sense via state trees.
/// </summary>
public class EntityBehaviorSight(Entity entity) : EntityBehavior(entity)
{
    public override string PropertyName() => "sight";

    private float sightDistance = 0f;
    private float sightAngleDeg = 0f;
    private bool enableDebugDraw = false;

    private readonly DebugDrawSystem debugDrawSystem = entity.Api.ModLoader.GetModSystem<DebugDrawSystem>();
    private long? debugDrawHandle;
    private readonly int debugDrawColorPositive = ColorUtil.ColorFromRgba(0, 255, 0, 255);
    private readonly int debugDrawColorNegative = ColorUtil.ColorFromRgba(255, 0, 0, 255);

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        sightDistance = properties.Attributes["sightDistance"].AsFloat();
        sightAngleDeg = properties.Attributes["sightAngleDeg"].AsFloat();
        enableDebugDraw = attributes["enableDebugDraw"].AsBool();
        if (enableDebugDraw) debugDrawHandle = debugDrawSystem?.RequestHandle();

        entity.World.RegisterGameTickListener(OnUpdateInterval, 50);
    }

    private void OnUpdateInterval(float deltaTime)
    {
        if (sightDistance <= 0f || sightAngleDeg <= 0f)
            return;

        var eyePos = (entity.Pos.XYZ + entity.LocalEyePos).ToVec3f() - entity.Pos.AsBlockPos.ToVec3f();
        var lookDir = entity.Pos.GetViewVector().Normalize().ToVec3d();

        var coneOrigin = entity.Pos.XYZ + entity.LocalEyePos;
        var coneHalfAngleRad = (sightAngleDeg / 2f) * GameMath.DEG2RAD;

        var saw = entity.World.GetEntitiesAround(coneOrigin, sightDistance, sightDistance,
            nearbyEntity =>
            {
                if (nearbyEntity == entity || nearbyEntity is IProjectile) return false;

                if (nearbyEntity.Pos.DistanceTo(coneOrigin) > sightDistance) return false;

                Vec3d targetCenter = nearbyEntity.Pos.XYZ + nearbyEntity.SelectionBox.Center;

                var targetVector = targetCenter - coneOrigin;
                var inCone = targetVector.Normalize().Dot(lookDir) >= Math.Cos(coneHalfAngleRad);

                if (!inCone) return false;

                return !nearbyEntity.HasBehavior<EntityBehaviorVisibility>() || nearbyEntity.GetBehavior<EntityBehaviorVisibility>()!.IsVisibleTo(entity);
            });

        foreach (var e in saw)
        {
            if (e.EntityId == entity.EntityId) continue;

            // TODO: consider caching this behavior per entity since this is a hot path
            if (e.HasBehavior<EntityBehaviorMemory>())
            {
                var memory = e.GetBehavior<EntityBehaviorMemory>();
                memory!.Record(EntityBehaviorMemory.Category.ObservedBy, entity);
            }
        }

        if (debugDrawHandle.HasValue)
        {
            debugDrawSystem?.RequestDebugDrawCone(new DebugDrawCone
            {
                Handle = (long)debugDrawHandle,
                BlockPosition = entity.Pos.AsBlockPos,
                Axis = lookDir,
                RelativeOrigin = eyePos,
                Length = sightDistance,
                HalfAngleRad = coneHalfAngleRad,
                Color = saw.Length > 0 ? debugDrawColorPositive : debugDrawColorNegative
            });
        }
    }
}
