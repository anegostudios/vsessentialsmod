using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JsonPatch.Operations;
using JsonPatch.Operations.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tavis;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using System.IO;

#nullable disable

namespace Vintagestory.ServerMods.NoObf
{
    /// <summary>
    /// A set of operations that define what a patch will do.
    /// See https://datatracker.ietf.org/doc/html/rfc6902#section-4.1 for more information on each operation type.
    /// </summary>
    [DocumentAsJson]
    public enum EnumJsonPatchOp
    {
        /// <summary>
        /// Add an element to a json property at a specific path. Please consider using <see cref="AddMerge"/> for improved mod compatability.
        /// </summary>
        Add,

        /// <summary>
        /// Add a set of objects to an array. Will not work if used on other data types.
        /// </summary>
        AddEach,

        /// <summary>
        /// Remove a json property at a specific path. Does not require a value to be set.
        /// </summary>
        Remove,

        /// <summary>
        /// Replaces a json property with one of a different value. Identical to a remove and then add.
        /// </summary>
        Replace,

        /// <summary>
        /// Copies a json property from one place and adds it to another. Requires the <see cref="JsonPatch.FromPath"/> property.
        /// </summary>
        Copy,

        /// <summary>
        /// Removes a json property from one place and adds it to another. Identical to removing from one place and adding it to another. Requires the <see cref="JsonPatch.FromPath"/> property.
        /// </summary>
        Move,

        /// <summary>
        /// Add merge is similar to <see cref="Add"/>, however if the target is an array, then the current value and patched value will merge together for improved compatibility.
        /// </summary>
        AddMerge
    }

    /// <summary>
    /// A condition for a json patch. Conditions are based on the currently loaded worldconfig.
    /// </summary>
    [DocumentAsJson]
    public class PatchCondition
    {
        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// The key for the world config that this condition relies on.
        /// </summary>
        [DocumentAsJson] public string When;

        /// <summary>
        /// <!--<jsonoptional>Recommended</jsonoptional><jsondefault>None</jsondefault>-->
        /// What value does the world config need to be for this patch to happen? Required if not using <see cref="useValue"/>. Will be ignored if using <see cref="useValue"/>.
        /// </summary>
        [DocumentAsJson] public string IsValue;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>False</jsondefault>-->
        /// If true, then this will replace the <see cref="JsonPatch.Value"/> with the value in the world config. Can be used to create more complex patches. Required if not using <see cref="IsValue"/>.
        /// </summary>
        [DocumentAsJson] public bool useValue;
    }

    /// <summary>
    /// A mod-dependence for a json patch. If your patch depends on another mod, you need to use this.
    /// </summary>
    [DocumentAsJson]
    public class PatchModDependence
    {
        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// The mod ID that this patch relies on.
        /// </summary>
        [DocumentAsJson] public string modid;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>False</jsondefault>-->
        /// If true, then the patch will only occur if the specified mod is *not* installed.
        /// </summary>
        [DocumentAsJson] public bool invert = false;
    }

    /// <summary>
    /// Defines a patch for a json asset. This allows modifying json files through mods without directly editing them.
    /// To help with creating patches, it is highly recommended to learn how to use the in-built modmaker program.
    /// See <see cref="https://wiki.vintagestory.at/Modding:Inbuilt_ModMaker"/> for more info.
    /// </summary>
    [DocumentAsJson]
    public class JsonPatch
    {
        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// The operation for the patch. Essentially controls what the patch actually does.
        /// </summary>
        [DocumentAsJson] public EnumJsonPatchOp Op;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// The asset location of the file where the patch should be applied.
        /// </summary>
        [DocumentAsJson] public AssetLocation File;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional>-->
        /// If using <see cref="EnumJsonPatchOp.Move"/> or <see cref="EnumJsonPatchOp.Copy"/>, this is the path to the json property to move or copy from.
        /// </summary>
        [DocumentAsJson] public string FromPath;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// This is the path to the json property where the operation will take place.
        /// </summary>
        [DocumentAsJson] public string Path;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// A list of mod dependencies for the patch. Can be used to create patches that are specific on certain mods being installed. Useful for compatibility!
        /// </summary>
        [DocumentAsJson] public PatchModDependence[] DependsOn;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>True</jsondefault>-->
        /// Should this patch be applied or not?
        /// </summary>
        [DocumentAsJson] public bool Enabled = true;

