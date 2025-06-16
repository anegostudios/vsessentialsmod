using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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

        public AiTaskGotoEntity(EntityAgent entity, Entity target) : base(entity)
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
            pathTraverser.NavigateTo_Async(targetEntity.ServerPos.XYZ, moveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck);
            currentFollowTime = 0;
        }

        public override bool CanContinueExecute()
        {
            return pathTraverser.Ready;
        }

        public override bool ContinueExecute(float dt)
        {
            currentFollowTime += dt;

            pathTraverser.CurrentTarget.X = targetEntity.ServerPos.X;
            pathTraverser.CurrentTarget.Y = targetEntity.ServerPos.Y;
            pathTraverser.CurrentTarget.Z = targetEntity.ServerPos.Z;

            Cuboidd targetBox = targetEntity.SelectionBox.ToDouble().Translate(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
            Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.ServerPos.Yaw);
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
            Cuboidd targetBox = targetEntity.SelectionBox.ToDouble().Translate(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
            Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.ServerPos.Yaw);
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
