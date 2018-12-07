using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Datastructures;

namespace Vintagestory.API.Common
{
    public enum EnumComparison
    {
        SameAs,
        LessThan,
        MoreThan
    }

    public enum EnumDataType
    {
        Bool,
        Int,
        Float,
        Double
    }


    public abstract class GoapCondition
    {
        
    }


    public class BlockConditon : GoapCondition
    {
        ActionConsumable<Block> matcher;

        public BlockConditon(ActionConsumable<Block> matcher)
        {
            this.matcher = matcher;
        }

        public bool Satisfies(Block block)
        {
            return matcher.Invoke(block);
        }
    }
    



}
