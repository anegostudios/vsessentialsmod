using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent
{
    public class GeneInitializer
    {
        public readonly string Name;
        public GenomeType ForType;
        /// <summary>
        /// Describes how likely each genotype is to be chosen when an animal is spawned or otherwise generated without parents.
        /// The format is an array of float arrays. One float for each allele of a single gene, one float array for each gene.
        /// The values are the cumulative probability for each allele to be chosen.
        /// </summary>
        public float[]?[]? AutosomalFrequencies;
        /// <summary>
        /// Similar to autosomal frequencies, but the format is not the same.
        /// This time, the outer array is of gene groups, and the inner array is for each individual gene within that group.
        /// There are no per-allele frequencies, because bitwise genes are constrained to only two alleles. So the values
        /// are per-gene chances to generate with an allele of 1 rather than 0.
        /// </summary>
        public float[]?[]? BitwiseFrequencies;
        /// <summary>
        /// Same as autosomal frequencies, but for genes on the X chromosome (mammals) or Z chromosome (birds).
        /// </summary>
        public float[]?[]? SexlinkedFrequencies;
        /// <summary>
        /// Only animals that spawn in this climate can use this initializer.
        /// </summary>
        public ClimateSpawnCondition? ClimateCondition;

        public GeneInitializer(GenomeType type, string name, JsonObject attributes)
        {
            this.ForType = type;
            Name = name;
            ClimateCondition = attributes["conditions"].AsObject<ClimateSpawnCondition>();

            AutosomalFrequencies = parseFrequencies(attributes["autosomal"], type.Autosomal);

            JsonObject jsonXZ = attributes["xz"];
            SexlinkedFrequencies = parseFrequencies(jsonXZ.Exists ? jsonXZ : attributes["sexlinked"], type.XZ);

            JsonObject jsonBitwise = attributes["bitwise"];
            if (jsonBitwise.Exists)
            {
                BitwiseFrequencies = new float[type.Bitwise.GeneGroupCount][];
                foreach (JProperty jp in ((JObject) jsonBitwise.Token).Properties())
                {
                    string geneGroup = jp.Name;
                    int groupSize = type.Bitwise.GroupSize(geneGroup);
                    int ordinal = type.Bitwise.GroupOrdinal(geneGroup);
                    if (jp.Value.Type == JTokenType.Array)
                    {
                        float[]? values = jp.Value.ToObject<float[]>();
                        if (values == null)
                        {
                            throw new Exception("Improper format of values for initializing bitwise gene group " + geneGroup + " in genome type " + type.Code);
                        }
                        if (values.Length > groupSize || values.Length < 1)
                        {
                            throw new Exception("Incorrect number of values for initializing bitwise gene group " + geneGroup + " in genome type " + type.Code);
                        }
                        BitwiseFrequencies[ordinal] = values;
                    }
                    else
                    {
                        float chance = new JsonObject(jp.Value).AsFloat();
                        BitwiseFrequencies[ordinal] = new float[] { chance };
                    }
                }
            }
        }

        public static bool AllelePossible(float[]?[]? frequencies, int gene, int allele)
        {
            float[]? f = frequencies?[gene];
            if (f == null) return allele == 0;

            if (f.Length <= allele) return false;

            if (allele == 0) return f[0] > 0;

            return f[allele] > f[allele - 1];
        }

        private float[]?[]? parseFrequencies(JsonObject json, NameMapping mappings)
        {
            if (!json.Exists)
            {
                return null;
            }

            float[]?[] frequencies = new float[mappings.GeneCount][];
            foreach (JProperty jp in ((JObject) json.Token).Properties())
            {
                string geneName = jp.Name;
                int geneID = mappings.GetGeneID(geneName);
                string? defaultAlleleName = null;
                JObject jsonFrequencies = (JObject) jp.Value;
                List<float> list = new List<float>();
                if (jsonFrequencies.ContainsKey("default"))
                {
                    defaultAlleleName = (jsonFrequencies.GetValue("default") as JValue)?.Value as string;
                }
                int defaultAlleleID = defaultAlleleName == null ? 0 : mappings.GetAlleleID(geneID, defaultAlleleName);
                bool sawDefault = false;
                foreach (JProperty jf in jsonFrequencies.Properties())
                {
                    string alleleName = jf.Name;
                    if (alleleName == "default" && defaultAlleleName != null)
                    {
                        continue;
                    }
                    int alleleID = mappings.GetAlleleID(geneID, alleleName);
                    if (alleleID >= list.Count)
                    {
                        CollectionsMarshal.SetCount(list, alleleID + 1);
                    }
                    list[alleleID] = new JsonObject(jf.Value).AsFloat();
                    sawDefault |= alleleID == defaultAlleleID;
                }
                float sum = 0;
                foreach (float f in list)
                {
                    sum += f;
                }
                if (defaultAlleleID >= list.Count)
                {
                    CollectionsMarshal.SetCount(list, defaultAlleleID + 1);
                }
                if (!sawDefault)
                {
                    list[defaultAlleleID] = Math.Max(0, 1 - sum);
                }
                float s = 1;
                if (sum > 1)
                {
                    s = 1 / sum;
                }

                frequencies[geneID] = new float[list.Count];
                frequencies[geneID]![0] = s * list[0];
                for (int i = 1; i < frequencies[geneID]!.Length; ++i)
                {
                    frequencies[geneID]![i] = frequencies[geneID]![i - 1] + s * list[i];
                }
            }
            return frequencies;
        }
    }
}
