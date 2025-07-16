using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.GameContent;

public class AiTaskStayCloseToGuardedEntityR : AiTaskStayCloseToEntityR
{
    public Entity guardedEntity;

    private AiTaskStayCloseToEntityConfig Config => GetConfig<AiTaskStayCloseToEntityConfig>();

    protected float seekingRange = 25;

    public AiTaskStayCloseToGuardedEntityR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        seekingRange = taskConfig["seekingRange"]?.AsFloat(25) ?? 25;
    }

    public override bool ShouldExecute()
    {
        if (entity.World.Rand.NextDouble() < 0.1)
        {
            guardedEntity = GetGuardedEntity();
        }

        if (guardedEntity == null) return false;
        if (Rand.NextDouble() > 0.1f) return false;
        if (entity.WatchedAttributes.GetBool("commandSit") == true) return false;

        targetEntity = guardedEntity;
        double x = guardedEntity.ServerPos.X;
        double y = guardedEntity.ServerPos.Y;
        double z = guardedEntity.ServerPos.Z;

        double dist = entity.ServerPos.SquareDistanceTo(x, y, z);

        return dist > seekingRange * seekingRange;
    }


    public override void StartExecute()
    {
        base.StartExecute();

        float size = targetEntity.SelectionBox.XSize;

        pathTraverser.NavigateTo_Async(targetEntity.ServerPos.XYZ, Config.MoveSpeed, size + 0.2f, OnGoalReached, OnStuck, TryTeleport, 1000, 1);

        targetOffset.Set(entity.World.Rand.NextDouble() * 2 - 1, 0, entity.World.Rand.NextDouble() * 2 - 1);

        stuck = false;
    }

    public override bool CanContinueExecute()
    {
        return pathTraverser.Ready;
    }

    public Entity GetGuardedEntity()
    {
        var uid = entity.WatchedAttributes.GetString("guardedPlayerUid");
        if (uid != null)
        {
            return entity.World.PlayerByUid(uid)?.Entity;
        }
        else
        {
            var id = entity.WatchedAttributes.GetLong("guardedEntityId");
            return entity.World.GetEntityById(id);
        }
    }
}
