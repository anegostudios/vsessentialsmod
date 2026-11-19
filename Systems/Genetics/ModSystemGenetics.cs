using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public class ModSystemGenetics : ModSystem
    {
        private GenomeTypeRegistry genomeTypeRegistry = null!;
        private Dictionary<string, IGeneInterpreter> interpreterMap = new Dictionary<string, IGeneInterpreter>();
        private FastSmallDictionary<string, IGeneInterpreterFactory> factoryMap = new(1);

        public delegate IGeneInterpreter IGeneInterpreterFactory(ICoreAPI api, GenomeType genomeType, JsonObject config, string domain, string sourceForLogging);

        public override void Start(ICoreAPI api)
        {
            genomeTypeRegistry = api.RegisterCustomRegistry<GenomeTypeRegistry>("genometypes");

            RegisterInterpreter(new TrackInbreedingInterpreter());
            RegisterInterpreter("InbreedingFertility", (api, type, json, domain, sourceForLogging) => new InbreedingFertilityInterpreter(api, type, json, domain, sourceForLogging));
            RegisterInterpreter("TextureOverlay", (api, type, json, domain, sourceForLogging) => new TextureOverlayInterpreter(api, type, json, domain, sourceForLogging));

            RegisterInterpreter("HorseBaseColor", (api, type, json, domain, sourceForLogging) => new HorseBaseColorInterpreter(api, type, json, domain, sourceForLogging));
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            if (api.Side != EnumAppSide.Server) return;

            Dictionary<AssetLocation, JToken> files = api.Assets.GetMany<JToken>(api.Logger, "config/genetics");
            foreach ((AssetLocation key, JToken json) in files)
            {
                try
                {
                    LoadGenomeType(key, json, api);
                }
                catch (Exception e)
                {
                    api.Logger.Error("Error loading asset " + key + ". " + e.Message + "\n" + e.StackTrace);
                }
            }

            api.Logger.Event(files.Count + " genome types loaded");
        }

        public void LoadGenomeType(AssetLocation key, JToken json, ICoreAPI api)
        {
            key = key.CopyWithPathPrefixAndAppendixOnce("config/genetics/", ".json");
            if (!genomeTypeRegistry.Types.ContainsKey(key))
            {
                genomeTypeRegistry.Types[key] = new GenomeType(key.CopyWithPath(key.PathOmittingPrefixAndSuffix("config/genetics/", ".json")), new JsonObject(json), this, api);
            }
        }

        public GenomeType GetGenomeType(AssetLocation location)
        {
            AssetLocation key = location.CopyWithPathPrefixAndAppendixOnce("config/genetics/", ".json");
            return genomeTypeRegistry.Types[key];
        }

        public void RegisterInterpreter(IGeneInterpreter interpreter)
        {
            interpreterMap[interpreter.Name] = interpreter;
        }

        public void RegisterInterpreter(string code, IGeneInterpreterFactory factory)
        {
            factoryMap[code] = factory;
        }

        public IGeneInterpreter GetInterpreter(ICoreAPI api, GenomeType genomeType, JsonObject config, string domain, string sourceForLogging)
        {
            string code = config["code"].AsString() ?? throw new ArgumentException("Missing 'code' in extraInterpreters");
            if (interpreterMap.TryGetValue(code, out IGeneInterpreter? result))
            {
                return result;
            }
            if (factoryMap.TryGetValue(code, out var factory))
            {
                return factory(api, genomeType, config, domain, sourceForLogging);
            }
            throw new ArgumentException("No registered genome interpreter with code " + code);
        }
    }
}
