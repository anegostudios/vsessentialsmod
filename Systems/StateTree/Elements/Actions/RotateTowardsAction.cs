using System;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Action that rotates the entity towards a position.
/// </summary>
public class RotateTowardsAction(
    float weight,
    RotateTowardsActionType type,
    float rotateSpeed,
    float threshold,
    Entity entity,
    EntityBehaviorMemory? entityMemory
) : Action(weight)
{
    public override void Execute(float deltaTime)
    {
        Vec3d position = null;
        switch (type)
        {
            case RotateTowardsActionType.TargetEntityPosition:
            {
                position = entityMemory?.GetTargetEntity().Pos.XYZ;
                break;
            }
            case RotateTowardsActionType.TargetBlockPosition:
                position = entityMemory?.TargetBlock.Position.ToVec3d();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        if (position != null)
            RotateTowardsTargetPosition(position, deltaTime);
    }

    private void RotateTowardsTargetPosition(Vec3d targetPosition, float deltaTime)
    {
        var entityLookDir = (entity.Pos.GetViewVector().NormalizedCopy() * -1f).ToVec3d();
        var worldUp = new Vec3d(0, 1, 0);
        var entityRight = entityLookDir.Cross(worldUp);

        var toPosition = entity.Pos.XYZ.Sub(targetPosition).Normalize();

        var dot = entityRight.Dot(toPosition);
        if (Math.Abs(dot) < threshold)
            return;

        var rotateAmount = rotateSpeed * deltaTime;
        entity.Pos.Yaw += dot > 0.0
            ? rotateAmount
            : -rotateAmount;
    }
}
