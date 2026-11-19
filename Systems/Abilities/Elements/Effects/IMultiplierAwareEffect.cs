using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

public interface IMultiplierAwareEffect : IEffect
{
    void Apply(Entity sourceEntity, Entity targetEntity, float multiplier);
}
