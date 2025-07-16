using Newtonsoft.Json;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// <br/><br/>
/// 
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2:<br/>
/// - executionChance default value: 0.5 => 1 (when in emotion state)<br/>
/// - executionChance default value: 0.1 => 1 <br/>
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskStayInRangeConfig : AiTaskBaseTargetableConfig
{
    /// <summary>
    /// Ignore checks for being able to start task, if was recently attacked.
    /// </summary>
    [JsonProperty] public bool RetaliateUnconditionally = false;

    /// <summary>
    /// If target is closer then this range, entity will try to go away from target.
    /// </summary>
    [JsonProperty] public float TargetRangeMin = 15f;

    /// <summary>
    /// If target is further then this range, entity will try to come closer.
    /// </summary>
    [JsonProperty] public float TargetRangeMax = 25f;

    /// <summary>
    /// Entity movement speed.
    /// </summary>
    [JsonProperty] public float MoveSpeed = 0.02f;

    /// <summary>
    /// Affects pathfinding algorithm, see <see cref="EnumAICreatureType"/>.
    /// </summary>
    [JsonProperty] public EnumAICreatureType AiCreatureType = EnumAICreatureType.Default;

    /// <summary>
    /// If set to false, entity will avoid stepping into lequids.
    /// </summary>
    [JsonProperty] public bool CanStepInLiquid = false;
}

/// <summary>
/// When player detected at a range of "searchRange" then
/// Always keep at a range of "targetRange" blocks if possible
/// This means we need some semi intelligent position selection where the bowtorn should stand because
/// 1. Needs to be within a min/max range
/// 2. Needs to have line of sight to shoot at
/// 3. Needs to not be in water or fall down a cliff in the process
/// 4. Prefer highground
/// 5. Sort&Find the most optimal location from all of these conditions
///
///
/// I think a simple steering system would be just fine for now
/// Plot a line straight towards/away from player and walk along that line for as long as there is nothing blocking it
/// If something is blocking it, turn left or right
/// </summary>
public class AiTaskStayInRangeR : AiTaskBaseTargetableR
{
    private AiTaskStayInRangeConfig Config => GetConfig<AiTaskStayInRangeConfig>();

    /// <summary>
    /// Exposes targetEntity for AiTaskTurretMode
    /// </summary>
    public Entity? TargetEntity { get => targetEntity; set => targetEntity = value; }

