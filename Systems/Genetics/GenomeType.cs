using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using System;
using System.ComponentModel;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public struct GeneLinkage
    {
        public int GeneID;
        /// <summary> Chance that this gene will separate from the next gene in the list. Otherwise they pass together. </summary>
        public float CrossoverChance;
    }

    public struct JsonGeneLinkage
    {
        /// <summary> The name of the gene being linked. </summary>
        [JsonProperty(Required = Required.Always)]
        public required string Gene;
        /// <summary> Chance that this gene will separate from the next gene in the list. Otherwise they pass together. </summary>
        public float CrossoverChance;
    }

    [ProtoContract]
    public class GenomeType
    {
        [ProtoMember(1)]
        public NameMapping Autosomal { get; protected set; }
        [ProtoMember(2)]
        public NameMapping XZ { get; protected set; }
        [ProtoMember(3)]
        public NameGroupMapping Anonymous { get; protected set; }
        [ProtoMember(4)]
        public NameGroupMapping Bitwise { get; protected set; }

        /// <summary> Not serialized, so make sure not to try accessing from client. </summary>
        private Dictionary<string, GeneInitializer> initializers = new Dictionary<string, GeneInitializer>();
        /// <summary> Always null on client, sometimes null on server.
        /// As with the rest of GenomeType, this is intended to be genus-level, which makes this particular initializer of only circumstantial usefulness. </summary>
        public GeneInitializer? DefaultInitializer;
        /// <summary> Not serialized, always null on client. For serverside traits common to the whole genus. Entity-specific or clientside interpreters should be declared in the genetics behavior instead. </summary>
        public IGeneInterpreter[]? Interpreters { get; protected set; }
        /// <summary> Chance for autosomal genes to separate from the next one in the list. Otherwise they pass together. Not serialized, always null on client. May also be null on the server, indicating that all genes are passed individually. </summary>
        public GeneLinkage[]? AutosomalLinkages;
        /// <summary> Similar to AutosomalLinkages, but for sex-linked genes. </summary>
        public GeneLinkage[]? SexChromosomeLinkages;

        [ProtoMember(5), DefaultValue(SexDetermination.XY)]
        public SexDetermination SexDetermination { get; protected set; } = SexDetermination.XY;

        [ProtoMember(6)]
        public AssetLocation Code { get; private set; }

        public GenomeType()
        {
            Code = "uninitialized";
            Autosomal = new NameMapping();
            XZ = new NameMapping();
            Anonymous = new NameGroupMapping();
            Bitwise = new NameGroupMapping();
        }

        internal GenomeType(AssetLocation code, JsonObject attributes, ModSystemGenetics modsystem, ICoreAPI api)
        {
            this.Code = code;

            JsonObject genes = attributes["genes"];
            Autosomal = parse(genes["autosomal"]);
            XZ = genes["xz"].Exists ? parse(genes["xz"]) : parse(genes["sexlinked"]);
            Anonymous = new NameGroupMapping(genes["anonymous"].AsNonNullArray<JsonGeneGroup>("anonymous genes for genome type " + code));
            Bitwise = new NameGroupMapping(genes["bitwise"].AsNonNullArray<JsonGeneGroup>("bitwise genes for genome type " + code));

            JsonObject initialization = attributes["initializers"];
            if (initialization.Exists)
            {
                foreach (JProperty jp in ((JObject) initialization.Token).Properties())
                {
                    initializers[jp.Name] = new GeneInitializer(this, jp.Name, new JsonObject(jp.Value));
                }
            }
            string? sexDetString = attributes["sexdetermination"].AsString();
            if (sexDetString != null)
            {
                SexDetermination = SexDeterminationExtensions.Parse(sexDetString);
            }

            JsonObject?[]? jsonInterpreters = attributes["interpreters"].AsArray();
            if (jsonInterpreters != null)
            {
                Interpreters = new IGeneInterpreter[jsonInterpreters.Length];
                for (int i = 0; i < jsonInterpreters.Length; ++i)
                {
                    JsonObject? config = jsonInterpreters[i];
                    if (config == null)
                    {
                        throw new Exception("Json error: Gene interpreter " + i + " was null for genome type " + code);
                    }
                    try
                    {
                        Interpreters[i] = modsystem.GetInterpreter(api, this, config, code.Domain, "genome type " + code);
                    }
                    catch (Exception)
                    {
                        api.Logger.Error("Error while setting up interpreters on genome type " + code);
                        throw;
                    }
                }
            }

            JsonGeneLinkage[]? autosomalLinkages = genes["autosomalLinkages"].AsNonNullArray<JsonGeneLinkage>("autosomal linkages for genome type " + Code);
            AutosomalLinkages = autosomalLinkages?.Select(j => new GeneLinkage() { GeneID = Autosomal.GetGeneIDOrWarn(j.Gene, "genome type " + Code), CrossoverChance = j.CrossoverChance }).ToArray();

            JsonGeneLinkage[]? sexChromosomeLinkages = genes["sexChromosomeLinkages"].AsNonNullArray<JsonGeneLinkage>("sex chromosome linkages for genome type " + Code);
            SexChromosomeLinkages = sexChromosomeLinkages?.Select(j => new GeneLinkage() { GeneID = XZ.GetGeneIDOrWarn(j.Gene, "genome type " + Code), CrossoverChance = j.CrossoverChance }).ToArray();
        }

        private NameMapping parse(JsonObject json)
        {
            if (!json.Exists) return new NameMapping();

            JsonObject[]? genes = json.AsArray();
            if (genes == null) return new NameMapping();

            List<string> geneNames = new(genes.Length);
            List<string[]> alleleNames = new(genes.Length);

            for (int gene = 0; gene < genes.Length; ++gene)
            {
                string? key = genes[gene]["gene"].AsString();

                if (key == null) continue;

                geneNames.Add(key);
                string[]? alleles = genes[gene]["alleles"].AsNonNullArray<string>("allele list of gene " + key + " for genome type " + Code);
                if (alleles == null)
                {
                    ILogger.Instance.Warning("Failed to parse allele names for gene " + key + " from " + json.ToString());
                    alleleNames.Add([]);
                }
                else
                {
                    alleleNames.Add(alleles);
                }
            }
            return new NameMapping(geneNames.ToArray(), alleleNames.ToArray());
        }

        public GeneInitializer GetInitializer(string name)
        {
            return initializers[name];
        }

        public GeneInitializer? GetInitializerOrLogWarning(string name, ILogger logger, string sourceForLogging)
        {
            if (initializers.TryGetValue(name, out GeneInitializer? initializer))
            {
                return initializer;
            }
            logger.Warning("No gene initializer named " + name + " exists for genome type " + Code + " requested by " + sourceForLogging);
            return null;
        }

        public GeneInitializer? ChooseInitializer(string[]? initializerNames, ClimateCondition climate, int y, Random random, ICoreServerAPI sapi)
        {
            List<GeneInitializer> valid = new List<GeneInitializer>();
            // If no list provided, default to all being valid
            if (initializerNames == null)
            {
                foreach (KeyValuePair<string, GeneInitializer> entry in initializers)
                {
                    addIfValid(valid, climate, y, entry.Value, sapi);
                }
            }
            else
            {
                for (int i = 0; i < initializerNames.Length; ++i)
                {
                    addIfValid(valid, climate, y, initializers[initializerNames[i]], sapi);
                }
            }
            if (valid.Count == 0)
            {
                return null;
            }
            return valid[random.Next(valid.Count)];
        }

        private void addIfValid(List<GeneInitializer> valid, ClimateCondition climate, int y, GeneInitializer ini, ICoreServerAPI sapi)
        {
            if (ini.ClimateCondition?.Matches(climate, y, sapi) != false)
            {
                valid.Add(ini);
            }
        }
    }
}