        /// <summary>
        /// <!--<jsonoptional>Obsolete</jsonoptional>-->
        /// The app side that the patch should be loaded on. Obsolete, please use <see cref="Side"/> instead.
        /// </summary>
        [Obsolete("Use Side instead")]
        [DocumentAsJson]
        public EnumAppSide? SideType
        {
            get { return Side; }
            set { Side = value; }
        }

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>Universal</jsondefault>-->
        /// The app side that the patch should be loaded on.
        /// </summary>
        [DocumentAsJson] public EnumAppSide? Side = EnumAppSide.Universal;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// A condition that this patch must satisfy to be applied. Uses specific values from the world config. Useful in conjunction with code mods.
        /// </summary>
        [DocumentAsJson] public PatchCondition Condition;

        /// <summary>
        /// <!--<jsonoptional>Recommended</jsonoptional><jsondefault>None</jsondefault>-->
        /// If adding, this is the value (or values) that will be added.
        /// </summary>
        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        public JsonObject Value;
    }

   public class ModJsonPatchLoader : ModSystem
 {
     ICoreAPI api;
     ITreeAttribute worldConfig;

     // Cache for loaded JSON documents
     private Dictionary<AssetLocation, (JToken token, IAsset asset)> _jsonCache = new Dictionary<AssetLocation, (JToken, IAsset)>();

     public override bool ShouldLoad(EnumAppSide side)
     {
         return true;
     }

     public override double ExecuteOrder() => 0.05;

     public override void AssetsLoaded(ICoreAPI api)
     {
         this.api = api;

         worldConfig = api.World.Config;
         if (worldConfig == null)
         {
             worldConfig = new TreeAttribute();
         }

         ApplyPatches();
     }


