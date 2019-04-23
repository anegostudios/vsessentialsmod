using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vintagestory.ServerMods.NoObf
{
    [JsonObject(MemberSerialization.OptIn)]
    public abstract class RegistryObjectType
    {
        public bool Enabled = true;
        public JObject jsonObject;
        public AssetLocation Code;
        public RegistryObjectVariantGroup[] VariantGroups;

        /// <summary>
        /// Variant values as resolved from blocktype/itemtype or entitytype
        /// </summary>
        public Dictionary<string, string> Variant = new Dictionary<string, string>();

        [JsonProperty]
        public WorldInteraction[] Interactions;

        [JsonProperty]
        public AssetLocation[] SkipVariants;

        [JsonProperty]
        public AssetLocation[] AllowedVariants;

        public HashSet<AssetLocation> AllowedVariantsQuickLookup = new HashSet<AssetLocation>();

        [JsonProperty]
        public string Class;

        /// <summary>
        /// Returns true if any given wildcard matches the blocks code. E.g. water-* will match all water blocks
        /// </summary>
        /// <param name="wildcards"></param>
        /// <returns></returns>
        public bool WildCardMatch(AssetLocation[] wildcards)
        {
            foreach (AssetLocation wildcard in wildcards)
            {
                if (WildCardMatch(wildcard)) return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if given wildcard matches the blocks code. E.g. water-* will match all water blocks
        /// </summary>
        /// <param name="wildCard"></param>
        /// <returns></returns>
        public bool WildCardMatch(AssetLocation wildCard)
        {
            if (wildCard == Code) return true;

            if (Code == null || wildCard.Domain != Code.Domain) return false;

            string pattern = Regex.Escape(wildCard.Path).Replace(@"\*", @"(.*)");

            return Regex.IsMatch(Code.Path, @"^" + pattern + @"$", RegexOptions.IgnoreCase);
        }

        public static bool WildCardMatch(string wildCard, string text)
        {
            if (wildCard == text) return true;
            string pattern = Regex.Escape(wildCard).Replace(@"\*", @"(.*)");
            return Regex.IsMatch(text, @"^" + pattern + @"$", RegexOptions.IgnoreCase);
        }

        public static bool WildCardMatches(string blockCode, List<string> wildCards, out string matchingWildcard)
        {
            foreach (string wildcard in wildCards)
            {
                if (WildCardMatch(wildcard, blockCode))
                {
                    matchingWildcard = wildcard;
                    return true;
                }
            }
            matchingWildcard = null;
            return false;
        }

        public static bool WildCardMatch(AssetLocation wildCard, AssetLocation blockCode)
        {
            if (wildCard == blockCode) return true;

            string pattern = Regex.Escape(wildCard.Path).Replace(@"\*", @"(.*)");

            return Regex.IsMatch(blockCode.Path, @"^" + pattern + @"$", RegexOptions.IgnoreCase);
        }

        public static bool WildCardMatches(AssetLocation blockCode, List<AssetLocation> wildCards, out AssetLocation matchingWildcard)
        {
            foreach (AssetLocation wildcard in wildCards)
            {
                if (WildCardMatch(wildcard, blockCode))
                {
                    matchingWildcard = wildcard;
                    return true;
                }
            }

            matchingWildcard = null;

            return false;
        }

    }
}
