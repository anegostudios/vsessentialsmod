using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    // Goap Concept:
    // Each agent has a several goals that he desires with a specific priority
    // i.e. stay saturated:
    // Goal is saturation > 60
    // how can we reach this target?
    // Ok there is ActionEat that can increase saturation, however its precondition is a food source block within 1 block radius
    // Ok there is ActionLocate block that can satisfy "food source block" by finding a block, but if it finds a block its precondition is move to position x/yz
    // Ok there is ActionWalk that can satisfy move to position x/yz
    public class BehaviorGoapAI : EntityBehavior
    {
        ITreeAttribute goaltree;

        internal float Aggressivness
        {
            get { return goaltree.GetFloat("aggressivness"); }
        }

        

        public BehaviorGoapAI(Entity entity) : base(entity)
        {
            goaltree = entity.WatchedAttributes.GetTreeAttribute("goaltree");
        }

        public override string PropertyName() { return "goaloriented"; }


    }
}
