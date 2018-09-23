using System.Collections.Generic;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.ServerMods.NoObf
{
    public class RegistryObjectVariantGroup
    {
        public string LoadFromProperties;
        public string Code;
        public string[] States;
        public EnumCombination Combine = EnumCombination.Multiply;

        public string OnVariant;
    }
}
