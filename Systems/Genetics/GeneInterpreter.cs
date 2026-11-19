using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    /// <summary>
    /// For turning genes into gameplay effects. With some imagination you can do quite a lot of things.
    /// The most general case is to implement Interpret() to read the genes and write an attribute based on them.
    /// Then other parts of the game can read that attribute and act on it.
    /// For convenience, methods are also provided for specific commonly desired changes such as to texture or shape.
    /// </summary>
    public interface IGeneInterpreter
    {
        string Name
        {
            get;
        }

        /// <summary>
        /// Change the genes so that they match the existing phenotype.
        /// </summary>
        void MatchPhenotype(Genome genome, ITreeAttribute attributes, object organism)
        {
            // Do nothing
        }

        /// <summary>
        /// Called on first spawn after genome is generated, but before it is set. Intended to modify the genome so
        /// that the genes are suitable for a wild-spawned adult, such as by ensuring lethal alleles are not homozygous.
        /// Not called when the genome was created by reproduction.
        /// </summary>
        void FinalizeSpawn(Genome genome, GeneInitializer? initializer, Random random)
        {
            // Do nothing
        }

        /// <summary>
        /// If true, this gene combination causes the animal to die before being born.
        /// Called during pregnancy. If you override this, you should also override FinalizeSpawn(). Mother and offspring
        /// should normally use the same GeneInterpreter, but if they are different, the mother's will be the one
        /// called here.
        /// </summary>
        bool IsEmbryonicLethal(Genome genome)
        {
            return false;
        }

        /// <summary>
        /// Called on first spawn or when an entity is born, after genome is finalized.
        /// Intended to be the main way for genes to affect the entity.
        /// </summary>
        void Interpret(Genome genome, ITreeAttribute attributes, Random random, object organism)
        {
            // Do nothing
        }

        void GatherShapeOverlays(Genome genome, ITreeAttribute attributes, ref List<(AssetLocation, Shape)>? overlays, string shapePathForLogging, ref string[]? willDeleteElements)
        {
            // Do nothing
        }

        void GatherTextureOverlays(Genome genome, ITreeAttribute attributes, ref List<BlendedOverlayTexture>? overlays, string material, ref bool replaceBaseTexture)
        {
            // Do nothing
        }

        ITexPositionSource? GetTextureSource(Genome genome, ITreeAttribute attributes, ref EnumHandling handling)
        {
            return null;
        }

        /// <summary>
        /// Clientside. Adds pre-translated strings to the list passed in, or potentially removes or replaces them.
        /// </summary>
        void ListPhenotype(Genome genome, ITreeAttribute attributes, List<string> descriptions)
        {
            // Do nothing
        }
    }
}
