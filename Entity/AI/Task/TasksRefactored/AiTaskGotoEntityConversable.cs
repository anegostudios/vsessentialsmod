using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// AI task created only from code. Used only by 'EntityBehaviorConversable'.
/// </summary>
public class AiTaskGotoEntityConversable : AiTaskBaseR
{
    public Entity TargetEntity { get; }
    public float MoveSpeed { get; set; } = 0.02f;
    public float SeekingRange { get; set; } = 25f;
    public float MaxFollowTime { get; set; } = 60;
    public float AllowedExtraDistance { get; set; }
    public bool Finished => !pathTraverser.Ready;

    protected bool stuck = false;
    protected float currentFollowTime = 0;
    protected string animationCode = "walk";

    public AiTaskGotoEntityConversable(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        world.Logger.Error($"This AI task 'AiTaskGotoEntityConversable' can only be created from code.");
        throw new InvalidOperationException($"This AI task can only be created from code.");
    }

    public AiTaskGotoEntityConversable(EntityAgent entity, Entity target) : base(entity)
    {
        TargetEntity = target;

        baseConfig.AnimationMeta = new AnimationMetaData()
        {
            Code = animationCode,
            Animation = animationCode,
            AnimationSpeed = 1f
        }.Init();
    }

    public override bool ShouldExecute() => false;

    public override void StartExecute()
    {
        base.StartExecute();
        stuck = false;
        pathTraverser.NavigateTo_Async(TargetEntity.ServerPos.XYZ, MoveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck);
        currentFollowTime = 0;
    }

    public override bool CanContinueExecute()
    {
        return pathTraverser.Ready;
    }

    public override bool ContinueExecute(float dt)
    {
        currentFollowTime += dt;

        pathTraverser.CurrentTarget.X = TargetEntity.ServerPos.X;
        pathTraverser.CurrentTarget.Y = TargetEntity.ServerPos.Y;
        pathTraverser.CurrentTarget.Z = TargetEntity.ServerPos.Z;

        Cuboidd targetBox = TargetEntity.SelectionBox.ToDouble().Translate(TargetEntity.ServerPos.X, TargetEntity.ServerPos.Y, TargetEntity.ServerPos.Z);
        Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.ServerPos.Yaw);
        double distance = targetBox.ShortestDistanceFrom(pos);
        
        float minDistance = MinDistanceToTarget();

        return
            currentFollowTime < MaxFollowTime &&
            distance < SeekingRange * SeekingRange &&
            distance > minDistance &&
            !stuck
        ;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        pathTraverser.Stop();
    }

    public virtual bool TargetReached()
    {
        Cuboidd targetBox = TargetEntity.SelectionBox.ToDouble().Translate(TargetEntity.ServerPos.X, TargetEntity.ServerPos.Y, TargetEntity.ServerPos.Z);
        Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.ServerPos.Yaw);
        double distance = targetBox.ShortestDistanceFrom(pos);

        float minDistance = MinDistanceToTarget();

        return distance < minDistance;
    }

    protected virtual void OnStuck()
    {
        stuck = true;
    }

    protected virtual void OnGoalReached()
    {
        pathTraverser.Active = true;
    }

    protected virtual float MinDistanceToTarget()
    {
        return AllowedExtraDistance + System.Math.Max(0.8f, TargetEntity.SelectionBox.XSize / 2 + entity.SelectionBox.XSize / 2);
    }
}
