using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods.NoObf;

#nullable disable

namespace Vintagestory.ServerMods
{
    /// <summary>
    /// Base class for systems that provide world generation areas with custom block patches.
    /// Implements IBlockPatchModifier with common logic for area-based patch customization.
    /// </summary>
    public abstract class WorldGenAreaProvider : ModStdWorldGen, IBlockPatchModifier
    {
        protected ICoreServerAPI api;
        protected Dictionary<string, BlockPatchConfig> areaPatchConfigs;
        protected List<IWorldGenArea> nearAreas = new();
        protected int areaPatchHashCode;
        protected LCGRandom areaRand;
        protected RockStrataConfig rockStrata;

        /// <summary>
        /// Return all areas managed by this provider
        /// </summary>
        public abstract IEnumerable<IWorldGenArea> GetAllAreas();

        /// <summary>
        /// Return the asset path for block patches for the given area, or null if no custom patches
        /// </summary>
        public abstract string GetBlockPatchPath(IWorldGenArea area);

        /// <summary>
        /// Category name for this provider's patches (used for hash code generation)
        /// </summary>
        protected abstract string PatchCategoryName { get; }

        /// <summary>
        /// Whether this provider is currently enabled
        /// </summary>
        protected abstract bool IsEnabled { get; }

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            base.StartServerSide(api);

            areaPatchHashCode = BitConverter.ToInt32(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(PatchCategoryName)));
            areaPatchConfigs = new Dictionary<string, BlockPatchConfig>();
        }

        /// <summary>
        /// Initialize the provider. Call this during world gen initialization.
        /// </summary>
        protected virtual void InitAreaProvider(RockStrataConfig rockStrata, LCGRandom rand)
        {
            this.rockStrata = rockStrata;
            this.areaRand = rand;
        }

        /// <summary>
        /// Check if an area blocks generation at the given position
        /// </summary>
        protected virtual IWorldGenArea GetBlockingAreaAt(int x, int z, int category, IEnumerable<IWorldGenArea> areas)
        {
            foreach (var area in areas)
            {
                if (area.Location.Contains(x, z)) return area;

                var distSq = area.CenterPos.HorDistanceSqTo(x, z);
                
                int checkRadius = 0;
                if (category < 0)
                {
                    checkRadius = area.LandformRadius;
                }
                else if (area.SkipGenerationFlags != null && area.SkipGenerationFlags.TryGetValue(category, out var radius))
                {
                    checkRadius = radius;
                }

                if (checkRadius > 0 && distSq < checkRadius * checkRadius)
                {
                    return area;
                }
            }

            return null;
        }

        #region IBlockPatchModifier Implementation

        public virtual bool PreventPlacementAt(int x, int z, int category)
        {
            if (!IsEnabled) return false;
            if (category == areaPatchHashCode) return false;

            var area = GetBlockingAreaAt(x, z, category, nearAreas);
            return area != null;
        }

        public virtual bool PreventPlacementBroadlyAt(int chunkX, int chunkZ)
        {
            if (!IsEnabled) return false;

            // Check 3x3 chunk area
            var rect = new HorRectanglei(
                (chunkX - 1) * chunksize, 
                (chunkZ - 1) * chunksize, 
                (chunkX + 2) * chunksize, 
                (chunkZ + 2) * chunksize
            );

            nearAreas.Clear();

            foreach (var area in GetAllAreas())
            {
                if (area.Location.Intersects(rect))
                {
                    nearAreas.Add(area);
                }
            }

            if (nearAreas.Count > 0)
            {
                EnsurePatchConfigsLoaded();
            }

            return nearAreas.Count > 0;
        }

        public virtual BlockPatchConfig GetPatchProviderAt(int chunkX, int chunkZ, ref EnumHandling handling)
        {
            if (!IsEnabled) return null;
            if (areaRand == null) return null;

            int dx = areaRand.NextInt(chunksize);
            int dz = areaRand.NextInt(chunksize);

            int x = chunkX * chunksize + dx;
            int z = chunkZ * chunksize + dz;

            var area = GetBlockingAreaAt(x, z, -1, nearAreas);
            if (area != null && areaPatchConfigs.TryGetValue(area.Code, out var blockPatchConfig))
            {
                return blockPatchConfig;
            }

            return null;
        }

        #endregion

        /// <summary>
        /// Load patch configs for all areas that have custom patches
        /// </summary>
        protected virtual void EnsurePatchConfigsLoaded()
        {
            foreach (var area in GetAllAreas())
            {
                if (areaPatchConfigs.ContainsKey(area.Code)) continue;

                var path = GetBlockPatchPath(area);
                if (string.IsNullOrEmpty(path)) continue;

                var patches = api.Assets.GetMany<BlockPatch[]>(api.World.Logger, path)
                    .OrderBy(b => b.Key.ToString())
                    .ToList();

                if (patches?.Count > 0)
                {
                    var allPatches = new List<BlockPatch>();
                    foreach (var val in patches)
                    {
                        foreach (var patch in val.Value)
                        {
                            patch.CategoryHashCode = areaPatchHashCode;
                        }
                        allPatches.AddRange(val.Value);
                    }

                    var config = new BlockPatchConfig()
                    {
                        Patches = allPatches.ToArray()
                    };
                    config.ResolveBlockIds(api, rockStrata, areaRand);
                    areaPatchConfigs[area.Code] = config;
                }
            }
        }
    }
}
