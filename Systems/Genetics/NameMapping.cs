using ProtoBuf;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Vintagestory.API.Common;

namespace Vintagestory.GameContent
{
    [ProtoContract]
    public struct NameMapping
    {
        private Dictionary<string, int> geneMap;
        [ProtoMember(1)]
        internal string[] geneArray;
        private string[][] alleleArrays;
        private Dictionary<string, byte>[] alleleMaps;

        [ProtoMember(2)]
        private ProtoArrayString[] protoFriendlyAlleleArrays
        {
            get => alleleArrays.Select(x => new ProtoArrayString() { array = x } ).ToArray();
            set => alleleArrays = value.Select(x => x.array).ToArray();
        }

        public int GeneCount { get => geneArray.Length; }
        public int AlleleCount(int gene)
        {
            return alleleArrays[gene].Length;
        }

        public bool TryGetGeneID(string name, out int geneId)
        {
            return geneMap.TryGetValue(name, out geneId);
        }

        public int GetGeneIDOrWarn(string name, string sourceForLogging)
        {
            if (geneMap.TryGetValue(name, out int geneId)) return geneId;

            ILogger.Instance.Warning("Unrecognized gene name " + name + " from " + sourceForLogging);
            return 0;
        }

        public int GetGeneID(string name)
        {
            return geneMap[name];
        }

        public string GetGeneName(int id)
        {
            return geneArray[id];
        }

        public string GetAlleleName(int geneID, int alleleID)
        {
            return alleleArrays[geneID][alleleID];
        }

        /// <summary> Returns the byte ID of the given allele name, if such an allele name is defined for the gene. Must be passed a valid gene ID. </summary>
        public bool TryGetAlleleID(int geneID, string alleleName, out byte alleleID)
        {
            return alleleMaps[geneID].TryGetValue(alleleName, out alleleID);
        }

        public byte GetAlleleID(int geneID, string alleleName)
        {
            return alleleMaps[geneID][alleleName];
        }

        public NameMapping()
        {
            geneArray = new string[0];
            alleleArrays = new string[0][];
            geneMap = null!;
            alleleMaps = null!;
        }

        public NameMapping(string[] geneArray, string[][] alleleArrays)
        {
            this.geneArray = geneArray;
            this.alleleArrays = alleleArrays;
            InitializeMaps();
        }

        [ProtoAfterDeserialization]
        [MemberNotNull(nameof(geneMap))]
        [MemberNotNull(nameof(alleleMaps))]
        private void InitializeMaps()
        {
            geneMap = new Dictionary<string, int>();
            alleleMaps = new Dictionary<string, byte>[geneArray.Length];
            for (int gene = 0; gene < geneArray.Length; ++gene)
            {
                geneMap[GetGeneName(gene)] = gene;
                alleleMaps[gene] = new Dictionary<string, byte>();
                for (byte allele = 0; allele < alleleArrays[gene].Length; ++allele)
                {
                    alleleMaps[gene][alleleArrays[gene][allele]] = allele;
                }
            }
        }
    }

    [ProtoContract]
    public class ProtoArrayString
    {
        [ProtoMember(1)]
        public required string[] array;
    }
}
