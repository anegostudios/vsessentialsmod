using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.ServerMods.NoObf
{
    public class VariantEntry
    {
        public string Code;

        public List<string> Codes;
        public List<string> Types;
    }

    public class ResolvedVariant
    {
        public OrderedDictionary<string, string> CodeParts = new OrderedDictionary<string, string>();

        public AssetLocation Code;

        public void ResolveCode(AssetLocation baseCode)
        {
            Code = baseCode.Clone();
            foreach (string code in CodeParts.Values)
            {
                if (code.Length > 0)
                {
                    Code.Path += "-" + code;
                }
            }
        }
    }


    public class ModRegistryObjectTypeLoader : ModSystem
    {
        // Dict Key is filename (with .json)
        public Dictionary<AssetLocation, StandardWorldProperty> worldProperties;
        public Dictionary<AssetLocation, VariantEntry[]> worldPropertiesVariants;

        Dictionary<AssetLocation, RegistryObjectType> blockTypes;
        Dictionary<AssetLocation, RegistryObjectType> itemTypes;
        Dictionary<AssetLocation, RegistryObjectType> entityTypes;
        List<RegistryObjectType>[] itemVariants;
        List<RegistryObjectType>[] blockVariants;
        List<RegistryObjectType>[] entityVariants;


        ICoreServerAPI api;


        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }


        public override double ExecuteOrder()
        {
            return 0.2;
        }

        public override void AssetsLoaded(ICoreAPI coreApi)
        {
            if (!(coreApi is ICoreServerAPI api)) return;
            this.api = api;

            api.Logger.VerboseDebug("Starting to gather blocktypes, itemtypes and entities");
            LoadWorldProperties();
            int maxThreads = api.Server.IsDedicated ? 3 : 8;
            int threads = GameMath.Clamp(Environment.ProcessorCount / 2 - 2, 1, maxThreads);

            itemTypes = new Dictionary<AssetLocation, RegistryObjectType>();
            blockTypes = new Dictionary<AssetLocation, RegistryObjectType>();
            entityTypes = new Dictionary<AssetLocation, RegistryObjectType>();

            foreach (KeyValuePair<AssetLocation, JObject> entry in api.Assets.GetMany<JObject>(api.Server.Logger, "itemtypes/"))
            {
                if (!entry.Key.Path.EndsWith(".json")) continue;

                try
                {
                    ItemType et = new ItemType();
                    et.CreateBasetype(entry.Key.Domain, entry.Value);
                    itemTypes.Add(entry.Key, et);
                }
                catch (Exception e)
                {
                    api.World.Logger.Error("Item type {0} could not be loaded. Will ignore. Exception thrown: {1}", entry.Key, e);
                    continue;
                }
            }
            itemVariants = new List<RegistryObjectType>[itemTypes.Count];
            api.Logger.VerboseDebug("Starting parsing ItemTypes in " + threads + " threads");
            PrepareForLoading(threads);

            foreach (KeyValuePair<AssetLocation, JObject> entry in api.Assets.GetMany<JObject>(api.Server.Logger, "entities/"))
            {
                if (!entry.Key.Path.EndsWith(".json")) continue;

                try
                {
                    EntityType et = new EntityType();
                    et.CreateBasetype(entry.Key.Domain, entry.Value);
                    entityTypes.Add(entry.Key, et);
                }
                catch (Exception e)
                {
                    api.World.Logger.Error("Entity type {0} could not be loaded. Will ignore. Exception thrown: {1}", entry.Key, e);
                    continue;
                }
            }
            entityVariants = new List<RegistryObjectType>[entityTypes.Count];

            foreach (KeyValuePair<AssetLocation, JObject> entry in api.Assets.GetMany<JObject>(api.Server.Logger, "blocktypes/"))
            {
                if (!entry.Key.Path.EndsWith(".json")) continue;
                
                try
                {
                    BlockType et = new BlockType();
                    et.CreateBasetype(entry.Key.Domain, entry.Value);
                    blockTypes.Add(entry.Key, et);
                }
                catch (Exception e)
                {
                    api.World.Logger.Error("Block type {0} could not be loaded. Will ignore. Exception thrown: {1}", entry.Key, e);
                    continue;
                }
            }
            blockVariants = new List<RegistryObjectType>[blockTypes.Count];

            TyronThreadPool.QueueTask(GatherAllTypes_Async);   // Now we've loaded everything, let's add one more gathering thread :)

            api.Logger.StoryEvent(Lang.Get("It remembers..."));
            api.Logger.VerboseDebug("Gathered all types, starting to load items");

            LoadItems(itemVariants);
            api.Logger.VerboseDebug("Parsed and loaded items");

            api.Logger.StoryEvent(Lang.Get("...all that came before"));

            LoadBlocks(blockVariants);
            api.Logger.VerboseDebug("Parsed and loaded blocks");

            LoadEntities(entityVariants);
            api.Logger.VerboseDebug("Parsed and loaded entities");

            api.Server.LogNotification("BlockLoader: Entities, Blocks and Items loaded");

            FreeRam();

            api.TriggerOnAssetsFirstLoaded();
        }

        private void LoadWorldProperties()
        {
            worldProperties = new Dictionary<AssetLocation, StandardWorldProperty>();

            foreach (var entry in api.Assets.GetMany<StandardWorldProperty>(api.Server.Logger, "worldproperties/"))
            {
                AssetLocation loc = entry.Key.Clone();
                loc.Path = loc.Path.Replace("worldproperties/", "");
                loc.RemoveEnding();
                
                entry.Value.Code.Domain = entry.Key.Domain;

                worldProperties.Add(loc, entry.Value);
            }

            worldPropertiesVariants = new Dictionary<AssetLocation, VariantEntry[]>();
            foreach (var val in worldProperties)
            {
                if (val.Value == null) continue;

                WorldPropertyVariant[] variants = val.Value.Variants;
                if (variants == null) continue;

                if (val.Value.Code == null)
                {
                    api.Server.LogError("Error in worldproperties {0}, code is null, so I won't load it", val.Key);
                    continue;
                }

                worldPropertiesVariants[val.Value.Code] = new VariantEntry[variants.Length];

                for (int i = 0; i < variants.Length; i++)
                {
                    if (variants[i].Code == null)
                    {
                        api.Server.LogError("Error in worldproperties {0}, variant {1}, code is null, so I won't load it", val.Key, i);
                        worldPropertiesVariants[val.Value.Code] = worldPropertiesVariants[val.Value.Code].RemoveEntry(i);
                        continue;
                    }

                    worldPropertiesVariants[val.Value.Code][i] = new VariantEntry() { Code = variants[i].Code.Path };
                }
            }
        }


        #region Entities
        void LoadEntities(List<RegistryObjectType>[] variantLists)
        {
            LoadFromVariants(variantLists, "entitie", (variants) =>
            {
                foreach (EntityType type in variants)
                {
                    api.RegisterEntityClass(type.Class, type.CreateProperties());
                }
            });
        }

        #endregion

        #region Items
        void LoadItems(List<RegistryObjectType>[] variantLists)
        {
            // Step2: create all the items from the itemtypes, and register them: this has to be on the main thread as the registry is not thread-safe
            LoadFromVariants(variantLists, "item", (variants) =>
            {
                foreach (ItemType type in variants)
                {
                    Item item = type.CreateItem(api);

                    try
                    {
                        api.RegisterItem(item);
                    }
                    catch (Exception e)
                    {
                        api.Server.LogError("Failed registering item {0}: {1}", item.Code, e);
                    }
                }
            });
        }
        #endregion

        #region Blocks

        void LoadBlocks(List<RegistryObjectType>[] variantLists)
        {
            LoadFromVariants(variantLists, "block", (variants) =>
            {
                foreach (BlockType type in variants)
                {
                    Block block = type.CreateBlock(api);

                    try
                    {
                        api.RegisterBlock(block);
                    }
                    catch (Exception e)
                    {
                        api.Server.LogError("Failed registering block {0}: {1}", block.Code, e);
                    }
                }
            });
        }
        #endregion

        #region generic
        void PrepareForLoading(int threadsCount)
        {
            // JSON parsing is slooowww, so let's multithread the **** out of it :)
            for (int i = 0; i < threadsCount; i++) TyronThreadPool.QueueTask(GatherAllTypes_Async);
        }


        private void GatherAllTypes_Async()
        {
            GatherTypes_Async(itemVariants, itemTypes);

            int timeOut = 1000;
            bool logged = false;
            while (blockVariants == null)
            {
                if (--timeOut == 0) return;
                if (!logged)
                {
                    api.Logger.VerboseDebug("Waiting for entityTypes to be gathered");
                    logged = true;
                }
                Thread.Sleep(10);
            }
            if (logged) api.Logger.VerboseDebug("EntityTypes now all gathered");

            GatherTypes_Async(blockVariants, blockTypes);

            timeOut = 1000;
            logged = false;
            while (entityVariants == null)
            {
                if (--timeOut == 0) return;
                if (!logged)
                {
                    api.Logger.VerboseDebug("Waiting for blockTypes to be gathered");
                    logged = true;
                }
                Thread.Sleep(10);
            }
            if (logged) api.Logger.VerboseDebug("BlockTypes now all gathered");

            GatherTypes_Async(entityVariants, entityTypes);
        }


        /// <summary>
        /// Each thread attempts to resolve and parse the whole list of types, using the parseStarted field for load-sharing
        /// </summary>
        private void GatherTypes_Async(List<RegistryObjectType>[] resolvedTypeLists, Dictionary<AssetLocation, RegistryObjectType> baseTypes)
        {
            int i = 0;
            foreach (RegistryObjectType val in baseTypes.Values)
            {
                if (Interlocked.CompareExchange(ref val.parseStarted, 1, 0) == 0)  // In each thread, only do work on RegistryObjectTypes which no other thread has yet worked on
                {
                    List<RegistryObjectType> resolvedTypes = new List<RegistryObjectType>();
                    try
                    {
                        if (val.Enabled) GatherVariantsAndPopulate(val, resolvedTypes);
                    }
                    finally
                    {
                        resolvedTypeLists[i] = resolvedTypes;
                    }
                }
                i++;
            }
        }


        /// <summary>
        /// This does the actual gathering and population, through GatherVariants calls - numerous calls if a base type has many variants
        /// </summary>
        /// <param name="baseType"></param>
        /// <param name="typesResolved"></param>
        void GatherVariantsAndPopulate(RegistryObjectType baseType, List<RegistryObjectType> typesResolved)
        {
            List<ResolvedVariant> variants = null;
            if (baseType.VariantGroups != null && baseType.VariantGroups.Length != 0)
            {
                try
                {
                    variants = GatherVariants(baseType.Code, baseType.VariantGroups, baseType.Code, baseType.AllowedVariants, baseType.SkipVariants);
                }
                catch (Exception e)
                {
                    api.Server.Logger.Error("Exception thrown while trying to gather all variants of the block/item/entity type with code {0}. May lead to the whole type being ignored. Exception: {1}", baseType.Code, e);
                    return;
                }
            }

            var deserializer = JsonUtil.CreateSerializerForDomain(baseType.Code.Domain);

            // Single variant
            if (variants == null || variants.Count == 0)
            {
                RegistryObjectType resolvedType = baseType.CreateAndPopulate(api, baseType.Code.Clone(), baseType.jsonObject, deserializer, new OrderedDictionary<string, string>());
                typesResolved.Add(resolvedType);
            }
            else
            {
                // Multiple variants
                int count = 1;
                foreach (ResolvedVariant variant in variants)
                {
                    JObject jobject = count++ == variants.Count ? baseType.jsonObject : baseType.jsonObject.DeepClone() as JObject;
                    // This DeepClone() is expensive, can we find a better way one day?

                    RegistryObjectType resolvedType = baseType.CreateAndPopulate(api, variant.Code, jobject, deserializer, variant.CodeParts);

                    typesResolved.Add(resolvedType);
                }
            }

            baseType.jsonObject = null;
        }


        void LoadFromVariants(List<RegistryObjectType>[] variantLists, string typeForLog, Action<List<RegistryObjectType>> register)
        {
            int count = 0;
            for (int i = 0; i < variantLists.Length; i++)
            {
                List<RegistryObjectType> variants = variantLists[i];
                while (variants == null)
                {
                    Thread.Sleep(10);   // If necessary, wait for all threads to finish
                    variants = variantLists[i];
                    if (variants != null && variants.Count > 0) api.Logger.VerboseDebug("Took time to parse " + variants.Count + " variants of " + variants[0].Code.FirstCodePart());
                }

                count += variants.Count;

                register.Invoke(variants);
            }

            api.Server.LogNotification("Loaded " + count + " unique " + typeForLog + "s");
        }
        #endregion


        public StandardWorldProperty GetWorldPropertyByCode(AssetLocation code)
        {
            StandardWorldProperty property = null;
            worldProperties.TryGetValue(code, out property);
            return property;
        }




        List<ResolvedVariant> GatherVariants(AssetLocation baseCode, RegistryObjectVariantGroup[] variantgroups, AssetLocation location, AssetLocation[] allowedVariants, AssetLocation[] skipVariants)
        {
            List<ResolvedVariant> variantsFinal = new List<ResolvedVariant>();

            OrderedDictionary<string, VariantEntry[]> variantsMul = new OrderedDictionary<string, VariantEntry[]>();


            // 1. Collect all types
            for (int i = 0; i < variantgroups.Length; i++)
            {
                if (variantgroups[i].LoadFromProperties != null)
                {
                    CollectFromWorldProperties(variantgroups[i], variantgroups, variantsMul, variantsFinal, location);
                }

                if (variantgroups[i].LoadFromPropertiesCombine != null)
                {
                    CollectFromWorldPropertiesCombine(variantgroups[i].LoadFromPropertiesCombine, variantgroups[i], variantgroups, variantsMul, variantsFinal, location);
                }

                if (variantgroups[i].States != null)
                {
                    CollectFromStateList(variantgroups[i], variantgroups, variantsMul, variantsFinal, location);
                }
            }

            // 2. Multiply multiplicative groups
            VariantEntry[,] variants = MultiplyProperties(variantsMul.Values.ToArray());


            // 3. Add up multiplicative groups
            for (int i = 0; i < variants.GetLength(0); i++)
            {
                ResolvedVariant resolved = new ResolvedVariant();
                for (int j = 0; j < variants.GetLength(1); j++)
                {
                    VariantEntry variant = variants[i, j];

                    if (variant.Codes != null)
                    {
                        for (int k = 0; k < variant.Codes.Count; k++)
                        {
                            resolved.CodeParts.Add(variant.Types[k], variant.Codes[k]);
                        }
                    }
                    else
                    {
                        resolved.CodeParts.Add(variantsMul.GetKeyAtIndex(j), variant.Code);
                    }
                }

                variantsFinal.Add(resolved);
            }

            foreach (ResolvedVariant var in variantsFinal)
            {
                var.ResolveCode(baseCode);
            }

            
            if (skipVariants != null)
            {
                List<ResolvedVariant> filteredVariants = new List<ResolvedVariant>();

                HashSet<AssetLocation> skipVariantsHash = new HashSet<AssetLocation>();
                List<AssetLocation> skipVariantsWildCards = new List<AssetLocation>();
                foreach(var val in skipVariants)
                {
                    if (val.IsWildCard)
                    {
                        skipVariantsWildCards.Add(val);
                    } else
                    {
                        skipVariantsHash.Add(val);
                    }
                }

                foreach (ResolvedVariant var in variantsFinal)
                {
                    if (skipVariantsHash.Contains(var.Code)) continue;
                    if (skipVariantsWildCards.FirstOrDefault(v => WildcardUtil.Match(v, var.Code)) != null) continue;

                    filteredVariants.Add(var);
                }

                variantsFinal = filteredVariants;
            }


            if (allowedVariants != null)
            {
                List<ResolvedVariant> filteredVariants = new List<ResolvedVariant>();

                HashSet<AssetLocation> allowVariantsHash = new HashSet<AssetLocation>();
                List<AssetLocation> allowVariantsWildCards = new List<AssetLocation>();
                foreach (var val in allowedVariants)
                {
                    if (val.IsWildCard)
                    {
                        allowVariantsWildCards.Add(val);
                    }
                    else
                    {
                        allowVariantsHash.Add(val);
                    }
                }

                foreach (ResolvedVariant var in variantsFinal)
                {
                    if (allowVariantsHash.Contains(var.Code) || allowVariantsWildCards.FirstOrDefault(v => WildcardUtil.Match(v, var.Code)) != null)
                    {
                        filteredVariants.Add(var);
                    }
                }

                variantsFinal = filteredVariants;
            }
            
            return variantsFinal;
        }

        private void CollectFromStateList(RegistryObjectVariantGroup variantGroup, RegistryObjectVariantGroup[] variantgroups, OrderedDictionary<string, VariantEntry[]> variantsMul, List<ResolvedVariant> blockvariantsFinal, AssetLocation filename)
        {
            if (variantGroup.Code == null)
            {
                api.Server.LogError(
                    "Error in itemtype {0}, a variantgroup using a state list must have a code. Ignoring.",
                    filename
                );
                return;
            }

            string[] states = variantGroup.States;
            string type = variantGroup.Code;

            // Additive state list
            if (variantGroup.Combine == EnumCombination.Add)
            {
                for (int j = 0; j < states.Length; j++)
                {
                    ResolvedVariant resolved = new ResolvedVariant();
                    resolved.CodeParts.Add(type, states[j]);
                    blockvariantsFinal.Add(resolved);
                }
            }

            // Multiplicative state list
            if (variantGroup.Combine == EnumCombination.Multiply)
            {
                List<VariantEntry> stateList = new List<VariantEntry>();

                for (int j = 0; j < states.Length; j++)
                {
                    stateList.Add(new VariantEntry() { Code = states[j] });
                }


                for (int i = 0; i < variantgroups.Length; i++)
                {
                    RegistryObjectVariantGroup cvg = variantgroups[i];
                    if (cvg.Combine == EnumCombination.SelectiveMultiply && cvg.OnVariant == variantGroup.Code)
                    {
                        for (int k = 0; k < stateList.Count; k++)
                        {
                            VariantEntry old = stateList[k];

                            if (cvg.Code != old.Code) continue;
                            
                            stateList.RemoveAt(k);

                            for (int j = 0; j < cvg.States.Length; j++)
                            {
                                List<string> codes = old.Codes ?? new List<string>() { old.Code };
                                List<string> types = old.Types ?? new List<string>() { variantGroup.Code };

                                string state = cvg.States[j];
                                codes.Add(state);
                                types.Add(cvg.Code);

                                stateList.Insert(k, new VariantEntry()
                                {
                                    Code = state.Length == 0 ? old.Code : old.Code + "-" + state,
                                    Codes = codes,
                                    Types = types
                                });
                            }
                        }
                    }
                }

                if (variantsMul.ContainsKey(type))
                {
                    stateList.AddRange(variantsMul[type]);
                    variantsMul[type] = stateList.ToArray();
                }
                else
                {
                    variantsMul.Add(type, stateList.ToArray());
                }

            }
        }


        private void CollectFromWorldProperties(RegistryObjectVariantGroup variantGroup, RegistryObjectVariantGroup[] variantgroups, OrderedDictionary<string, VariantEntry[]> blockvariantsMul, List<ResolvedVariant> blockvariantsFinal, AssetLocation location)
        {
            CollectFromWorldPropertiesCombine(new AssetLocation[] { variantGroup.LoadFromProperties }, variantGroup, variantgroups, blockvariantsMul, blockvariantsFinal, location);
        }

        private void CollectFromWorldPropertiesCombine(AssetLocation[] propList, RegistryObjectVariantGroup variantGroup, RegistryObjectVariantGroup[] variantgroups, OrderedDictionary<string, VariantEntry[]> blockvariantsMul, List<ResolvedVariant> blockvariantsFinal, AssetLocation location)
        {
            if (propList.Length > 1 && variantGroup.Code == null)
            {
                api.Server.LogError(
                    "Error in item or block {0}, defined a variantgroup with loadFromPropertiesCombine (first element: {1}), but did not explicitly declare a code for this variant group, hence I do not know which code to use. Ignoring.",
                    location, propList[0]
                );
                return;
            }

            foreach (var val in propList)
            {
                StandardWorldProperty property = GetWorldPropertyByCode(val);

                if (property == null)
                {
                    api.Server.LogError(
                        "Error in item or block {0}, worldproperty {1} does not exist (or is empty). Ignoring.",
                        location, variantGroup.LoadFromProperties
                    );
                    return;
                }

                string typename = variantGroup.Code == null ? property.Code.Path : variantGroup.Code;

                if (variantGroup.Combine == EnumCombination.Add)
                {

                    foreach (WorldPropertyVariant variant in property.Variants)
                    {
                        ResolvedVariant resolved = new ResolvedVariant();
                        resolved.CodeParts.Add(typename, variant.Code.Path);
                        blockvariantsFinal.Add(resolved);
                    }
                }

                if (variantGroup.Combine == EnumCombination.Multiply)
                {
                    VariantEntry[] variants;
                    if (blockvariantsMul.TryGetValue(typename, out variants))
                    {
                        blockvariantsMul[typename] = variants.Append(worldPropertiesVariants[property.Code]);
                    } else
                    {
                        blockvariantsMul.Add(typename, worldPropertiesVariants[property.Code]);
                    }
                    
                }
            }
        }


        // Takes n lists of properties and returns every unique n-tuple 
        // through a 2 dimensional array blockvariants[i, ni] 
        // where i = n-tuple index and ni = index of current element in the n-tuple
        VariantEntry[,] MultiplyProperties(VariantEntry[][] variants)
        {
            int resultingQuantiy = 1;

            for (int i = 0; i < variants.Length; i++)
            {
                resultingQuantiy *= variants[i].Length;
            }

            VariantEntry[,] multipliedProperties = new VariantEntry[resultingQuantiy, variants.Length];

            for (int i = 0; i < resultingQuantiy; i++)
            {
                int index = i;

                for (int j = 0; j < variants.Length; j++)
                {
                    int variantLength = variants[j].Length;
                    VariantEntry variant = variants[j][index % variantLength];

                    multipliedProperties[i, j] = new VariantEntry() { Code = variant.Code, Codes = variant.Codes, Types = variant.Types };

                    index /= variantLength;
                }
            }

            return multipliedProperties;
        }



        private void FreeRam()
        {
            blockTypes = null;
            itemTypes = null;
            entityTypes = null;
            worldProperties = null;
            worldPropertiesVariants = null;
        }
    }
}