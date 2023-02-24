using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.ServerMods
{
    public class RegistryObjectVariantGroup
    {
        public AssetLocation LoadFromProperties;
        public AssetLocation[] LoadFromPropertiesCombine;
        public string Code;
        public string[] States;
        public EnumCombination Combine = EnumCombination.Multiply;

        public string OnVariant;
        public string IsValue;
    }
}
