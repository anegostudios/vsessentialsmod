#nullable disable

namespace Vintagestory.ServerMods
{
    /// <summary>
    /// Interface for forest floor blocks.
    /// </summary>
    public interface IBlockForestFloor
    {
        /// <summary>
        /// The current level/stage of this forest floor block (0-MaxStage)
        /// </summary>
        int CurrentLevel();
    }
}
