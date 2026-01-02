using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

#nullable disable

namespace Vintagestory.ServerMods;

public class GridRecipeLoader : ModSystem
{
    private ICoreServerAPI api;
    private bool classExclusiveRecipes = true;
    private static readonly Regex PlaceholderRegex = new Regex(@"\{([^\{\}]+)\}", RegexOptions.Compiled);

    public override double ExecuteOrder() => 1;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api is ICoreServerAPI serverApi)
        {
            this.api = serverApi;

            classExclusiveRecipes = serverApi.World.Config.GetBool("classExclusiveRecipes", true);

            LoadGridRecipes();
        }
    }

    public void LoadGridRecipes()
    {
        Dictionary<AssetLocation, JToken> files = api.Assets.GetMany<JToken>(api.Server.Logger, "recipes/grid");
        int recipeQuantity = 0;

        foreach ((AssetLocation location, JToken content) in files)
        {
            if (content is JObject)
            {
                LoadRecipe(location, content.ToObject<GridRecipe>(location.Domain));
                recipeQuantity++;
            }
            if (content is JArray)
            {
                foreach (JToken token in (content as JArray))
                {
                    LoadRecipe(location, token.ToObject<GridRecipe>(location.Domain));
                    recipeQuantity++;
                }
            }
        }

        api.World.Logger.Event($"{recipeQuantity} crafting recipes loaded from {files.Count} files");
        api.World.Logger.StoryEvent(Lang.Get("Grand inventions..."));
    }

    public void LoadRecipe(AssetLocation assetLocation, GridRecipe recipe)
    {
        if (!recipe.Enabled) return;
        if (!classExclusiveRecipes) recipe.RequiresTrait = null;

        if (recipe.Name == null) recipe.Name = assetLocation;

        Dictionary<string, string[]> nameToCodeMapping = recipe.GetNameToCodeMapping(api.World);

        if (nameToCodeMapping.Count <= 0)
        {
            string[] placeholders = GetPlaceholders(recipe.Output?.Code);
            if (placeholders.Length > 0)
            {
                AddInvalidRecipe(assetLocation, "output contains unresolved placeholders " + string.Join(", ", placeholders));
                return;
            }

            if (recipe.ResolveIngredients(api.World))
            {
                api.RegisterCraftingRecipe(recipe);
            }
            else
            {
                AddInvalidRecipe(assetLocation, "failed to resolve output " + recipe.Output?.Code);
            }

            return;
        }

        List<string> emptyMappings = new List<string>();
        foreach ((string key, string[] variants) in nameToCodeMapping)
        {
            if (variants == null || variants.Length == 0)
            {
                emptyMappings.Add(key);
            }
        }

        if (emptyMappings.Count > 0)
        {
            AddInvalidRecipe(assetLocation, "wildcard name(s) have no matches: " + string.Join(", ", emptyMappings));
            return;
        }

        List<GridRecipe> subRecipes = new();

        int variantsCombinations = 1;
        foreach ((_, string[] mapping) in nameToCodeMapping)
        {
            variantsCombinations *= mapping.Length;
        }

        bool first = true;
        int variantCodeIndexDivider = 1;
        foreach ((string variantCode, string[] variants) in nameToCodeMapping)
        {
            if (variants.Length == 0) continue;
            
            for (int i = 0; i < variantsCombinations; i++)
            {
                GridRecipe currentRecipe;
                string currentVariant = variants[i / variantCodeIndexDivider % variants.Length];

                if (first)
                {
                    currentRecipe = recipe.Clone();
                    subRecipes.Add(currentRecipe);
                }
                else
                {
                    currentRecipe = subRecipes[i];
                }

                foreach (CraftingRecipeIngredient ingredient in currentRecipe.Ingredients.Values)
                {
                    if (ingredient.IsBasicWildCard)
                    {
                        if (ingredient.Name == variantCode)
                        {
                            ingredient.FillPlaceHolder(variantCode, currentVariant);
                            ingredient.Code.Path = ingredient.Code.Path.Replace("*", currentVariant);
                            ingredient.IsBasicWildCard = false;
                        }
                    }
                    else if (ingredient.IsAdvancedWildCard)
                    {
                        ingredient.FillPlaceHolder(variantCode, currentVariant);
                        ingredient.IsAdvancedWildCard = GridRecipe.IsAdvancedWildcard(ingredient.Code);
                    }

                    if (ingredient.ReturnedStack?.Code != null)
                    {
                        ingredient.ReturnedStack.Code.Path = ingredient.ReturnedStack.Code.Path.Replace("{" + variantCode + "}", currentVariant);
                    }
                }

                currentRecipe.Output.FillPlaceHolder(variantCode, currentVariant);
            }
            variantCodeIndexDivider *= variants.Length;
            first = false;
        }

        if (subRecipes.Count == 0)
        {
            AddInvalidRecipe(assetLocation, "wildcards did not match any blocks or items");
            return;
        }

        bool outputChecked = false;
        foreach (GridRecipe subRecipe in subRecipes)
        {
            if (!outputChecked)
            {
                string[] placeholders = GetPlaceholders(subRecipe.Output?.Code);
                if (placeholders.Length > 0)
                {
                    AddInvalidRecipe(assetLocation, "output contains unresolved placeholders " + string.Join(", ", placeholders));
                    return;
                }
                outputChecked = true;
            }

            if (!subRecipe.ResolveIngredients(api.World))
            {
                AddInvalidRecipe(assetLocation, "failed to resolve output " + subRecipe.Output?.Code);
                continue;
            }
            api.RegisterCraftingRecipe(subRecipe);
        }
    }

    private void AddInvalidRecipe(AssetLocation path, string reason)
    {
        RecipeValidationErrors.Add(string.Format("grid recipe {0}: {1}", path.ToShortString(), reason));
    }

    private static string[] GetPlaceholders(AssetLocation code)
    {
        if (code?.Path == null) return Array.Empty<string>();

        MatchCollection matches = PlaceholderRegex.Matches(code.Path);
        if (matches.Count == 0) return Array.Empty<string>();

        string[] placeholders = new string[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            placeholders[i] = matches[i].Value;
        }

        return placeholders;
    }

    
}

