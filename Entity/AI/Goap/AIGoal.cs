using Vintagestory.API.Common;

#nullable disable

namespace Vintagestory.GameContent
{
    public class AIGoal
    {
        IWorldAccessor world;
        BehaviorGoapAI behavior;

        public AIGoal(BehaviorGoapAI behavior, IWorldAccessor world)
        {
            this.world = world;
            this.behavior = behavior;
        }

        public virtual bool ShouldExecute()
        {
            return false;
        }
    }
}
