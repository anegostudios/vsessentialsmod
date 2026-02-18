using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

#nullable disable

namespace Vintagestory.ServerMods.NoObf
{
    [JsonObject(MemberSerialization.OptIn)]
    public class GlobalConfig
    {
        [JsonProperty]
        public AssetLocation waterBlockCode;
        [JsonProperty]
        public AssetLocation waterRapidsBlockCode;
        [JsonProperty]
        public float waterRapidsChance=0;

        [JsonProperty]
        public AssetLocation saltWaterBlockCode;

        [JsonProperty]
        public AssetLocation lakeIceBlockCode;

        [JsonProperty]
        public AssetLocation lavaBlockCode;

        [JsonProperty]
        public AssetLocation basaltBlockCode;

        [JsonProperty]
        public AssetLocation mantleBlockCode;

        [JsonProperty]
        public AssetLocation defaultRockCode;

        [JsonProperty]
        public float neutralCreatureSpawnMultiplier = 1f;

        [JsonProperty]
        public AssetLocation rivuletWaterBlockCode;

        [JsonProperty]
        public AssetLocation rivuletRapidWaterBlockCode;

        [JsonProperty]
        public AssetLocation hotSpringBacteria87DegCode;
        [JsonProperty]
        public AssetLocation hotSpringBacteriaSmooth74DegCode;
        [JsonProperty]
        public AssetLocation hotSpringBacteriaSmooth65DegCode;
        [JsonProperty]
        public AssetLocation hotSpringBacteriaSmooth55DegCode;
        [JsonProperty]
        public AssetLocation sludgyGravelBlockCode;
        [JsonProperty]
        public AssetLocation boilingWaterBlockCode;

        [JsonProperty]
        public AssetLocation devastatedSoil0Code;
        [JsonProperty]
        public AssetLocation devastatedSoil1Code;
        [JsonProperty]
        public AssetLocation devastatedSoil2Code;
        [JsonProperty]
        public AssetLocation devastatedSoil3Code;
        [JsonProperty]
        public AssetLocation devastatedSoil4Code;
        [JsonProperty]
        public AssetLocation devastatedSoil5Code;
        [JsonProperty]
        public AssetLocation devastatedSoil6Code;
        [JsonProperty]
        public AssetLocation devastatedSoil7Code;
        [JsonProperty]
        public AssetLocation devastatedSoil8Code;
        [JsonProperty]
        public AssetLocation devastatedSoil9Code;
        [JsonProperty]
        public AssetLocation devastatedSoil10Code;
        [JsonProperty]
        public AssetLocation devastationGrowthCode;
        [JsonProperty]
        public AssetLocation soilMediumNoneCode;
        [JsonProperty]
        public AssetLocation soilMediumNormalCode;
        [JsonProperty]
        public AssetLocation tallgrassMediumFreeCode;
        [JsonProperty]
        public AssetLocation tallgrassTallFreeCode;

        [JsonProperty]
        public AssetLocation metaFillerBlockCode;
        [JsonProperty]
        public AssetLocation metaPathwayBlockCode;
        [JsonProperty]
        public AssetLocation metaUndergroundBlockCode;
        [JsonProperty]
        public AssetLocation metaAbovegroundBlockCode;
        [JsonProperty]
        public AssetLocation spottyMossDecorCode;

        public int waterBlockId;
        public int waterRapidsBlockId;
        public int saltWaterBlockId;
        public int lakeIceBlockId;
        public int lavaBlockId;
        public int basaltBlockId;
        public int mantleBlockId;
        public int defaultRockId;
        public int rivuletWaterBlockId;
        public int rivuletRapidWaterBlockId;

        public int hotSpringBacteria87DegId;
        public int hotSpringBacteriaSmooth74DegId;
        public int hotSpringBacteriaSmooth65DegId;
        public int hotSpringBacteriaSmooth55DegId;
        public int sludgyGravelBlockId;
        public int boilingWaterBlockId;
        public int devastatedSoil0Id;
        public int devastatedSoil1Id;
        public int devastatedSoil2Id;
        public int devastatedSoil3Id;
        public int devastatedSoil4Id;
        public int devastatedSoil5Id;
        public int devastatedSoil6Id;
        public int devastatedSoil7Id;
        public int devastatedSoil8Id;
        public int devastatedSoil9Id;
        public int devastatedSoil10Id;
        public int devastationGrowthId;
        public int soilMediumNoneId;
        public int soilMediumNormalId;
        public int tallgrassMediumFreeId;
        public int tallgrassTallFreeId;
        public int metaFillerBlockId;
        public int metaPathwayBlockId;
        public int metaUndergroundBlockId;
        public int metaAbovegroundBlockId;
        public int spottyMossDecorId;

        public static readonly string cacheKey = "GlobalConfig";

        public static bool ReplaceMetaBlocks = true;

