using System.Collections.Generic;
using Vintagestory.API.Common;

#nullable disable

namespace Vintagestory.GameContent
{
    public class AIGoapAction
    {
        Dictionary<string, GoapCondition> conditions = new Dictionary<string, GoapCondition>();
        Dictionary<string, GoapCondition> effect = new Dictionary<string, GoapCondition>();

        public virtual float Cost { get { return 1f; } }

        


    }
}
