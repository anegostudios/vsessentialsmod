using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace Vintagestory.ServerMods
{
    // By DarkHekroMant: https://github.com/DArkHekRoMaNT/CompatibilityLib/blob/40ab78c40d6fb69e9b5c702534be5b5fcb74627e/src/Core.cs
    // This was under under MIT License: https://github.com/DArkHekRoMaNT/CompatibilityLib/blob/40ab78c40d6fb69e9b5c702534be5b5fcb74627e/LICENSE
    public class ModCompatiblityUtil : ModSystem
    {
        public static AssetCategory compatibility;

        public static string[] partiallyWorkingCategories = { "shapes", "textures" };

        public static List<string> LoadedModIds { get; private set; } = new List<string>();


        public override double ExecuteOrder() => 0.04; // Load before json patching

        public override void StartPre(ICoreAPI api)
        {
            compatibility = new AssetCategory("compatibility", true, EnumAppSide.Universal);
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            LoadedModIds = api.ModLoader.Mods.Select((m) => m.Info.ModID).ToList();
            RemapFromCompatbilityFolder(api);
        }

        private void RemapFromCompatbilityFolder(ICoreAPI api)
        {
            int quantityAdded = 0;
            int quantityReplaced = 0;

            foreach (var mod in api.ModLoader.Mods)
            {
                string prefix = "compatibility/" + mod.Info.ModID + "/";
                var assets = api.Assets.GetManyInCategory("compatibility", mod.Info.ModID+"/");
                foreach (var asset in assets)
                {
                    // Remap the original asset path to the new path
                    var origPath = new AssetLocation(mod.Info.ModID, asset.Location.Path.Remove(0, prefix.Length));

                    if (api.Assets.AllAssets.ContainsKey(origPath))
                    {
                        quantityReplaced++;
                    }
                    else
                    {
                        quantityAdded++;
                    }

                    api.Assets.AllAssets[origPath] = asset;
                }
            }

            api.World.Logger.Notification("Compatibility lib: {0} assets added, {1} assets replaced.", quantityAdded, quantityReplaced);
        }

    }
}
