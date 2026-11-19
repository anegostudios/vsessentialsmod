using System;
using System.Linq;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class HorseBaseColorInterpreter : IGeneInterpreter
    {
        public string Name => "HorseBaseColor";

        // Known viable: SB1, W15, W19, W20, W32, W34, W35
        public static string[] LethalKIT = ["donkey-white", "donkey-spotting", "W1", "W2", "W3", "W4", "W5", "W6", "W7", "W8", "W9", "W10", "W11", "W13", "W14", "W16", "W17", "W18", "W21", "W22", "W23", "W24", "W25", "W26", "W27", "W28", "W30", "W31", "W33"];

        protected string? material = null;

        protected BlendedOverlayTexture BlackBase;
        protected BlendedOverlayTexture DarkChocolateBase;
        protected BlendedOverlayTexture LightChocolateBase;
        protected BlendedOverlayTexture BayBase;
        protected BlendedOverlayTexture RedBase;
        protected BlendedOverlayTexture SorrelBase;
        protected BlendedOverlayTexture GoldenBase;
        protected BlendedOverlayTexture ButtermilkBase;
        protected BlendedOverlayTexture[] BaseColorsCreamToWhite;

        protected BlendedOverlayTexture WhiteMane;
        protected BlendedOverlayTexture FlaxenMane;
        protected BlendedOverlayTexture PerlinoMane;

        public HorseBaseColorInterpreter(ICoreAPI api, GenomeType genomeType, JsonObject config, string domain, string sourceForLogging)
        {
            material = config["material"].AsString();

            BlackBase = ReadJsonTexture(api, config, "blackBase", domain, sourceForLogging);
            DarkChocolateBase = ReadJsonTexture(api, config, "darkChocolateBase", domain, sourceForLogging);
            LightChocolateBase = ReadJsonTexture(api, config, "lightChocolateBase", domain, sourceForLogging);
            BayBase = ReadJsonTexture(api, config, "bayBase", domain, sourceForLogging);
            RedBase = ReadJsonTexture(api, config, "redBase", domain, sourceForLogging);
            SorrelBase = ReadJsonTexture(api, config, "sorrelBase", domain, sourceForLogging);
            GoldenBase = ReadJsonTexture(api, config, "goldenBase", domain, sourceForLogging);
            ButtermilkBase = ReadJsonTexture(api, config, "buttermilkBase", domain, sourceForLogging);

            BaseColorsCreamToWhite = ReadJsonTextures(api, config, "baseColorsCreamToWhite", domain, sourceForLogging);

            WhiteMane = ReadJsonTexture(api, config, "whiteMane", domain, sourceForLogging);
            FlaxenMane = ReadJsonTexture(api, config, "flaxenMane", domain, sourceForLogging);
            PerlinoMane = ReadJsonTexture(api, config, "perlinoMane", domain, sourceForLogging);
        }

        public BlendedOverlayTexture ReadJsonTexture(ICoreAPI api, JsonObject baseJson, string path, string domain, string sourceForLogging)
        {
            BlendedOverlayTexture? texture = baseJson[path].AsObject<BlendedOverlayTexture>(null, domain)!;
            if (texture == null)
            {
                throw new Exception("Json error: No " + path + " provided for HorseBaseColor on " + sourceForLogging);
            }
            return texture;
        }

        public BlendedOverlayTexture[] ReadJsonTextures(ICoreAPI api, JsonObject baseJson, string path, string domain, string sourceForLogging)
        {
            BlendedOverlayTexture[]? textureVariants = baseJson[path].AsArray<BlendedOverlayTexture>(null, domain)!;
            if (textureVariants == null)
            {
                api.Logger.Warning("Json error: No " + path + " provided for HorseBaseColor on " + sourceForLogging);
            }
            else if (!Array.TrueForAll(textureVariants, x => x != null))
            {
                api.Logger.Warning("Json error: HorseBaseColor interpreter for " + sourceForLogging + " includes an invalid overlay in " + path);
                textureVariants = textureVariants.Where(x => x != null).ToArray();
            }
            return textureVariants ?? [];
        }

        void IGeneInterpreter.Interpret(Genome genome, ITreeAttribute attributes, Random random, object organism)
        {
            ITreeAttribute? geneticsTree = attributes.GetTreeAttribute("genetics");
            ArgumentNullException.ThrowIfNull(geneticsTree);

            if (!geneticsTree.HasAttribute("baseColorRandom1")) geneticsTree.SetDouble("baseColorRandom1", random.NextDouble());
            if (!geneticsTree.HasAttribute("baseColorRandom2")) geneticsTree.SetDouble("baseColorRandom2", random.NextDouble());
            if (!geneticsTree.HasAttribute("maneColorRandom1")) geneticsTree.SetDouble("maneColorRandom1", random.NextDouble());
            if (!geneticsTree.HasAttribute("maneColorRandom2")) geneticsTree.SetDouble("maneColorRandom2", random.NextDouble());
        }


        void IGeneInterpreter.FinalizeSpawn(Genome genome, GeneInitializer? initializer, Random random)
        {
            genome.SetNotHomozygousFor("frame", ["frame"], initializer, "wildtype");

            genome.SetNotHomozygousFor("KIT", LethalKIT, initializer, "wildtype");
            genome.SetNotHomozygousFor("MITF", ["SW3", "Macchiato", "SW5"], initializer, "wildtype");
            genome.SetNotHomozygousFor("PAX3", ["SW4"], initializer, "wildtype");
        }

        bool IGeneInterpreter.IsEmbryonicLethal(Genome genome)
        {
            return genome.HasOnlyAlleles("KIT", LethalKIT)
                || genome.HasOnlyAlleles("MITF", ["SW3", "Macchiato", "SW5"])
                || genome.HasOnlyAlleles("PAX3", ["SW4"])
                // Not strictly speaking _embryonic_ lethal, but while it's unimplemented we really, really, don't want homozygous horses appearing in save files
                || genome.HasOnlyAlleles("frame", "frame");
        }

        // 0 for the lightest, most golden chestnut, 1 for the deepest red
        protected virtual double GetRedDepth(Genome genome, double random)
        {
            double rufous = genome.BitwiseFraction("rufous");
            rufous = 0.16 + rufous * (1 - 0.16);
            if (genome.IsHomozygous("flaxen", "flaxen"))
            {
                rufous -= 0.08;
            }
            if (genome.HasAllele("mealy", "mealy"))
            {
                rufous -= 0.08;
            }

            return rufous * 0.9 + random * 0.1;
        }

        protected void AddBaseTexture(Genome genome, ITreeAttribute attributes, ref List<BlendedOverlayTexture>? overlays)
        {
            overlays ??= new();

            double random1 = attributes.GetDouble("baseColorRandom1");
            double random2 = attributes.GetDouble("baseColorRandom2");

            bool redBased = !genome.HasAllele("extension", "wildtype");
            bool blackBased = !redBased && genome.IsHomozygous("agouti", "black");

            // Double-dilute, and equivalent phenotypes
            if (genome.HasOnlyAlleles("cream", "cream", "sunshine", "snowdrop", "donkey-ivory"))
            {
                double shade = GetRedDepth(genome, random1);
                if (blackBased)
                {
                    shade = 0.25 + 0.75 * random1;
                }
                // Weighted towards indices near each other. Prevents yellow-white blends that don't shift through cream.
                random2 = 0.65 * random2 + 0.35 * (1 - shade);
                AddBlendedOverlays(BaseColorsCreamToWhite, 1 - shade, random2, ref overlays);
                return;
            }

            // Leave out pearl and champagne for simplicity

            // Buckskin and palomino
            double rufous = GetRedDepth(genome, random1);
            if (!blackBased && genome.HasAllele("cream", "cream"))
            {
                if (genome.HasAllele("cream", "buttermilk"))
                {
                    rufous = 0.55 * rufous - 0.05;
                }
                rufous -= 0.2;

                BlendedOverlayTexture tint = ButtermilkBase;
                double opacity = 0;
                if (rufous > 0.6)
                {
                    tint = SorrelBase;
                    opacity = (rufous - 0.6) / 0.4;
                }
                else if (rufous < 0.5)
                {
                    opacity = 1 - 2 * rufous;
                }

                if (opacity < 1)
                {
                    overlays.Add(GoldenBase);
                }
                if (opacity > 0)
                {
                    tint = tint.Clone();
                    tint.Alpha = (int) (tint.Alpha * Math.Min(opacity, 1));
                    overlays.Add(tint);
                }

                if (redBased && genome.HasAllele("liver", "dark"))
                {
                    BlendedOverlayTexture darktint = DarkChocolateBase.Clone();
                    opacity = 0.25 * random2;
                    if (genome.IsHomozygous("liver", "dark"))
                    {
                        opacity += 0.15;
                    }
                    darktint.Alpha = (int) (darktint.Alpha * opacity);
                    overlays.Add(darktint);
                }

                return;
            }

            if (blackBased)
            {
                // Leave out pearl, champagne, and silver for simplicity

                BlendedOverlayTexture tint = DarkChocolateBase;
                double opacity = random1 - 0.4;
                // Lighten slightly for smoky black
                if (genome.HasAllele("cream", "cream"))
                {
                    opacity += 0.4;
                    if (genome.HasAllele("cream", "buttermilk"))
                    {
                        tint = LightChocolateBase;
                        opacity *= 0.9;
                    }
                }

                if (opacity < 1)
                {
                    overlays.Add(BlackBase);
                }
                if (opacity > 0)
                {
                    tint = tint.Clone();
                    tint.Alpha = (int) (tint.Alpha * opacity);
                    overlays.Add(tint);
                }
                return;
            }

            // Set up red intensity, shared between bay and chestnut
            overlays.Add(SorrelBase);
            // rufous = 0: 50% sorrel + 50% golden
            // rufous = 1: 100% red
            if (rufous < 0.48)
            {
                BlendedOverlayTexture tint = GoldenBase.Clone();
                tint.Alpha = (int) (tint.Alpha * (0.5 - rufous));
                overlays.Add(tint);
            }
            else if (rufous > 0.52)
            {
                BlendedOverlayTexture tint = RedBase.Clone();
                tint.Alpha = (int) (tint.Alpha * (2 * rufous - 1));
                overlays.Add(tint);
            }

            // Bay
            if (genome.HasAllele("extension", "wildtype"))
            {
                double shading = random2;
                int agouticycle = genome.Type.Autosomal.GetGeneID("agouticycle");
                byte brightred = genome.Type.Autosomal.GetAlleleID(agouticycle, "red");
                int agouti = genome.Type.Autosomal.GetGeneID("agouti");
                byte wildtypeagouti = genome.Type.Autosomal.GetAlleleID(agouti, "wildtype");
                if (genome.IsHomozygous(agouticycle, brightred) && genome.IsHomozygous(agouti, wildtypeagouti))
                {
                    // Bright clear bay
                    shading = 0;
                }
                // Require promoter to be cis-linked
                else if ((genome.Autosomal[agouticycle, 0] == brightred && genome.Autosomal[agouti, 0] == wildtypeagouti) || (genome.Autosomal[agouticycle, 1] == brightred && genome.Autosomal[agouti, 1] == wildtypeagouti))
                {
                    shading *= 0.2;
                }
                else
                {
                    shading = Math.Min(1, shading + 0.12);
                }

                if (shading >= 0.98)
                {
                    overlays.Clear();
                    overlays.Add(BayBase);
                }
                else if (shading > 0)
                {
                    BlendedOverlayTexture tint = BayBase.Clone();
                    tint.Alpha = (int) (tint.Alpha * shading);
                    overlays.Add(tint);
                }
                return;
            }

            // Chestnut

            if (genome.IsHomozygous("liver", "dark"))
            {
                // Looked at many photos of black forest horses. Individual horses seem to shift in shade over time within
                // about half of one of these step sizes. The rest of the variation is likely genetic, but let's treat it as random for now.
                overlays.Clear();
                overlays.Add(LightChocolateBase);
                float maxOpacity = 1;
                BlendedOverlayTexture tint = DarkChocolateBase;
                if (random1 < 0.5f)
                {
                    tint = BlackBase;
                    maxOpacity = 0.5f;
                }

                tint = tint.Clone();
                tint.Alpha = (int) (tint.Alpha * random2 * maxOpacity);
                overlays.Add(tint);
                return;
            }

            if (genome.HasAllele("liver", "dark"))
            {
                BlendedOverlayTexture tint = LightChocolateBase;
                if (random1 < 0.5f)
                {
                    tint = DarkChocolateBase;
                }

                tint = tint.Clone();
                tint.Alpha = (int) (tint.Alpha * random2);
                overlays.Add(tint);
                return;
            }

            if (genome.IsHomozygous("flaxen", "flaxen"))
            {
                BlendedOverlayTexture tint = GoldenBase.Clone();

                // Just a hint of yellow here, not much
                tint.Alpha = (int)(tint.Alpha * random2 * random2 * 0.25);
                overlays.Add(tint);
            }
        }

        public static void AddBlendedOverlays(BlendedOverlayTexture[] textures, double fraction1, double fraction2, ref List<BlendedOverlayTexture> overlays)
        {
            BlendedOverlayTexture main = textures.AtFraction(fraction1);
            BlendedOverlayTexture tint = textures.AtFraction(fraction2);
            overlays.Add(main);

            if (main != tint)
            {
                tint = tint.Clone();
                double alpha = fraction2 * textures.Length % 1.0; 
                tint.Alpha = (int)(alpha * 128);
                overlays.Add(tint);
            }
        }

        protected void AddManeTexture(Genome genome, ITreeAttribute attributes, ref List<BlendedOverlayTexture>? overlays)
        {
            overlays ??= new();
            double random1 = attributes.GetDouble("maneColorRandom1");
            double random2 = attributes.GetDouble("maneColorRandom2");

            bool redBased = !genome.HasAllele("extension", "wildtype");

            // Double-dilutes
            if (genome.HasOnlyAlleles("cream", "cream", "sunshine", "snowdrop", "donkey-ivory"))
            {
                if (random1 < 0.25 || random1 > 0.75 && random2 < 0.98)
                {
                    // Alternate yellowish texture (different from the mane that comes with the cremello body textures)
                    overlays.Add(FlaxenMane);
                }

                BlendedOverlayTexture tint = WhiteMane;
                if (!redBased && random1 < 0.5f)
                {
                    tint = PerlinoMane;
                }

                if (random2 > 0.02)
                {
                    tint = tint.Clone();
                    tint.Alpha = (int) (tint.Alpha * random2);
                    overlays.Add(tint);
                }

                return;
            }

            // Leave out pearl, silver, and black-based champagne for simplicity

            if (!redBased) return; // Handled by a json texture overlay of black manes

            // Other colors can blend with a mane the same color as the body, but flaxen can come with too dark of a red for blending directly with white
            if (genome.IsHomozygous("flaxen", "flaxen"))
            {
                overlays.Add(FlaxenMane);
            }

            if (genome.HasAllele("cream", "cream") || genome.HasAllele("champagne", "champagne") || genome.IsHomozygous("flaxen", "flaxen"))
            {
                BlendedOverlayTexture tint = WhiteMane.Clone();
                double opacity = 0.5 + 0.5 * random2;
                tint.Alpha = (int) (tint.Alpha * opacity);
                overlays.Add(tint);
            }

            // For bright red chestnut, use the mane included with the base color, unless flaxen
        }

        void IGeneInterpreter.GatherTextureOverlays(Genome genome, ITreeAttribute attributes, ref List<BlendedOverlayTexture>? overlays, string material, ref bool replaceBaseTexture)
        {
            if (this.material != null && material != this.material) return;

            ITreeAttribute? geneticsTree = attributes.GetTreeAttribute("genetics");
            ArgumentNullException.ThrowIfNull(geneticsTree);

            replaceBaseTexture = true;
            AddBaseTexture(genome, geneticsTree, ref overlays);
            AddManeTexture(genome, geneticsTree, ref overlays);
        }
    }
}
