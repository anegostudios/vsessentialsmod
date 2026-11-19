using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public class TextureOverlayInterpreter : IGeneInterpreter
    {
        public const string Code = "TextureOverlay";
        public string Name => Code;

        // One variant will be randomly chosen for each individual to be the overlay
        // Null only if the json is ill-formatted, but we'd still like not to crash in that situation
        protected BlendedOverlayTexture[]? textureVariants;

        // Genome must satisfy all these conditions for the overlay to be applied
        protected GeneCondition[] conditions = [];

        // If set, a random opacity will be chosen for the layer, in between these values
        protected float minOpacity;
        protected float maxOpacity;

        // If set, these overlays will only be applied to this specified material
        protected string? material = null;

        /// <summary>
        /// Used for saving which randomly chosen variant to use into entity data
        /// </summary>
        protected string id = "";

        protected bool skipTexturesBelow = false;


        public TextureOverlayInterpreter(ICoreAPI api, GenomeType genomeType, JsonObject config, string domain, string sourceForLogging)
        {
            material = config["material"].AsString();

            minOpacity = config["minOpacity"].AsFloat(1);
            maxOpacity = config["maxOpacity"].AsFloat(1);

            skipTexturesBelow = config["skipTexturesBelow"].AsBool();

            id = config["id"].AsString() ?? "";
            if (id == "")
            {
                api.Logger.Warning("Json error: No id provided for TextureOverlay on " + sourceForLogging + ". This affects texture randomization across saving and loading.");
            }

            textureVariants = config["textureVariants"].AsArray<BlendedOverlayTexture>(null, domain)!;
            if (textureVariants == null)
            {
                api.Logger.Warning("Json error: No textureVariants provided for TextureOverlay on " + sourceForLogging);
            }
            else if (!Array.TrueForAll(textureVariants, x => x != null))
            {
                api.Logger.Warning("Json error: A texture overlay of id " + id + " on " + sourceForLogging + " includes an invalid overlay");
                textureVariants = textureVariants.Where(x => x != null).ToArray();
            }

            JsonObject[]? jsonConditions = config["conditions"].AsArray();
            if (jsonConditions != null)
            {
                conditions = new GeneCondition[jsonConditions.Length];
                for (int i = 0; i < jsonConditions.Length; ++i)
                {
                    conditions[i] = new GeneCondition(jsonConditions[i], genomeType, "texture overlay " + id + " gene condition " + i + " for " + sourceForLogging);
                }
            }
        }

        void IGeneInterpreter.Interpret(Genome genome, ITreeAttribute attributes, Random random, object organism)
        {
            ITreeAttribute? geneticsTree = attributes.GetTreeAttribute("genetics");
            ArgumentNullException.ThrowIfNull(geneticsTree);

            string key = id + "OverlayIndex";
            if (textureVariants != null && textureVariants.Length > 1 && !geneticsTree.HasAttribute(key))
            {
                geneticsTree.SetInt(key, random.Next(textureVariants.Length));
            }
            else if (!geneticsTree.HasAttribute(key))
            {
                geneticsTree.SetInt(key, 0);
            }

            string alphaKey = id + "AlphaModifier";
            if (minOpacity < 1.0 && maxOpacity > minOpacity && !geneticsTree.HasAttribute(alphaKey))
            {
                geneticsTree.SetFloat(alphaKey, random.NextSingle());
            }
            else if (!geneticsTree.HasAttribute(alphaKey))
            {
                geneticsTree.SetFloat(alphaKey, 1f);
            }
        }

        void IGeneInterpreter.GatherTextureOverlays(Genome genome, ITreeAttribute attributes, ref List<BlendedOverlayTexture>? overlays, string material, ref bool replaceBaseTexture)
        {
            if (this.material != null && material != this.material) return;

            foreach (var condition in conditions)
            {
                if (!condition.Matches(genome.Autosomal)) return;
            }
            // Still needs support for sex-linked genes

            if (textureVariants == null) return;

            ITreeAttribute? geneticsTree = attributes.GetTreeAttribute("genetics");
            ArgumentNullException.ThrowIfNull(geneticsTree);

            int variant = geneticsTree.GetInt(id + "OverlayIndex");
            overlays ??= new();
            if (skipTexturesBelow)
            {
                replaceBaseTexture = true;
                overlays.Clear();
            }
            BlendedOverlayTexture overlay = textureVariants[variant % textureVariants.Length];
            if (minOpacity < 1.0)
            {
                float alphaModifier = geneticsTree.GetFloat(id + "AlphaModifier");
                overlay = overlay.Clone();
                float opacity = minOpacity + alphaModifier * (maxOpacity - minOpacity);
                overlay.Alpha = Math.Clamp((int)Math.Round(255 * opacity), 0, 255);
            }
            overlays.Add(overlay);
        }
    }
}
