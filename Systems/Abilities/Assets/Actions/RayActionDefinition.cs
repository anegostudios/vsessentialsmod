using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

[AssetSchemaMetadata("Ray", "Collects entities via one or more raycasts.")]
public class RayActionDefinition : IActionDefinition
{
    /// <summary>
    /// The ray length.
    /// </summary>
    [JsonProperty]
    [DefaultValue(5.0f)]
    public Variable<float> Distance { get; set; } = 5.0f;

    /// <summary>
    /// An optional offset for the start position.
    /// </summary>
    [JsonProperty]
    public FastVec3d StartPositionOffset { get; set; }

    /// <summary>
    /// An optional offset for the start direction.
    /// </summary>
    [JsonProperty]
    public FastVec3d StartDirectionOffset { get; set; }

    /// <summary>
    /// The horizontal angular limit for the rays.
    /// </summary>
    [JsonProperty]
    [DefaultValue(45.0f)]
    public float HorizontalAngleDeg { get; set; } = 45.0f;

    /// <summary>
    /// The vertical angular limit for the rays.
    /// </summary>
    [JsonProperty]
    [DefaultValue(45.0f)]
    public float VerticalAngleDeg { get; set; } = 45.0f;

    /// <summary>
    /// How many rays to emit in the volume defined by <see cref="Distance"/>, <see cref="HorizontalAngleDeg"/>, and
    /// <see cref="VerticalAngleDeg"/>. Rays are evenly distributed in this volume. An even number is preferred for even
    /// distribution.
    /// </summary>
    [JsonProperty]
    [DefaultValue(6)]
    public int RayCount { get; set; } = 6;

    private const float MaxAngleDeg = 360.0f;

    public AbilityAction Build(Entity user, IList<IEffect> onHitEffects, AnimationMetaData animationMetaData, DebugDrawSystem debugDrawSystem)
    {
        float horizAngleRad = MathF.Min(HorizontalAngleDeg, MaxAngleDeg) * GameMath.DEG2RAD;
        float vertAngleRad = MathF.Min(VerticalAngleDeg, MaxAngleDeg) * GameMath.DEG2RAD;
        float halfHorizAngleRad = horizAngleRad / 2f;
        float halfVertAngleRad = vertAngleRad / 2f;

        float aspect = vertAngleRad > 0 ? horizAngleRad / vertAngleRad : float.PositiveInfinity;
        int rowCount = (int)MathF.Max(1.0f, MathF.Round(MathF.Sqrt(RayCount / aspect)));
        rowCount = Math.Min(rowCount, RayCount);

        return new RayAction(
            user,
            onHitEffects,
            StartPositionOffset.ToVec3d(),
            StartDirectionOffset.ToVec3d(),
            Distance.GetValue(user),
            halfHorizAngleRad,
            halfVertAngleRad,
            rowCount,
            RayCount,
            debugDrawSystem);
    }
}
