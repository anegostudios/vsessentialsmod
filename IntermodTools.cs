using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace Vintagestory.ServerMods
{
    // By DarkHekroMant: https://github.com/DArkHekRoMaNT/CompatibilityLib/blob/40ab78c40d6fb69e9b5c702534be5b5fcb74627e/src/Core.cs
    // This was under under MIT License: https://github.com/DArkHekRoMaNT/CompatibilityLib/blob/40ab78c40d6fb69e9b5c702534be5b5fcb74627e/LICENSE
    public class ModCompatiblityUtil : ModSystem
    {
        public static AssetCategory compatibility = new AssetCategory("compatibility", true, EnumAppSide.Universal);

        public static string[] partiallyWorkingCategories = { "shapes", "textures" };

        public static List<string> LoadedModIds { get; private set; } = new List<string>();


        public override double ExecuteOrder() => 0.04; //load before json patching

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            LoadedModIds = api.ModLoader.Mods.Select((m) => m.Info.ModID).ToList();

            RemovePrefixes(api);
        }

        private void RemovePrefixes(ICoreAPI api)
        {
            foreach (var mod in api.ModLoader.Mods)
            {
                string prefix = "compatibility/" + mod.Info.ModID;
                foreach (var asset in api.Assets.GetMany(prefix))
                {
                    AssetLocation newLoc = asset.Location;
                    newLoc.Path = asset.Location.Path.Remove(0, prefix.Length + 1); //remove "<prefix>/"

                    //Remove existing assets (if exists)
                    if (api.Assets.AllAssets.ContainsKey(newLoc)) api.Assets.AllAssets.Remove(newLoc);

                    asset.Location.Path = newLoc.Path;

                    // Warning about partially worked categories
                    if (partiallyWorkingCategories.Contains(asset.Location.Category.Code))
                    {
                        api.World.Logger.Warning("[" + Mod.Info.ModID + "] Asset categories: {0} - may work strange. " +
                            "It is best to leave such assets out of the compatibility category!",
                            string.Join(", ", partiallyWorkingCategories)
                        );
                    }
                }
            }
        }

    }
}
