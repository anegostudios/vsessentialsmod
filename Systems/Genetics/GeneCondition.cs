using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public struct GeneCondition
    {
        // <summary> The gene this condition is checking </summary>
        public int GeneID;

        // <summary> At least one allele must match any one of these </summary>
        public byte[]? AlleleIDs;
        // <summary> All other alleles (could be polyploid or haploid) must match this, unless it is -1 </summary>
        public int OtherAlleleID = -1;

        // <summary> If set to true, the result gets swapped true-false at the end </summary>
        public bool Invert = false;

        public GeneCondition() { }

        public GeneCondition(JsonObject json, GenomeType genomeType, string sourceForLogging)
        {
            string? geneName = json["gene"].AsString();
            if (geneName == null)
            {
                ILogger.Instance.Warning("Json error: Gene condition in " + sourceForLogging + " is missing which gene to check");
            }
            else if (!genomeType.Autosomal.TryGetGeneID(geneName, out GeneID))
            {
                ILogger.Instance.Warning("Json error: Gene condition in " + sourceForLogging + " has unrecognized gene " + geneName);
            }

            Invert = json["invert"].AsBool(false);

            string? homozygousAlleleName = json["homozygous"].AsString();
            if (homozygousAlleleName != null)
            {
                if (genomeType.Autosomal.TryGetAlleleID(GeneID, homozygousAlleleName, out byte id))
                {
                    AlleleIDs = [id];
                    OtherAlleleID = id;
                }
                else
                {
                    ILogger.Instance.Warning("Json error: Gene condition in " + sourceForLogging + " has unrecognized allele " + homozygousAlleleName + " for gene " + geneName);
                }
                return;
            }

            string[]? hasAlleleNames = json["hasAllele"].AsNonNullArray<string>(sourceForLogging);
            if (hasAlleleNames == null)
            {
                string? hasAlleleName = json["hasAllele"].AsString();
                if (hasAlleleName == null)
                {
                    throw new Exception("Json error: Gene condition in " + sourceForLogging + " does not specify which allele to look for ('hasAllele' or 'homozygous'), for gene " + geneName);
                }
                hasAlleleNames = [ hasAlleleName ];
            }

            if (hasAlleleNames != null)
            {
                AlleleIDs = new byte[hasAlleleNames.Length];
                for (int i = 0; i < hasAlleleNames.Length; ++i)
                {
                    if (!genomeType.Autosomal.TryGetAlleleID(GeneID, hasAlleleNames[i], out AlleleIDs[i]))
                    {
                        ILogger.Instance.Warning("Json error: Gene condition in " + sourceForLogging + " has unrecognized allele " + hasAlleleNames[i] + " for gene " + geneName);
                    }
                }
            }

            string? otherAlleleName = json["otherAllele"].AsString();
            if (otherAlleleName != null)
            {
                if (genomeType.Autosomal.TryGetAlleleID(GeneID, otherAlleleName, out byte id))
                {
                    OtherAlleleID = id;
                }
                else
                {
                    ILogger.Instance.Warning("Json error: Gene condition in " + sourceForLogging + " has unrecognized allele " + otherAlleleName + " for gene " + geneName);
                }
            }
        }

        public bool Matches(byte[,] genes)
        {
            bool mainFound = false;
            for (int n = 0; n < genes.GetLength(1); ++n)
            {
                byte allele = genes[GeneID, n];
                bool isMain = AlleleIDs != null && AlleleIDs.Contains(allele);

                if (OtherAlleleID != -1 && allele != OtherAlleleID && (mainFound || !isMain))
                {
                    return Invert;
                }

                mainFound = mainFound || isMain;
            }

            if (OtherAlleleID != -1)
            {
                mainFound = mainFound || AlleleIDs.Contains((byte)OtherAlleleID);
            }
            return mainFound ^ Invert;
        }
    }
}
