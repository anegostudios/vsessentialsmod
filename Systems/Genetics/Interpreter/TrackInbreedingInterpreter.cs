using System;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public class TrackInbreedingInterpreter : IGeneInterpreter
    {
        public string Name => "TrackInbreeding";

        void IGeneInterpreter.Interpret(Genome genome, ITreeAttribute attributes, Random random, object organism)
        {
            Range range = genome.Type.Bitwise.TryGetRange("coi");
            int numGenes = range.End.Value - range.Start.Value;
            if (numGenes > 0)
            {
                int repeats = genome.BitwiseHomozygotes(range);
                float coi = (2 * repeats - numGenes) / (float)numGenes;
                attributes.GetOrAddTreeAttribute("genetics").SetFloat("coi", coi);
            }
        }
    }
}
