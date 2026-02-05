using System.Collections.Generic;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.ServerMods
{
    /// <summary>
    /// Represents an area in the world where generation rules can be customized.
    /// Implement this interface to define zones with custom block patches, suppressed generation, etc.
    /// </summary>
    public interface IWorldGenArea
    {
        /// <summary>
        /// Unique identifier for this area
        /// </summary>
        string Code { get; }

        /// <summary>
        /// The bounding box of this area
        /// </summary>
        Cuboidi Location { get; }

        /// <summary>
        /// Center position of this area
        /// </summary>
        BlockPos CenterPos { get; }

        /// <summary>
        /// Radius for landform/terrain modifications around CenterPos
        /// </summary>
        int LandformRadius { get; }

        /// <summary>
        /// Radius for structure generation around CenterPos
        /// </summary>
        int GenerationRadius { get; }

        /// <summary>
        /// Category hash codes mapped to skip radii.
        /// Key: Category hash code (e.g., TreesHashCode, ShrubsHashCode)
        /// Value: Radius within which to skip generation. If 0, only skip within the Location cuboid.
        /// Use ModStdWorldGen static hash codes for standard categories.
        /// </summary>
        Dictionary<int, int> SkipGenerationFlags { get; }

        /// <summary>
        /// This is the max range (squared) of skip generation radius across all categories
        /// </summary>
        int MaxSkipGenerationRadiusSq { get; }
    }
}
