using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

public class AiTaskComeToOwnerR : AiTaskStayCloseToEntityR
{
    public Entity? TargetOwner { get => targetEntity; set => targetEntity = value; }

    public AiTaskComeToOwnerR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {

    }

    public override bool ShouldExecute()
    {
        if (!PreconditionsSatisficed()) return false;

        ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("ownedby");

        if (tree == null) return false;

        string? uid = tree.GetString("uid");

        if (uid == null) return false;

        targetEntity = entity.World.PlayerByUid(uid)?.Entity;

        if (targetEntity == null) return false;

        return CanSense(targetEntity, GetSeekingRange());
    }
}
