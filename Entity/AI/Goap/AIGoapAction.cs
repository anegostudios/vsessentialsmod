using System.Collections.Generic;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent
{
    public class AIGoapAction
    {
        Dictionary<string, GoapCondition> conditions = new Dictionary<string, GoapCondition>();
        Dictionary<string, GoapCondition> effect = new Dictionary<string, GoapCondition>();

        public virtual float Cost { get { return 1f; } }

        


    }
}
