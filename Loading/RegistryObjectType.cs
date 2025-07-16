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

        protected T CreateResolvedType<T>(ICoreServerAPI api, AssetLocation fullcode, JObject jobject, JsonSerializer deserializer, API.Datastructures.OrderedDictionary<string, string> variant) where T : RegistryObjectType, new()
        {
            T resolvedType = new T()
            {
                Code = Code,
                VariantGroups = VariantGroups,
                Enabled = Enabled,
                jsonObject = jobject,
                Variant = variant
            };

            try
            {
                solveByType(jobject, fullcode.Path, variant);
            }
            catch (Exception e)
            {
                api.Server.Logger.Error("Exception thrown while trying to resolve *byType properties of type {0}, variant {1}. Will ignore most of the attributes. Exception thrown:", this.Code, fullcode);
                api.Server.Logger.Error(e);
            }

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

        protected static void solveByType(JToken json, string codePath, API.Datastructures.OrderedDictionary<string, string> searchReplace)
        {
            if (json is JObject jsonObj)
            {
                List<string> propertiesToRemove = null;
                Dictionary<string, JToken> propertiesToAdd = null;

                foreach (var entry in jsonObj)
                {
                    if (entry.Key.EndsWith("byType", StringComparison.OrdinalIgnoreCase))
                    {
                        string trueKey = entry.Key.Substring(0, entry.Key.Length - "byType".Length);
                        var jobj = entry.Value as JObject;
                        if (jobj == null)
                        {
                            throw new FormatException("Invalid value at key: " + entry.Key);
                        }
                        foreach (var byTypeProperty in jobj)
                        {
                            if (WildcardUtil.Match(byTypeProperty.Key, codePath))
                            {
                                JToken typedToken = byTypeProperty.Value;    // Unnecessary to solveByType specifically on this new token's contents as we will be doing a solveByType on all the tokens in the jsonObj anyhow, after adding the propertiesToAdd
                                if (propertiesToAdd == null) propertiesToAdd = new Dictionary<string, JToken>();
                                propertiesToAdd.Add(trueKey, typedToken);
                                break;   // Replaces for first matched key only
                            }
                        }
                        if (propertiesToRemove == null) propertiesToRemove = new List<string>();
                        propertiesToRemove.Add(entry.Key);
                    }
                }


                if (propertiesToRemove != null)
                {
                    foreach (var property in propertiesToRemove)
                    {
                        jsonObj.Remove(property);
                    }

                    if (propertiesToAdd != null)
                    {
                        foreach (var property in propertiesToAdd)
                        {
                            if (jsonObj[property.Key] is JObject jobject)
                            {
                                jobject.Merge(property.Value);
                            }
                            else
                            {
                                jsonObj[property.Key] = property.Value;
                            }
                        }
                    }
                }

                foreach (var entry in jsonObj)
                {
                    solveByType(entry.Value, codePath, searchReplace);
                }
            }
            else if (json.Type == JTokenType.String)
            {
                string value = (string)(json as JValue).Value;
                if (value.Contains("{"))
                {
                    (json as JValue).Value = RegistryObject.FillPlaceHolder(value, searchReplace);
                }
            }
            else if (json is JArray jarray)
            {
                foreach (var child in jarray)
                {
                    solveByType(child, codePath, searchReplace);
                }
            }
        }
        #endregion

    }
}
