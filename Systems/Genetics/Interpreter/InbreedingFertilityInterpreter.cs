using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public class InbreedingFertilityInterpreter : IGeneInterpreter
    {
        public string Name => "InbreedingFertility";

        /// <summary> If this many or more of the "deleterious" genes are homozygous and non-zero, the embryo will not be viable. </summary>
        public int LethalityThreshold = 4;

        /// <summary> Chance for each allele to be zero, and therefore not contribute to negative effects from inbreeding. </summary>
        public float InbreedingResistance = 0.6f;

        public InbreedingFertilityInterpreter(ICoreAPI api, GenomeType genomeType, JsonObject config, string domain, string sourceForLogging)
        {
            LethalityThreshold = config["lethalityThreshold"].AsInt(LethalityThreshold);
            InbreedingResistance = config["inbreedingResistance"].AsFloat(InbreedingResistance);

            Range range = genomeType.Anonymous.TryGetRange("deleterious");
            if (range.Start.Value == range.End.Value)
            {
                api.Logger.Warning("InbreedingFertility gene interpreter requires a 'deleterious' anonymous gene group to be defined in " + sourceForLogging);
            }
        }

        void IGeneInterpreter.FinalizeSpawn(Genome genome, GeneInitializer? initializer, Random random)
        {
            Range range = genome.Type.Anonymous.TryGetRange("deleterious");

            for (int gene = range.Start.Value; gene < range.End.Value; ++gene)
            {
                if (random.NextSingle() < InbreedingResistance) {
                    genome.Anonymous[gene, 0] = 0;
                }
                bool homozygous = true;
                for (int n = 1; n < genome.Ploidy; ++n)
                {
                    if (random.NextSingle() < InbreedingResistance) {
                        genome.Anonymous[gene, n] = 0;
                    }
                    homozygous = homozygous && genome.Anonymous[gene, n] == genome.Anonymous[gene, 0];
                }
                if (homozygous) genome.Anonymous[gene, 0] = 0;
            }
        }

        protected int countVitalityHomozygotes(Genome genome)
        {
            Range range = genome.Type.Anonymous.TryGetRange("deleterious");

            int duplicates = 0;
            for (int gene = range.Start.Value; gene < range.End.Value; ++gene)
            {
                if (genome.Anonymous[gene, 0] == 0) continue;

                bool homozygous = true;
                for (int n = 1; n < genome.Ploidy; ++n)
                {
                    homozygous = homozygous && genome.Anonymous[gene, n] == genome.Anonymous[gene, 0];
                }
                if (homozygous) duplicates += 1;
            }
            return duplicates;
        }

        bool IGeneInterpreter.IsEmbryonicLethal(Genome genome)
        {
            return countVitalityHomozygotes(genome) >= LethalityThreshold;
        }
    }
}
