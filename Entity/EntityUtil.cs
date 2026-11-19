using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

public static class EntityUtil
{
    /// <summary> Returns true if someone owns onEntity, and it's not byEntity. Or in practical terms, returns true if most interactions by byEntity on onEntity should be blocked with an ownership-related error message. </summary>
    public static bool RestrictedByOwnership(Entity onEntity, Entity byEntity)
    {
        if (byEntity is not EntityPlayer playerEntity) return false;

        string? ownerUID = onEntity.WatchedAttributes.GetTreeAttribute("ownedby")?.GetString("uid");
        return ownerUID != null && ownerUID != playerEntity.Player.PlayerUID;
    }
}