     /// <summary>
     ///
     /// </summary>
     /// <param name="forPartialPath">Only apply patches that patch a file starting with this path.</param>
     public void ApplyPatches(string forPartialPath = null)
     {
         // Load all patch files
         List<IAsset> entries = api.Assets.GetMany("patches/");

         int appliedCount = 0;
         int notfoundCount = 0;
         int errorCount = 0;
         int totalCount = 0;
         int unmetConditionCount = 0;

         // List of loaded mods for dependency checks
         HashSet<string> loadedModIds = new HashSet<string>(api.ModLoader.Mods.Select((m) => m.Info.ModID).ToList());

         // First collect all valid patches
         var validPatches = new List<(JsonPatch patch, AssetLocation source, int index)>();

         // Iterate over all patch files
         foreach (IAsset asset in entries)
         {
             JsonPatch[] patches = null;
             try
             {
                 patches = asset.ToObject<JsonPatch[]>();
             }
             catch (Exception e)
             {
                 api.Logger.Error("Failed loading patches file {0}:", asset.Location);
                 api.Logger.Error(e);
             }

             // Iterate over all patches in the file
             for (int j = 0; patches != null && j < patches.Length; j++)
             {
                 JsonPatch patch = patches[j];

                 // Skip disabled patches
                 if (!patch.Enabled)
                     continue;

                 // Immediately check the patch side
                 EnumAppSide targetSide = patch.Side == null ? patch.File.Category.SideType : (EnumAppSide)patch.Side;
                 if (targetSide != EnumAppSide.Universal && patch.Side != api.Side)
                     continue;

                 // Check the patch condition
                 if (patch.Condition != null)
                 {
                     IAttribute attr = worldConfig[patch.Condition.When];
                     if (attr == null)
                         continue;

                     if (patch.Condition.useValue)
                     {
                         patch.Value = new JsonObject(JToken.Parse(attr.ToJsonToken()));
                     }
                     else
                     {
                         if (!patch.Condition.IsValue.Equals(attr.GetValue() + "",
                                 StringComparison.InvariantCultureIgnoreCase))
                         {
                             api.Logger.VerboseDebug("Patch file {0}, patch {1}: Unmet IsValue condition ({2}!={3})",
                                 asset.Location, j, patch.Condition.IsValue, attr.GetValue() + "");
                             unmetConditionCount++;
                             continue;
                         }
                     }
                 }

                 // Check patch dependencies
                 if (patch.DependsOn != null)
                 {
                     bool enabled = true;

                     foreach (var dependence in patch.DependsOn)
                     {
                         bool loaded = loadedModIds.Contains(dependence.modid);
                         enabled = enabled && (loaded ^ dependence.invert);
                     }

                     if (!enabled)
                     {
                         unmetConditionCount++;
                         api.Logger.VerboseDebug("Patch file {0}, patch {1}: Unmet DependsOn condition ({2})",
                             asset.Location, j,
                             string.Join(",", patch.DependsOn.Select(pd => (pd.invert ? "!" : "") + pd.modid)));
                         continue;
                     }
                 }

                 // Filter by path if needed
                 if (forPartialPath != null && !patch.File.PathStartsWith(forPartialPath))
                     continue;

                 // Add valid patches to processing list
                 validPatches.Add((patch, asset.Location, j));
             }
         }


         totalCount = validPatches.Count;

         // Process patches in the order they appear in validPatches
         var processedFiles = new HashSet<AssetLocation>();
         var filesToSave = new List<AssetLocation>();

         // Iterate over all valid patches in order from the beginning
         for (int i = 0; i < validPatches.Count; i++)
         {
             var (patch, source, index) = validPatches[i];
             var targetFile = patch.File.Clone();

             // Handle wildcards
             if (targetFile.Path.EndsWith('*'))
             {
                 string basePath = targetFile.Path.TrimEnd('*');
                 List<IAsset> assets = api.Assets.GetMany(basePath, targetFile.Domain, false);

                 // Apply patch to all found files
                 for (int j = 0; j < assets.Count; j++)
                 {
                     ProcessSinglePatch(assets[j].Location, patch, source, index,
                         ref appliedCount, ref notfoundCount, ref errorCount,
                         ref processedFiles, ref filesToSave);
                 }
             }
             else
             {
                 // Apply patch to a single file
                 ProcessSinglePatch(targetFile, patch, source, index,
                     ref appliedCount, ref notfoundCount, ref errorCount,
                     ref processedFiles, ref filesToSave);
             }
         }

         // Save all modified files
         foreach (var fileLoc in filesToSave)
         {
             // Get modified JSON document from cache
             if (_jsonCache.TryGetValue(fileLoc, out var cachedData))
             {
                 try
                 {
                     // Serialize JSON back to string
                     var sb = StringBuilderCache.Acquire();
                     using (var writer = new StringWriter(sb))
                     using (var jsonWriter = new JsonTextWriter(writer))
                     {
                         cachedData.token.WriteTo(jsonWriter);
                     }

                     // Save back to bytes
                     cachedData.asset.Data = Encoding.UTF8.GetBytes(StringBuilderCache.GetStringAndRelease(sb));
                     cachedData.asset.IsPatched = true;
                 }
                 catch (Exception e)
                 {
                     api.World.Logger.Error("Failed to serialize JSON file {0}:", fileLoc);
                     api.World.Logger.Error(e);
                     errorCount++;
                 }
             }
         }

         // Clear caches after processing
         _jsonCache?.Clear();
         filesToSave?.Clear();
         processedFiles?.Clear();
         validPatches?.Clear();
         entries?.Clear();

         // Log results
         StringBuilder sbFinal = new StringBuilder();
         sbFinal.Append("JsonPatch Loader: ");

         if (totalCount == 0)
         {
             sbFinal.Append("Nothing to patch");
         }
         else
         {
             sbFinal.Append(string.Format("{0} patches total", totalCount));

             if (appliedCount > 0)
             {
                 sbFinal.Append(string.Format(", successfully applied {0} patches", appliedCount));
             }

             if (notfoundCount > 0)
             {
                 sbFinal.Append(string.Format(", missing files on {0} patches", notfoundCount));
             }

             if (unmetConditionCount > 0)
             {
                 sbFinal.Append(string.Format(", unmet conditions on {0} patches", unmetConditionCount));
             }

             if (errorCount > 0)
             {
                 sbFinal.Append(string.Format(", had errors on {0} patches", errorCount));
             }
             else
             {
                 sbFinal.Append(", no errors");
             }
         }

         api.Logger.Notification(sbFinal.ToString());
         api.Logger.VerboseDebug("Patchloader finished");
     }


