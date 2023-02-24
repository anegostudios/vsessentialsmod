using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class BlockEntityGeneric : BlockEntity, IRotatable
    {
        public void OnTransformed(ITreeAttribute tree, int degreeRotation, EnumAxis? flipAxis)
        {
            foreach (var val in Behaviors)
            {
                if (val is IRotatable bhrot)
                {
                    bhrot.OnTransformed(tree, degreeRotation, flipAxis);
                }
            }
        }
    }
}
