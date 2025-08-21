using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

#nullable disable

namespace Vintagestory.ServerMods;

public class GridRecipeLoader : ModSystem
{
    private ICoreServerAPI api;
    private bool classExclusiveRecipes = true;

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
            if (recipe.ResolveIngredients(api.World))
            {
                api.RegisterCraftingRecipe(recipe);
            }

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

        foreach (GridRecipe subRecipe in subRecipes)
        {
            if (!subRecipe.ResolveIngredients(api.World)) continue;
            api.RegisterCraftingRecipe(subRecipe);
        }
    }
}