     /// <summary>
     /// Processes a single patch for a single file
     /// </summary>
     private void ProcessSinglePatch(AssetLocation fileLoc, JsonPatch patch, AssetLocation source, int index,
         ref int applied, ref int notFound, ref int errorCount,
         ref HashSet<AssetLocation> processedFiles, ref List<AssetLocation> filesToSave)
     {
         // Fix file extension if necessary
         if (!fileLoc.Path.EndsWithOrdinal(".json"))
             fileLoc.Path += ".json";

         // Load or get JSON document from cache
         if (!_jsonCache.TryGetValue(fileLoc, out var cachedData))
         {
             var asset = api.Assets.TryGet(fileLoc);
             if (asset == null)
             {
                 notFound++;
                 LogFileNotFound(fileLoc, patch, source, index);
                 return;
             }

             // Parse JSON
             JToken token;
             try
             {
                 var jsonText = asset.ToText();
                 token = JToken.Parse(jsonText);
             }
             catch (Exception e)
             {
                 errorCount++;
                 api.World.Logger.Error("Patch {0} (target: {2}) in {1} failed probably because the syntax of the value is broken:",
                     index, source, fileLoc);
                 api.World.Logger.Error(e);
                 return;
             }

             cachedData = (token, asset);
             _jsonCache[fileLoc] = cachedData;
         }

         // Apply patch (create operation)
         Operation op = CreateOperation(patch, source, index, ref errorCount);
         if (op == null)
             return;

         // Apply the operation
         var patchdoc = new PatchDocument(op);
         try
         {
             // Apply patch
             patchdoc.ApplyTo(cachedData.token);
             applied++;

             // Mark file to be saved
             if (!processedFiles.Contains(fileLoc))
             {
                 processedFiles.Add(fileLoc);
                 filesToSave.Add(fileLoc);
             }
         }
         catch (PathNotFoundException p)
         {
             api.World.Logger.Error("Patch {0} (target: {4}) in {1} failed because supplied path {2} is invalid: {3}",
                 index, source, patch.Path, p.Message, fileLoc);
             errorCount++;
         }
         catch (Exception e)
         {
             api.World.Logger.Error("Patch {0} (target: {2}) in {1} failed:", index, source, fileLoc);
             api.World.Logger.Error(e);
             errorCount++;
         }
     }



     private void LogFileNotFound(AssetLocation loc, JsonPatch patch, AssetLocation source, int index)
     {
         if (patch.File.Category == null)
         {
             api.World.Logger.VerboseDebug("Patch {0} in {1}: File {2} not found. Wrong asset category",
                 index, source, loc);
         }
         else
         {
             EnumAppSide catSide = patch.File.Category.SideType;
             if (catSide != EnumAppSide.Universal && api.Side != catSide)
             {
                 api.World.Logger.VerboseDebug("Patch {0} in {1}: File {2} not found. Hint: This asset is usually only loaded {3} side",
                     index, source, loc, catSide);
             }
             else
             {
                 api.World.Logger.VerboseDebug("Patch {0} in {1}: File {2} not found",
                     index, source, loc);
             }
         }
     }



