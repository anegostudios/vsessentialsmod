using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

public interface IEffect
{
    public void Apply(Entity sourceEntity, Entity targetEntity);
}