    public AiTaskStayInRangeR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskStayInRangeConfig>(entity, taskConfig, aiConfig);
    }

    public override bool ShouldExecute()
    {
        // If target is set by 'AiTaskTurretMode', execute if target is out of range
        if (targetEntity != null)
        {
            return CheckIfOutOfRange();
        }

        if (!(PreconditionsSatisficed() || Config.RetaliateUnconditionally && RecentlyAttacked)) return false;

        if (!RecentlyAttacked)
        {
            attackedByEntity = null;
        }

        if (!CheckAndResetSearchCooldown()) return false;

        if (Config.RetaliateAttacks && attackedByEntity != null && attackedByEntity.Alive && attackedByEntity.IsInteractable && CanSense(attackedByEntity, GetSeekingRange()) && !entity.ToleratesDamageFrom(attackedByEntity))
        {
            targetEntity = attackedByEntity;
            return true;
        }

        SearchForTarget();

        if (targetEntity != null)
        {
            return CheckIfOutOfRange();
        }

        return false;
    }

    public override bool ContinueExecute(float dt)
    {
        if (!ContinueExecuteChecks(dt)) return false;

        if (pathTraverser.Active) return true;

        CheckIfOutOfRange(out bool tooFar, out bool tooNear);

        bool canWalk = false;

        if (tooFar)
        {
            canWalk = WalkTowards(-1);
        }
        else if (tooNear)
        {
            canWalk = WalkTowards(1);
        }

        return canWalk && (tooFar || tooNear);
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);

        targetEntity = null;
    }

    protected virtual bool CheckIfOutOfRange() => CheckIfOutOfRange(out _, out _);

    protected virtual bool CheckIfOutOfRange(out bool tooFar, out bool tooNear)
    {
        tooFar = false;
        tooNear = false;
        if (targetEntity == null) return false;

        double distanceSquared = entity.ServerPos.SquareDistanceTo(targetEntity.ServerPos.XYZ);
        tooFar = distanceSquared >= Config.TargetRangeMin * Config.TargetRangeMin;
        tooNear = distanceSquared <= Config.TargetRangeMax * Config.TargetRangeMax;

        return tooNear || tooFar;
    }

    protected virtual bool WalkTowards(int sign)
    {
        IBlockAccessor ba = entity.World.BlockAccessor;
        Vec3d selfpos = entity.ServerPos.XYZ;
        Vec3d dir = selfpos.SubCopy(targetEntity.ServerPos.X, selfpos.Y, targetEntity.ServerPos.Z).Normalize();
        Vec3d nextPos = selfpos + sign * dir;
        // Lets use only block center for testing
        Vec3d testPos = new((int)nextPos.X + 0.5, (int)nextPos.Y, (int)nextPos.Z + 0.5);

        // Straight
        if (CanStepTowards(nextPos))
        {
            pathTraverser.WalkTowards(nextPos, Config.MoveSpeed, 0.3f, OnGoalReached, OnStuck, Config.AiCreatureType);
            return true;
        }

        // Randomly flip left/right direction preference
        int rnds = 1 - entity.World.Rand.Next(2) * 2;

        // Left
        Vec3d ldir = dir.RotatedCopy(rnds * GameMath.PIHALF);
        nextPos = selfpos + ldir;
        testPos = new Vec3d((int)nextPos.X + 0.5, (int)nextPos.Y, (int)nextPos.Z + 0.5);
        if (CanStepTowards(testPos))
        {
            pathTraverser.WalkTowards(nextPos, Config.MoveSpeed, 0.3f, OnGoalReached, OnStuck, Config.AiCreatureType);
            return true;
        }
        // Right
        Vec3d rdir = dir.RotatedCopy(-rnds * GameMath.PIHALF);
        nextPos = selfpos + rdir;
        testPos = new Vec3d((int)nextPos.X + 0.5, (int)nextPos.Y, (int)nextPos.Z + 0.5);
        if (CanStepTowards(testPos))
        {
            pathTraverser.WalkTowards(nextPos, Config.MoveSpeed, 0.3f, OnGoalReached, OnStuck, Config.AiCreatureType);
            return true;
        }

        return false;
    }

    protected virtual bool CanStepTowards(Vec3d nextPos)
    {
        if (targetEntity == null) return false;

        Vec3d collTmpVec = new();

        bool hereCollide = world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, nextPos, false);
        if (hereCollide)
        {
            bool oneBlockUpCollide = world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, collTmpVec.Set(nextPos).Add(0, Math.Min(1, stepHeight), 0), false);
            // Ok to step up one block
            if (!oneBlockUpCollide) return true;
        }

        // Block in front plus block in front one step up -> this is a wall
        if (hereCollide) return false;

        if (IsLiquidAt(nextPos) && !Config.CanStepInLiquid) return false;

        // Ok to step down one block
        bool belowCollide = world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, collTmpVec.Set(nextPos).Add(0, -1.1, 0), false);
        if (belowCollide)
        {
            nextPos.Y -= 1;
            return true;
        }

        if (IsLiquidAt(collTmpVec) && !Config.CanStepInLiquid) return false;

        // Ok to step down 2 or 3 blocks if we are 1-2 block above the player
        bool below2Collide = world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, collTmpVec.Set(nextPos).Add(0, -2.1, 0), false);
        if (below2Collide && entity.ServerPos.Y - targetEntity.ServerPos.Y >= 1)
        {
            nextPos.Y -= 2;
            return true;
        }

        if (IsLiquidAt(collTmpVec) && !Config.CanStepInLiquid) return false;

        bool below3Collide = world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, collTmpVec.Set(nextPos).Add(0, -3.1, 0), false);
        if (!below2Collide && below3Collide && entity.ServerPos.Y - targetEntity.ServerPos.Y >= 2)
        {
            nextPos.Y -= 3;
            return true;
        }

        return false;
    }

    protected virtual bool IsLiquidAt(Vec3d pos)
    {
        BlockPos blockPos = new(0);
        blockPos.SetAndCorrectDimension(pos);
        return entity.World.BlockAccessor.GetBlock(blockPos).IsLiquid();
    }

    protected virtual void OnStuck()
    {

    }

    protected virtual void OnGoalReached()
    {

    }
}
