using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

/// <summary>
/// Plays an animation on the entity after its shape has been tessellated.
/// </summary>
/// <param name="entity"></param>
public class EntityBehaviorAnimate(Entity entity) : EntityBehavior(entity)
{
    public override string PropertyName() => "animate";

    private string? animCode;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        animCode = attributes["animCode"].ToString();
    }

    public override void OnTesselated()
    {
        base.OnTesselated();

        if (string.IsNullOrEmpty(animCode) || !entity.Properties.Client.AnimationsByMetaCode.TryGetValue(animCode, out var animData)) return;

        entity.AnimManager.TryStartAnimation(animData);
    }
}
