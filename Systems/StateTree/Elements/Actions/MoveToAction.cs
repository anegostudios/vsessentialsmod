using System;
using Vintagestory.API.MathTools;
using Vintagestory.Essentials;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Action that makes the entity move towards a position.
/// </summary>
public class MoveToAction(
    float weight,
    MoveToActionType type,
    float moveSpeed,
    float threshold,
    StraightLineTraverser straightLineTraverser,
    EntityBehaviorMemory? entityMemory
) : Action(weight)
{
    private bool canFinish = false;

    public override void Execute(float deltaTime)
    {
        Vec3d position;
        switch (type)
        {
            case MoveToActionType.TargetEntityPosition:
            {
                position = entityMemory?.TargetEntity?.LastKnownPosition.ToVec3d();
                break;
            }
            case MoveToActionType.TargetBlockPosition:
            {
                position = entityMemory?.TargetBlock?.Position.ToVec3d();
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        if (position != null)
            MoveToTargetPosition(position);
    }

    private void MoveToTargetPosition(Vec3d targetPosition)
    {
        if (straightLineTraverser.Active)
        {
            straightLineTraverser.CurrentTarget.X = targetPosition.X;
            straightLineTraverser.CurrentTarget.Y = targetPosition.Y;
            straightLineTraverser.CurrentTarget.Z = targetPosition.Z;
        }
        else
        {
            straightLineTraverser.WalkTowards(targetPosition, moveSpeed, threshold, OnDone, OnDone);
        }
    }

    private void OnDone() => canFinish = true;

    public override bool HasFinished() => canFinish;
}
