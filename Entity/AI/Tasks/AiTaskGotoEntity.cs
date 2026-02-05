using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class AiTaskGotoEntity : AiTaskBase
    {
        public Entity targetEntity;

        public float moveSpeed = 0.02f;
        public float seekingRange = 25f;
        public float maxFollowTime = 60;
        public float allowedExtraDistance;

        bool stuck = false;

        float currentFollowTime = 0;


        public bool Finished => !pathTraverser.Ready;

        public AiTaskGotoEntity(EntityAgent entity, Entity target) : base(entity, JsonObject.FromJson("{}"), JsonObject.FromJson("{}"))
        {
            targetEntity = target;

            animMeta = new AnimationMetaData()
            {
                Code = "walk",
                Animation = "walk",
                AnimationSpeed = 1f
            }.Init();
        }



        public override bool ShouldExecute()
        {
            return false;
        }

        public float MinDistanceToTarget()
        {
            return allowedExtraDistance + System.Math.Max(0.8f, targetEntity.SelectionBox.XSize / 2 + entity.SelectionBox.XSize / 2);
        }

        public override void StartExecute()
        {
            base.StartExecute();
            stuck = false;
            pathTraverser.NavigateTo_Async(targetEntity.Pos.XYZ, moveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck);
            currentFollowTime = 0;
        }

        public override bool CanContinueExecute()
        {
            return pathTraverser.Ready;
        }

        public override bool ContinueExecute(float dt)
        {
            currentFollowTime += dt;

            //Check if time is still valid for task.
            if (!IsInValidDayTimeHours(false)) return false;

            pathTraverser.CurrentTarget.X = targetEntity.Pos.X;
            pathTraverser.CurrentTarget.Y = targetEntity.Pos.Y;
            pathTraverser.CurrentTarget.Z = targetEntity.Pos.Z;

            Cuboidd targetBox = targetEntity.SelectionBox.ToDouble().Translate(targetEntity.Pos.X, targetEntity.Pos.Y, targetEntity.Pos.Z);
            Vec3d pos = entity.Pos.XYZ.Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.Pos.Yaw);
            double distance = targetBox.ShortestDistanceFrom(pos);

            float minDist = MinDistanceToTarget();

            return
                currentFollowTime < maxFollowTime &&
                distance < seekingRange * seekingRange &&
                distance > minDist &&
                !stuck
            ;
        }

        public bool TargetReached()
        {
            Cuboidd targetBox = targetEntity.SelectionBox.ToDouble().Translate(targetEntity.Pos.X, targetEntity.Pos.Y, targetEntity.Pos.Z);
            Vec3d pos = entity.Pos.XYZ.Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.Pos.Yaw);
            double distance = targetBox.ShortestDistanceFrom(pos);

            float minDist = MinDistanceToTarget();

            return distance < minDist;
        }


        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);
            pathTraverser.Stop();
        }


        public override bool Notify(string key, object data)
        {
            return false;
        }


        private void OnStuck()
        {
            stuck = true;
        }

        private void OnGoalReached()
        {
            pathTraverser.Active = true;
        }
    }
}
