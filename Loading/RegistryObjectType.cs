using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.ServerMods.NoObf
{

    /// <summary>
    /// The very base class for an in-game object. Extended by blocktypes, itemtypes, and entitytypes.
    /// Controls the object's code, variant types, allowed/disallowed variants, world interactions, and a class for extra functionality.
    /// </summary>
    /// <example>
    /// <code language="json">
    ///"code": "cheese",
    ///"class": "ItemCheese",
    ///"variantgroups": [
    ///	{
    ///		"code": "type",
    ///		"states": [ "cheddar", "blue", "waxedcheddar" ]
    ///	},
    ///	{
    ///		"code": "part",
    ///		"states": [ "1slice", "2slice", "3slice", "4slice" ]
    ///	}
    ///],
    ///"skipVariants": [
    ///	"cheese-waxedcheddar-1slice",
    ///	"cheese-waxedcheddar-2slice",
    ///	"cheese-waxedcheddar-3slice"
    ///],
    /// </code>
    /// </example>
    [DocumentAsJson]
    [JsonObject(MemberSerialization.OptIn)]
    public abstract class RegistryObjectType
    {
        /// <summary>
        /// Used to synchronise threads when parsing on server startup.  Starts at 0.  It will be set to 1 when a thread parses the CollectibleType.  It is not necessary to copy this value when cloning.
        /// </summary>
        volatile internal int parseStarted;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>true</jsondefault>-->
        /// If set to false, this object will not be loaded.
        /// </summary>
        [DocumentAsJson]
        public bool Enabled = true;

        public JObject jsonObject;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// The unique code for this object. Used as the prefix for any variant codes.
        /// </summary>
        [DocumentAsJson]
        public AssetLocation Code;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// All available variants for this object.
        /// </summary>
        [DocumentAsJson]
        public RegistryObjectVariantGroup[] VariantGroups;

        /// <summary>
        /// Variant values as resolved from blocktype/itemtype or entitytype
        /// </summary>
        public API.Datastructures.OrderedDictionary<string, string> Variant = new ();

        /// <summary>
        /// <!--<jsonoptional>Unused</jsonoptional><jsondefault>None</jsondefault>-->
        /// (Currently unused) A set of potential world interactions for this object. Used to display what the object is used for - e.g. Shift + Right Click to Knap Stones.
        /// </summary>
        [JsonProperty]
        public WorldInteraction[] Interactions;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// A set of resolved code-variants that will not be loaded by the game.
        /// </summary>
        [JsonProperty]
        public AssetLocation[] SkipVariants;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// If set, only resolved code-variants in this list will be loaded by the game.
        /// </summary>
        [JsonProperty]
        public AssetLocation[] AllowedVariants;

        public HashSet<AssetLocation> AllowedVariantsQuickLookup = new HashSet<AssetLocation>();

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// A reference to the registered C# class of the object. Can be used to add extra functionality to objects.
        /// </summary>
        [JsonProperty]
        public string Class;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// List of tags that this type belongs to. Used for categorizing objects.
        /// </summary>
        [JsonProperty]
        public string[] Tags = Array.Empty<string>();

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

        /*public static bool WildCardMatch(string wildCard, string text)
        {
            if (wildCard == text) return true;
            string pattern = Regex.Escape(wildCard).Replace(@"\*", @"(.*)");
            return Regex.IsMatch(text, @"^" + pattern + @"$", RegexOptions.IgnoreCase);
        }*/

        public static bool WildCardMatches(string blockCode, List<string> wildCards, out string matchingWildcard)
        {
            foreach (string wildcard in wildCards)
            {
                if (WildcardUtil.Match(wildcard, blockCode))
                //if (WildCardMatch(wildcard, blockCode))
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

        #region loading
        internal virtual void CreateBasetype(ICoreAPI api, string filepathForLogging, string entryDomain, JObject entityTypeObject)
        {
            loadInherits(api, ref entityTypeObject, entryDomain, filepathForLogging);

            AssetLocation location;
            try
            {
                location = entityTypeObject.GetValue("code", StringComparison.InvariantCultureIgnoreCase).ToObject<AssetLocation>(entryDomain);
            }
            catch (Exception e)
            {
                throw new Exception("Asset has no valid code property. Will ignore. Exception thrown:-", e);
            }


            Code = location;

            if (entityTypeObject.TryGetValue("variantgroups", StringComparison.InvariantCultureIgnoreCase, out JToken property))
            {
                VariantGroups = property.ToObject<RegistryObjectVariantGroup[]>();
                entityTypeObject.Remove(property.Path);
            }
            if (entityTypeObject.TryGetValue("skipVariants", StringComparison.InvariantCultureIgnoreCase, out property))
            {
                SkipVariants = property.ToObject<AssetLocation[]>(entryDomain);
                entityTypeObject.Remove(property.Path);
            }
            if (entityTypeObject.TryGetValue("allowedVariants", StringComparison.InvariantCultureIgnoreCase, out property))
            {
                AllowedVariants = property.ToObject<AssetLocation[]>(entryDomain);
                entityTypeObject.Remove(property.Path);
            }

            if (entityTypeObject.TryGetValue("enabled", StringComparison.InvariantCultureIgnoreCase, out property))
            {
                Enabled = property.ToObject<bool>();
                entityTypeObject.Remove(property.Path);
            }
            else Enabled = true;

            jsonObject = entityTypeObject;
        }

        private void loadInherits(ICoreAPI api, ref JObject entityTypeObject, string entryDomain, string parentFileNameForLogging)
        {
            if (entityTypeObject.TryGetValue("inheritFrom", StringComparison.InvariantCultureIgnoreCase, out var iftok))
            {
                AssetLocation inheritFrom = iftok.ToObject<AssetLocation>(entryDomain).WithPathAppendixOnce(".json");

                var asset = api.Assets.TryGet(inheritFrom);
                if (asset != null)
                {
                    try
                    {
                        var inheritedObj = JObject.Parse(asset.ToText());
                        loadInherits(api, ref inheritedObj, entryDomain, inheritFrom.ToShortString());

                        inheritedObj.Merge(entityTypeObject, new JsonMergeSettings()
                        {
                            MergeArrayHandling = MergeArrayHandling.Replace,
                            PropertyNameComparison = StringComparison.InvariantCultureIgnoreCase
                        });
                        entityTypeObject = inheritedObj;
                        entityTypeObject.Remove("inheritFrom");
                    }
                    catch (Exception e)
                    {
                        api.Logger.Error(Lang.Get("File {0} wants to inherit from {1}, but this is not valid json. Exception: {2}.", parentFileNameForLogging, inheritFrom, e));
                    }
                }
                else
                {
                    api.Logger.Error(Lang.Get("File {0} wants to inherit from {1}, but this file does not exist. Will ignore.", parentFileNameForLogging, inheritFrom));
                }
            }

        }

            internal virtual RegistryObjectType CreateAndPopulate(ICoreServerAPI api, AssetLocation fullcode, JObject jobject, JsonSerializer deserializer, API.Datastructures.OrderedDictionary<string, string> variant)
    {
        return this;
    }


    /// <summary>
    /// Create and populate the resolved type
    /// </summary>
    protected T CreateResolvedType<T>(ICoreServerAPI api, AssetLocation fullcode, JObject jobject, JsonSerializer deserializer, OrderedDictionary<string, string> variant) where T : RegistryObjectType, new()
    {
        T resolvedType = new T()
        {
            Code = Code,
            VariantGroups = VariantGroups,
            Enabled = Enabled,
            jsonObject = jobject, // This is now already resolved JSON
            Variant = variant
        };

        // solvedbytype is no longer needed since we already resolved the JSON earlier

        try
        {
            JsonUtil.PopulateObject(resolvedType, jobject, deserializer);
        }
        catch (Exception e)
        {
            api.Server.Logger.Error("Exception thrown while trying to parse json data of the type with code {0}, variant {1}. Will ignore most of the attributes. Exception:", this.Code, fullcode);
            api.Server.Logger.Error(e);
        }

        resolvedType.Code = fullcode;
        resolvedType.jsonObject = null;
        return resolvedType;
    }

    /// <summary>
    /// Checks if the token needs resolution (contains "byType" or placeholders "{...}").
    /// This is a static check, independent of codePath and variant.
    /// </summary>
    private static bool NeedsResolve(JToken token)
    {
        if (token == null) return false;

        // Check if the object has properties ending with "byType"
        if (token is JObject obj)
        {
            foreach (var prop in obj.Properties())
            {
                if (prop.Name.EndsWith("byType", StringComparison.OrdinalIgnoreCase)) return true;
                if (NeedsResolve(prop.Value)) return true;
            }
            return false;
        }
        else if (token is JArray arr) // Check array elements
        {
            // If at least one element needs resolution, return true
            foreach (var item in arr)
            {
                if (NeedsResolve(item)) return true;
            }
            return false;
        }
        else if (token is JValue val && val.Type == JTokenType.String) // Check string values for placeholders
        {
            string str = (string)val.Value;
            return str != null && str.Contains("{");
        }

        return false;
    }

    /// <summary>
    /// Lazily resolves the JSON token for the given codePath and variant
    /// Creates new containers (JObject/JArray) only for parts requiring changes
    /// Unchanged subtrees are shared 
    /// </summary>
    public static JToken Resolve(JToken token, string codePath, OrderedDictionary<string, string> searchReplace)
    {
        if (token == null)
            return null;

        // Check if the token needs resolution
        if (token is JObject obj)
        {
            // Check if this object needs resolution at all
            bool hasByType = false;
            bool needsChildResolve = false;
            foreach (var prop in obj.Properties())
            {
                if (prop.Name.EndsWith("byType", StringComparison.OrdinalIgnoreCase))
                {
                    hasByType = true;
                }
                if (NeedsResolve(prop.Value))
                {
                    needsChildResolve = true;
                }
            }

            if (!hasByType && !needsChildResolve)
            {
                return token; // Share the original object since nothing changes
            }

            // Create a new JObject only if needed
            var newObj = new JObject();
            Dictionary<string, JToken> propertiesToAdd = null;

            foreach (var prop in obj.Properties())
            {
                var key = prop.Name;
                if (key.EndsWith("byType", StringComparison.OrdinalIgnoreCase))
                {
                    string trueKey = key.Substring(0, key.Length - "byType".Length);
                    var byTypeObj = prop.Value as JObject;
                    if (byTypeObj == null) continue;

                    JToken selected = null;
                    foreach (var byTypeProp in byTypeObj.Properties())
                    {
                        if (WildcardUtil.Match(byTypeProp.Name, codePath))
                        {
                            selected = byTypeProp.Value;
                            break; // First matched
                        }
                    }

                    if (selected != null)
                    {
                        if (propertiesToAdd == null) propertiesToAdd = new Dictionary<string, JToken>();
                        propertiesToAdd[trueKey] = selected; // Store JToken directly
                    }
                }
                else
                {
                    newObj[key] = Resolve(prop.Value, codePath, searchReplace);
                }
            }

            // Add the resolved "byType" properties
            if (propertiesToAdd != null)
            {
                foreach (var add in propertiesToAdd)
                {
                    string trueKey = add.Key;
                    JToken selected = add.Value;
                    JToken resolvedSelected = Resolve(selected, codePath, searchReplace);

                    if (newObj[trueKey] is JObject existing)
                    {
                        existing.Merge(resolvedSelected);  // No mergeSettings, to match original (default Concat for arrays)
                    }
                    else
                    {
                        newObj[trueKey] = resolvedSelected;
                    }
                }
            }

            return newObj;
        }
        else if (token is JArray arr)
        {
            // Check if the array needs resolution
            bool needs = false;
            foreach (var item in arr)
            {
                if (NeedsResolve(item))
                {
                    needs = true;
                    break;
                }
            }

            if (!needs) return token; // Share original

            var newArr = new JArray();
            foreach (var item in arr)
            {
                newArr.Add(Resolve(item, codePath, searchReplace));
            }
            return newArr;
        }
        else if (token is JValue val && val.Type == JTokenType.String) // String value
        {
            string str = (string)val.Value;
            if (str != null && str.Contains("{"))
            {
                return new JValue(RegistryObject.FillPlaceHolderOptimized(str, searchReplace)); // Using optimized version of placeholder replacement method
            }
            return token; // Share original
        }
        else
        {
            return token; // Primitives shared
        }
    }
        #endregion

    }
}