        public static GlobalConfig GetInstance(ICoreServerAPI api)
        {
            if(api.ObjectCache.TryGetValue(cacheKey, out var value))
            {
                return value as GlobalConfig;
            }

            var asset = api.Assets.Get("worldgen/global.json");
            var globalConfig = asset.ToObject<GlobalConfig>();

            globalConfig.defaultRockId = api.World.GetBlock(globalConfig.defaultRockCode)?.BlockId ?? 0;
            globalConfig.waterBlockId = api.World.GetBlock(globalConfig.waterBlockCode)?.BlockId ?? 0;
            globalConfig.waterRapidsBlockId = api.World.GetBlock(globalConfig.waterRapidsBlockCode)?.BlockId ?? 0;
            globalConfig.saltWaterBlockId = api.World.GetBlock(globalConfig.saltWaterBlockCode)?.BlockId ?? 0;
            globalConfig.lakeIceBlockId = api.World.GetBlock(globalConfig.lakeIceBlockCode)?.BlockId ?? 0;
            globalConfig.lavaBlockId = api.World.GetBlock(globalConfig.lavaBlockCode)?.BlockId ?? 0;
            globalConfig.basaltBlockId = api.World.GetBlock(globalConfig.basaltBlockCode)?.BlockId ?? 0;
            globalConfig.mantleBlockId = api.World.GetBlock(globalConfig.mantleBlockCode)?.BlockId ?? 0;

            globalConfig.rivuletWaterBlockId = api.World.GetBlock(globalConfig.rivuletWaterBlockCode)?.BlockId ?? 0;
            globalConfig.rivuletRapidWaterBlockId = api.World.GetBlock(globalConfig.rivuletRapidWaterBlockCode)?.BlockId ?? 0;

            globalConfig.hotSpringBacteria87DegId = api.World.GetBlock(globalConfig.hotSpringBacteria87DegCode)?.BlockId ?? 0;
            globalConfig.hotSpringBacteriaSmooth74DegId = api.World.GetBlock(globalConfig.hotSpringBacteriaSmooth74DegCode)?.BlockId ?? 0;
            globalConfig.hotSpringBacteriaSmooth65DegId = api.World.GetBlock(globalConfig.hotSpringBacteriaSmooth65DegCode)?.BlockId ?? 0;
            globalConfig.hotSpringBacteriaSmooth55DegId = api.World.GetBlock(globalConfig.hotSpringBacteriaSmooth55DegCode)?.BlockId ?? 0;
            globalConfig.sludgyGravelBlockId = api.World.GetBlock(globalConfig.sludgyGravelBlockCode)?.BlockId ?? 0;
            globalConfig.boilingWaterBlockId = api.World.GetBlock(globalConfig.boilingWaterBlockCode)?.BlockId ?? 0;
            globalConfig.devastatedSoil0Id = api.World.GetBlock(globalConfig.devastatedSoil0Code)?.BlockId ?? 0;
            globalConfig.devastatedSoil1Id = api.World.GetBlock(globalConfig.devastatedSoil1Code)?.BlockId ?? 0;
            globalConfig.devastatedSoil2Id = api.World.GetBlock(globalConfig.devastatedSoil2Code)?.BlockId ?? 0;
            globalConfig.devastatedSoil3Id = api.World.GetBlock(globalConfig.devastatedSoil3Code)?.BlockId ?? 0;
            globalConfig.devastatedSoil4Id = api.World.GetBlock(globalConfig.devastatedSoil4Code)?.BlockId ?? 0;
            globalConfig.devastatedSoil5Id = api.World.GetBlock(globalConfig.devastatedSoil5Code)?.BlockId ?? 0;
            globalConfig.devastatedSoil6Id = api.World.GetBlock(globalConfig.devastatedSoil6Code)?.BlockId ?? 0;
            globalConfig.devastatedSoil7Id = api.World.GetBlock(globalConfig.devastatedSoil7Code)?.BlockId ?? 0;
            globalConfig.devastatedSoil8Id = api.World.GetBlock(globalConfig.devastatedSoil8Code)?.BlockId ?? 0;
            globalConfig.devastatedSoil9Id = api.World.GetBlock(globalConfig.devastatedSoil9Code)?.BlockId ?? 0;
            globalConfig.devastatedSoil10Id = api.World.GetBlock(globalConfig.devastatedSoil10Code)?.BlockId ?? 0;
            globalConfig.devastationGrowthId = api.World.GetBlock(globalConfig.devastationGrowthCode)?.BlockId ?? 0;
            globalConfig.soilMediumNoneId = api.World.GetBlock(globalConfig.soilMediumNoneCode)?.BlockId ?? 0;
            globalConfig.soilMediumNormalId = api.World.GetBlock(globalConfig.soilMediumNormalCode)?.BlockId ?? 0;
            globalConfig.tallgrassMediumFreeId = api.World.GetBlock(globalConfig.tallgrassMediumFreeCode)?.BlockId ?? 0;
            globalConfig.tallgrassTallFreeId = api.World.GetBlock(globalConfig.tallgrassTallFreeCode)?.BlockId ?? 0;
            globalConfig.metaFillerBlockId = api.World.GetBlock(globalConfig.metaFillerBlockCode)?.BlockId ?? 0;
            globalConfig.metaPathwayBlockId = api.World.GetBlock(globalConfig.metaPathwayBlockCode)?.BlockId ?? 0;
            globalConfig.metaUndergroundBlockId = api.World.GetBlock(globalConfig.metaUndergroundBlockCode)?.BlockId ?? 0;
            globalConfig.metaAbovegroundBlockId = api.World.GetBlock(globalConfig.metaAbovegroundBlockCode)?.BlockId ?? 0;
            globalConfig.spottyMossDecorId = api.World.GetBlock(globalConfig.spottyMossDecorCode)?.BlockId ?? 0;

            api.ObjectCache[cacheKey] = globalConfig;
            return globalConfig;
        }
    }
}
