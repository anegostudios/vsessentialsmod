using Vintagestory.API.Common.Entities;

namespace Vintagestory.StateTree;

/// <summary>
/// Action that makes the entity rotate at a constant speed.
/// </summary>
public class RotateAction(float weight, Entity entity, float speed) : Action(weight)
{
    public override void Execute(float deltaTime)
    {
        entity.Pos.Yaw += speed * deltaTime;
    }
}
