using System;
using Vintagestory.API.MathTools;

namespace Vintagestory.API.Common
{
    public abstract class PathTraverserBase
    {
        protected EntityAgent entity;
        protected Vec3d target;
        public Action OnGoalReached;
        public Action OnStuck;
        protected int stuckCounter;

        public bool Active;

        protected float movingSpeed;
        public float curTurnRadPerSec;

        protected float targetDistance;

        public virtual Vec3d CurrentTarget
        {
            get { return target; }
        }

        public virtual bool Ready
        {
            get { return true; }
        }


        public PathTraverserBase(EntityAgent entity)
        {
            this.entity = entity;
        }

        public bool NavigateTo(Vec3d target, float movingSpeed, Action OnGoalReached, Action OnStuck, int mhdistanceTolerance = 0)
        {
            return NavigateTo(target, movingSpeed, 0.12f, OnGoalReached, OnStuck, false, 10000, mhdistanceTolerance);
        }

        public virtual bool NavigateTo(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck, bool giveUpWhenNoPath = false, int searchDepth = 10000, int mhdistanceTolerance = 0)
        {
            return WalkTowards(target, movingSpeed, targetDistance, OnGoalReached, OnStuck);
        }

        public virtual bool NavigateTo_Async(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck, Action OnNoPath = null, int searchDepth = 10000, int mhdistanceTolerance = 0)
        {
            return WalkTowards(target, movingSpeed, targetDistance, OnGoalReached, OnStuck);
        }

        public virtual bool WalkTowards(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck)
        {
            stuckCounter = 0;

            this.OnGoalReached = OnGoalReached;
            this.OnStuck = OnStuck;
            this.movingSpeed = movingSpeed;
            this.targetDistance = targetDistance;
            this.target = target;
            Active = true;
            return BeginGo();
        }

        public virtual void OnGameTick(float dt)
        {

        }

        protected abstract bool BeginGo();
        public abstract void Stop();

        public virtual void Retarget()
        {
            
        }
    }
}