     private Operation CreateOperation(JsonPatch jsonPatch, AssetLocation source, int index, ref int errorCount)
     {
         switch (jsonPatch.Op)
         {
             case EnumJsonPatchOp.Add:
                 if (jsonPatch.Value == null)
                 {
                     api.World.Logger.Error("Patch {0} in {1} failed: Add operation requires Value", index, source);
                     errorCount++;
                     return null;
                 }

                 return new AddReplaceOperation() { Path = new JsonPointer(jsonPatch.Path), Value = jsonPatch.Value.Token };

             case EnumJsonPatchOp.AddEach:
                 if (jsonPatch.Value == null)
                 {
                     api.World.Logger.Error("Patch {0} in {1} failed: AddEach operation requires Value", index, source);
                     errorCount++;
                     return null;
                 }

                 return new AddEachOperation() { Path = new JsonPointer(jsonPatch.Path), Value = jsonPatch.Value.Token };

             case EnumJsonPatchOp.Remove:
                 return new RemoveOperation() { Path = new JsonPointer(jsonPatch.Path) };

             case EnumJsonPatchOp.Replace:
                 if (jsonPatch.Value == null)
                 {
                     api.World.Logger.Error("Patch {0} in {1} failed: Replace operation requires Value", index, source);
                     errorCount++;
                     return null;
                 }

                 return new ReplaceOperation() { Path = new JsonPointer(jsonPatch.Path), Value = jsonPatch.Value.Token };

             case EnumJsonPatchOp.Copy:
                 return new CopyOperation() { Path = new JsonPointer(jsonPatch.Path), FromPath = new JsonPointer(jsonPatch.FromPath) };

             case EnumJsonPatchOp.Move:
                 return new MoveOperation() { Path = new JsonPointer(jsonPatch.Path), FromPath = new JsonPointer(jsonPatch.FromPath) };

             case EnumJsonPatchOp.AddMerge:
                 if (jsonPatch.Value == null)
                 {
                     api.World.Logger.Error("Patch {0} in {1} failed: AddMerge operation requires Value", index, source);
                     errorCount++;
                     return null;
                 }

                 return new AddMergeOperation() { Path = new JsonPointer(jsonPatch.Path), Value = jsonPatch.Value.Token };

             default:
                 return null;
         }
     }




     ///
     /// For backward compatibility with older calls
     /// 

