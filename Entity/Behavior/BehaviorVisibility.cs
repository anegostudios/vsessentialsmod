using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Provides some utilities for checking line-of-sight to/from other entities. This requires specifying the
/// elements of the entity's shape that should be considered during checks. Usage example:
/// <code>
/// {
/// "code": "visibility",
///     "lineOfSight": {
///         "elements": [
///             "Head_1",
///             "Torso_1",
///             "HandRight_1",
///             "HandLeft_1",
///             "LegRight_1",
///             "LegLeft_1",
///             "FootRight_1",
///             "FootLeft_1"
///         ],
///         "enableDebugDraw": true
///     }
/// }
/// </code>
/// Note: for best performance, the 'elements' array should be kept as small as reasonably possible and
/// ordered based on visibility chance, with the most likely visible elements at the top.
/// </summary>
/// <param name="entity"></param>
public class EntityBehaviorVisibility(Entity entity) : EntityBehavior(entity)
{
    public override string PropertyName() => "visibility";

    private readonly EntityPlayer player = entity as EntityPlayer;

    private List<string> elementNames = [];

    private bool elementPositionsStale = true;
    private List<Vec3d> elementWorldPositions;

    private BlockSelection blockSelection = new();
    private EntitySelection entitySelection = new();

    private DebugDrawSystem? debugDrawSystem;
    private readonly List<long> rayDebugDrawHandles = [];
    private readonly int debugDrawColor = ColorUtil.ColorFromRgba(251, 43, 212, 255);
    private readonly TimeSpan debugDrawDuration = TimeSpan.FromSeconds(1f);

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        var los = attributes["lineOfSight"];

        var elements = los?["elements"].AsArray<string>();
        if (elements == null || elements.Length == 0) return;

        elementNames = elements.ToList();
        elementWorldPositions = new List<Vec3d>(elements.Length);
        elementWorldPositions.AddRange(Enumerable.Repeat(Vec3d.Zero, elements.Length));
        entity.requirePosesOnServer = true;

        if (los["enableDebugDraw"].AsBool())
        {
            debugDrawSystem = entity.Api.ModLoader.GetModSystem<DebugDrawSystem>();
            var handles = debugDrawSystem?.RequestHandles(elements.Length);
            if (handles != null) rayDebugDrawHandles.AddRange(handles);
        }
    }

    public override void OnGameTick(float deltaTime)
    {
        base.OnGameTick(deltaTime);
        elementPositionsStale = true;
    }

    public bool IsVisibleTo(Entity other)
    {
        if (elementNames.Count == 0) return false;

        if (elementPositionsStale)
        {
            var animator = player?.TpAnimManager?.Animator ?? entity.AnimManager?.Animator;
            if (animator == null) return false;

            var entityWorldMatrix = OBBMath.BuildEntityWorldMatrix(entity);

            for (int i = 0; i < elementNames.Count; ++i)
            {
                var name = elementNames[i];
                var pose = animator.GetPosebyName(name);
                if (pose?.ForElement == null)
                {
                    entity.Api?.Logger?.Warning("EntityBehaviorVisibility on {0}: element '{1}' not found", entity.Code, name);
                    return false;
                }
                var elem = pose.ForElement;
                if (elem.From == null || elem.To == null) return false;

                if (pose.AnimModelMatrix == null)
                    return false;

                Matrixf elementWorldMatrix = entityWorldMatrix.Clone().Mul(pose.AnimModelMatrix);

                Vec3f centerLocal = new Vec3f(
                    ((float)elem.To[0] - (float)elem.From[0]) / 32f,
                    ((float)elem.To[1] - (float)elem.From[1]) / 32f,
                    ((float)elem.To[2] - (float)elem.From[2]) / 32f
                );

                Vec3d centerWorld = elementWorldMatrix.TransformVector(centerLocal.ToVec4f(1)).XYZ.ToVec3d();
                elementWorldPositions[i] = centerWorld;
            }

            elementPositionsStale = false;
        }

        return HasLineOfSight(other);
    }

    private bool HasLineOfSight(Entity fromEntity)
    {
        var from = fromEntity.Pos.XYZ + fromEntity.LocalEyePos;

        for (int i = 0; i < elementNames.Count; ++i)
        {
            var to = elementWorldPositions[i];

            fromEntity.World.RayTraceForSelection(from, to, ref blockSelection, ref entitySelection);

            if (debugDrawSystem != null && rayDebugDrawHandles.Count > i)
            {
                var relativeOrigin = (from - fromEntity.Pos.AsBlockPos.ToVec3d()).ToVec3f();
                var relativeEnd = (to - fromEntity.Pos.AsBlockPos.ToVec3d()).ToVec3f();
                debugDrawSystem.RequestDebugDrawLine(new DebugDrawLine
                {
                    Handle = rayDebugDrawHandles[i],
                    BlockPosition = fromEntity.Pos.AsBlockPos,
                    RelativeOrigin = relativeOrigin,
                    RelativeEnd = relativeEnd,
                    Color = debugDrawColor,
                    Duration = debugDrawDuration
                });
            }

            if (entitySelection != null && entitySelection.Entity == entity)
                return true;
        }

        return false;
    }
}
