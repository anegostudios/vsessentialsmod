using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Action that applies to entities hit by ray trace(s).
/// </summary>
public class RayAction(
    Entity abilityUser,
    IList<IEffect> onHitEffects,
    Vec3d startPositionOffset,
    Vec3d startDirectionOffset,
    float distance,
    float halfHorizAngleRad,
    float halfVertAngleRad,
    int rowCount,
    int rayCount,
    DebugDrawSystem debugDrawSystem
) : AbilityAction(abilityUser, onHitEffects)
{
    private readonly List<long> debugDrawHandles = debugDrawSystem?.RequestHandles(rayCount);

    private EntitySelection? entitySelection = new();

    public override void Run(ITargetSelector targetSelector)
    {
        base.Run(targetSelector);

        if (rayCount < 1)
            return;

        var target = targetSelector.GetTarget();

        var worldOrigin = target.Pos.XYZ + target.LocalEyePos + startPositionOffset;
        var localOrigin = worldOrigin - target.Pos.AsBlockPos.ToVec3d();

        Vec3d targetForward = target.Pos.GetViewVector().NormalizedCopy().ToVec3d() * -1d;

        if (rayCount == 1)
        {
            var rayVector = (targetForward + startDirectionOffset) * distance;
            var to = worldOrigin + rayVector;
            var blockSelection = new BlockSelection();
            target.World.RayTraceForSelection(worldOrigin, to, ref blockSelection, ref entitySelection);

            debugDrawSystem?.RequestDebugDrawLine(new DebugDrawLine
            {
                Handle = debugDrawHandles[0],
                BlockPosition = target.Pos.AsBlockPos,
                RelativeOrigin = localOrigin.ToVec3f(),
                RelativeEnd = (localOrigin + rayVector).ToVec3f(),
                Color = DebugDrawColor,
                Duration = DebugDrawDuration
            });

            if (entitySelection != null)
                OnHit([entitySelection.Entity]);

            return;
        }

        Vec3d entityRight = targetForward.Cross(WorldUp).Normalize();

        var hits = new HashSet<Entity>();
        int currentRayIndex = 0;
        for (int row = 0; row < rowCount; ++row)
        {
            int raysRemaining = rayCount - currentRayIndex;
            int rowsRemaining = rowCount - row;
            int colsThisRow = raysRemaining / rowsRemaining;

            for (int col = 0; col < colsThisRow; ++col)
            {
                float horizT = colsThisRow > 1 ? (float)col / (colsThisRow - 1) : 0.5f;
                float vertT = rowCount > 1 ? (float)row / (rowCount - 1) : 0.5f;

                float phi = GameMath.Lerp(-halfHorizAngleRad, halfHorizAngleRad, horizT);
                float theta = GameMath.Lerp(-halfVertAngleRad, halfVertAngleRad, vertT);

                var localDir = new Vec3d(
                    MathF.Sin(phi) * MathF.Cos(theta),
                    MathF.Sin(theta),
                    MathF.Cos(phi) * MathF.Cos(theta)
                );

                Vec3d rayDirection = localDir.X * entityRight + localDir.Y * WorldUp + localDir.Z * targetForward;
                Vec3d rayVector = (rayDirection + startDirectionOffset) * distance;
                Vec3d worldRayEnd = worldOrigin + rayVector;

                var blockSelection = new BlockSelection();
                target.World.RayTraceForSelection(worldOrigin, worldRayEnd, ref blockSelection, ref entitySelection);

                debugDrawSystem?.RequestDebugDrawLine(new DebugDrawLine
                {
                    Handle = debugDrawHandles[currentRayIndex],
                    BlockPosition = target.Pos.AsBlockPos,
                    RelativeOrigin = localOrigin.ToVec3f(),
                    RelativeEnd = (localOrigin + rayVector).ToVec3f(),
                    Color = DebugDrawColor,
                    Duration = DebugDrawDuration
                });

                if (entitySelection != null)
                    hits.Add(entitySelection.Entity);

                currentRayIndex++;
            }
        }

        OnHit(hits.ToArray());
    }
}
