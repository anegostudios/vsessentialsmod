using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods.NoObf;

#nullable disable

namespace Vintagestory.ServerMods
{
    /// <summary>
    /// A provider of story structures
    /// </summary>
    public interface IStructureProvider
    {
        /// <summary>
        /// Returns structure if should block generation of given category of blocks at this location (3D search)
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="category">Set to -1 to not check category</param>
        /// <returns></returns>
        //IStructureLocation GetBlockingStructureAt(BlockPos pos, int category);

        /// <summary>
        /// Returns structure if should deny generation of given category of blocks at this location. Ignores Y-Coordinate
        /// </summary>
        /// <param name="x"></param>
        /// <param name="z"></param>
        /// <param name="category">Set to -1 to not check category</param>
        /// <returns></returns>
        IStructureLocation GetBlockingStructureAt(int x, int z, int category);
    }


    public class ModSystemWorldGenStructures : ModSystem
    {
        /// <summary>
        /// Event triggered when structures are deleted from a region during regeneration.
        /// </summary>
        public event Action<List<GeneratedStructure>> OnStructuresDeleted;
        
        public void TriggerStructuresDeleted(List<GeneratedStructure> structures) => OnStructuresDeleted?.Invoke(structures);

        /// <summary>
        /// Event triggered when a chunk column regeneration is finalized.
        /// </summary>
        public event Action<int, int> OnRegenFinalized;

        public void TriggerRegenFinalized(int chunkX, int chunkZ) => OnRegenFinalized?.Invoke(chunkX, chunkZ);
        protected IStructureProvider[] providers;
        public override bool ShouldLoad(EnumAppSide forSide) => true;


        public override void StartPre(ICoreAPI api)
        {
            providers = Array.Empty<IStructureProvider>();
        }

        public void RegisterProvider(IStructureProvider provider)
        {
            providers = providers.Append(provider);
        }

        /// <summary>
        /// Checks weather the provided position is inside a story structures schematics for the specified skipCategory, or it's radius.
        /// If the Radius for the storystrcuture should be also checked is defined in the json as int as part of the skipGenerationCategories. Does only check 2D from story locations center.
        /// If the Radius at skipGenerationCategories is 0 then only the structures cuboid is checked.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="z"></param>
        /// <param name="category">The hashcode of the string category from storystructure.json skipGenerationCategories. The strings from storystructure.json are first converted to lowercase before getting the hash code.</param>
        /// <returns></returns>
        public IStructureLocation GetIntersectingStructure(int x, int z, int category)
        {
            for (int i = 0; i < providers.Length; i++)
            {
                var structure = providers[i].GetBlockingStructureAt(x, z, category);
                if (structure != null) return structure;
            }

            return null;
        }

    }

    public abstract class ModStdWorldGen : ModSystem
    {
        public GlobalConfig gcfg;
        protected const int chunksize = GlobalConstants.ChunkSize;

        public static int StructuresHashCode;
        public static int PatchesHashCode;
        public static int CavesHashCode;
        public static int TreesHashCode;
        public static int ShrubsHashCode;
        public static int StalagHashCode;
        public static int HotSpringsHashCode;
        public static int RivuletsHashCode;
        public static int PondsHashCode;
        public static int CreaturesHashCode;

        public static int[] AllHashCodes;

        static ModStdWorldGen()
        {
            StructuresHashCode = BitConverter.ToInt32(SHA256.HashData("structures"u8.ToArray()));
            PatchesHashCode = BitConverter.ToInt32(SHA256.HashData("patches"u8.ToArray()));
            CavesHashCode = BitConverter.ToInt32(SHA256.HashData("caves"u8.ToArray()));
            TreesHashCode = BitConverter.ToInt32(SHA256.HashData("trees"u8.ToArray()));
            ShrubsHashCode = BitConverter.ToInt32(SHA256.HashData("shrubs"u8.ToArray()));
            HotSpringsHashCode = BitConverter.ToInt32(SHA256.HashData("hotsprings"u8.ToArray()));
            RivuletsHashCode = BitConverter.ToInt32(SHA256.HashData("rivulets"u8.ToArray()));
            StalagHashCode = BitConverter.ToInt32(SHA256.HashData("stalag"u8.ToArray()));
            PondsHashCode = BitConverter.ToInt32(SHA256.HashData("pond"u8.ToArray()));
            CreaturesHashCode = BitConverter.ToInt32(SHA256.HashData("creatures"u8.ToArray()));

            AllHashCodes = new int[] { StructuresHashCode, PatchesHashCode, CavesHashCode, TreesHashCode, ShrubsHashCode, HotSpringsHashCode, RivuletsHashCode, StalagHashCode, PondsHashCode, CreaturesHashCode };
        }

        ModSystemWorldGenStructures provider;


        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }

        
        public override void StartServerSide(ICoreServerAPI api)
        {
            LoadGlobalConfig(api);
        }

        public void LoadGlobalConfig(ICoreServerAPI api)
        {
            gcfg = GlobalConfig.GetInstance(api);

            provider = api.ModLoader.GetModSystem<ModSystemWorldGenStructures>();
        }

        public IStructureLocation GetIntersectingStructure(int x, int z, int category)
        {
            return provider.GetIntersectingStructure(x, z, category);
        }


        /// <summary>
        /// Checks weather the provided position is inside a story structures schematics for the specified skipCategory, or it's radius.
        /// If the Radius for the storystrcuture should be also checked is defined in the json as int as part of the skipGenerationCategories. Does only check 2D from story locations center.
        /// If the Radius at skipGenerationCategories is 0 then only the structures cuboid is checked.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        public IStructureLocation GetIntersectingStructure(int x, int z)
        {
            return GetIntersectingStructure(x, z, -1);
        }
    }
}