     public void ApplyPatch(int patchIndex, AssetLocation patchSourcefile, JsonPatch jsonPatch, ref int applied, ref int notFound, ref int errorCount)
     {
         EnumAppSide targetSide = jsonPatch.Side == null ? jsonPatch.File.Category.SideType : (EnumAppSide)jsonPatch.Side;

         if (targetSide != EnumAppSide.Universal && jsonPatch.Side != api.Side) return;

         if (jsonPatch.File == null)
         {
             api.World.Logger.Error("Patch {0} in {1} failed because it is missing the target file property", patchIndex, patchSourcefile);
             return;
         }

         var loc = jsonPatch.File.Clone();

         if (jsonPatch.File.Path.EndsWith('*'))
         {
             List<IAsset> assets = api.Assets.GetMany(jsonPatch.File.Path.TrimEnd('*'), jsonPatch.File.Domain, false);
             foreach (var val in assets)
             {
                 jsonPatch.File = val.Location;
                 ApplyPatch(patchIndex, patchSourcefile, jsonPatch, ref applied, ref notFound, ref errorCount);
             }

             jsonPatch.File = loc;

             return;
         }


         if (!loc.Path.EndsWithOrdinal(".json")) loc.Path += ".json";

         var asset = api.Assets.TryGet(loc);
         if (asset == null)
         {
             if (jsonPatch.File.Category == null)
             {
                 api.World.Logger.VerboseDebug("Patch {0} in {1}: File {2} not found. Wrong asset category", patchIndex, patchSourcefile, loc);
             }
             else
             {
                 EnumAppSide catSide = jsonPatch.File.Category.SideType;
                 if (catSide != EnumAppSide.Universal && api.Side != catSide)
                 {
                     api.World.Logger.VerboseDebug("Patch {0} in {1}: File {2} not found. Hint: This asset is usually only loaded {3} side", patchIndex, patchSourcefile, loc, catSide);
                 }
                 else
                 {
                     api.World.Logger.VerboseDebug("Patch {0} in {1}: File {2} not found", patchIndex, patchSourcefile, loc);
                 }
             }


             notFound++;
             return;
         }

         Operation op = null;
         switch (jsonPatch.Op)
         {
             case EnumJsonPatchOp.Add:
                 if (jsonPatch.Value == null)
                 {
                     api.World.Logger.Error("Patch {0} in {1} failed probably because it is an add operation and the value property is not set or misspelled", patchIndex, patchSourcefile);
                     errorCount++;
                     return;
                 }

                 op = new AddReplaceOperation() { Path = new JsonPointer(jsonPatch.Path), Value = jsonPatch.Value.Token };
                 break;
             case EnumJsonPatchOp.AddEach:
                 if (jsonPatch.Value == null)
                 {
                     api.World.Logger.Error("Patch {0} in {1} failed probably because it is an add each operation and the value property is not set or misspelled", patchIndex, patchSourcefile);
                     errorCount++;
                     return;
                 }

                 op = new AddEachOperation() { Path = new JsonPointer(jsonPatch.Path), Value = jsonPatch.Value.Token };
                 break;
             case EnumJsonPatchOp.Remove:
                 op = new RemoveOperation() { Path = new JsonPointer(jsonPatch.Path) };
                 break;
             case EnumJsonPatchOp.Replace:
                 if (jsonPatch.Value == null)
                 {
                     api.World.Logger.Error("Patch {0} in {1} failed probably because it is a replace operation and the value property is not set or misspelled", patchIndex, patchSourcefile);
                     errorCount++;
                     return;
                 }

                 op = new ReplaceOperation() { Path = new JsonPointer(jsonPatch.Path), Value = jsonPatch.Value.Token };
                 break;
             case EnumJsonPatchOp.Copy:
                 op = new CopyOperation() { Path = new JsonPointer(jsonPatch.Path), FromPath = new JsonPointer(jsonPatch.FromPath) };
                 break;
             case EnumJsonPatchOp.Move:
                 op = new MoveOperation() { Path = new JsonPointer(jsonPatch.Path), FromPath = new JsonPointer(jsonPatch.FromPath) };
                 break;
             case EnumJsonPatchOp.AddMerge:
                 op = new AddMergeOperation() { Path = new JsonPointer(jsonPatch.Path), Value = jsonPatch.Value.Token };
                 break;
         }

         PatchDocument patchdoc = new PatchDocument(op);
         JToken token;
         try
         {
             token = JToken.Parse(asset.ToText());
         }
         catch (Exception e)
         {
             api.World.Logger.Error("Patch {0} (target: {2}) in {1} failed probably because the syntax of the value is broken:", patchIndex, patchSourcefile, loc);
             api.World.Logger.Error(e);
             errorCount++;
             return;
         }

         try
         {
             patchdoc.ApplyTo(token);
         }
         catch (PathNotFoundException p)
         {
             api.World.Logger.Error("Patch {0} (target: {4}) in {1} failed because supplied path {2} is invalid: {3}", patchIndex, patchSourcefile, jsonPatch.Path, p.Message, loc);
             errorCount++;
             return;
         }
         catch (Exception e)
         {
             api.World.Logger.Error("Patch {0} (target: {2}) in {1} failed, following Exception was thrown:", patchIndex, patchSourcefile, loc);
             api.World.Logger.Error(e);
             errorCount++;
             return;
         }

         string text = token.ToString();
         asset.Data = Encoding.UTF8.GetBytes(text);
         asset.IsPatched = true;

         applied++;
     }
 }


 // Simple cache for StringBuilder
 internal static class StringBuilderCache
 {
     [ThreadStatic]
     private static StringBuilder cachedInstance;

     public static StringBuilder Acquire()
     {
         var result = cachedInstance;
         if (result == null)
         {
             return new StringBuilder(4096); // Initial capacity
         }

         cachedInstance = null;
         result.Length = 0;
         return result;
     }

     public static string GetStringAndRelease(StringBuilder sb)
     {
         var result = sb.ToString();
         cachedInstance = sb;
         return result;
     }
 }
}

