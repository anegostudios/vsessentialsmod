using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;

#nullable disable

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Class for not rendering entities
    /// </summary>
    public class EntityRendererInvisible : EntityRenderer
    {
        public EntityRendererInvisible(Entity entity, ICoreClientAPI api) : base(entity, api)
        {
        }

        public override void Dispose()
        {
        }
    }
}
