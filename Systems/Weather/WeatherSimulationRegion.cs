    using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    // Epiphany!
    // Stratus clouds can be made with using fog that is only added with a vertical up gradient. No fog near sealevel!!!
    // "Stratus clouds are low-level clouds characterized by horizontal layering with a uniform base, as opposed to convective or cumuliform clouds that are formed by rising thermals. More specifically, the term stratus is used to describe flat, hazy, featureless clouds of low altitude varying in color from dark gray to nearly white."

    // https://en.wikipedia.org/wiki/Cloud#/media/File:Cloud_types.jpg
    // Cumulus: tall chunky bits, low altitude
    // Cirrocumulus: like white noise, high altitude
    // Cirrus: white stripes, high altitude
    // Altostratus: flat chunky bits, medium altitude
    // Altocumulus: small chunky bits, medium altitude
    // Nimbostratus: Very tall chunk bits, low till medium altitude
    // Cumulonimbus: super tall chunky bits, low till high altitude
    // Stratus: Very Flat chunky bits, low altitude
    // Stratocumulus: Flat chunky bits, below mid altitude


    // Epiphany #2
    // 1. We run the WeatherSimulation System once for each online player, taking the climate conditions of the player at that location into account => fucking localized weather system
    // 2. Weather pattern chance value is changed from a float to a object:
    // Chance always 0.5:
    // chance: { baseValue: 0.5, rain: { avg: 1, var 1 }, temp: { avg: 1, var 1 } }   
    // Chance is 0.5 when rain is at 0.5 and temp is at 10 degrees. But may still happen at reduced chance for rain between 0.25 till 0.75 and temp between 9 and 11
    // chance: { baseValue: 0.5, rain: { avg: 0.5, var: 0.25 }, temp: { avg: 10, var: 1 } }

    // One unsolved problem though: How can blocks know if they are getting rained on?

    // Epiphany #3
    // We run the weather simulation once for each loaded map region and on the client lerp between the closest 4 for each player

    /// <summary>
    /// Location based weather simulation for one map region
    /// </summary>
    public class WeatherSimulationRegion
    {
        // Persistent data
        public bool Transitioning;
        public float TransitionDelay;
        public WeatherPattern NewWePattern;
        public WeatherPattern OldWePattern;

        /// <summary>
        /// Holds a list of daily snow accum snapshot of previous 144 days (= 1 year)
        /// </summary>
        public RingArray<SnowAccumSnapshot> SnowAccumSnapshots;
        public static object snowAccumSnapshotLock = new object();

        public WindPattern CurWindPattern;
        public WeatherEvent CurWeatherEvent;

        public float Weight;
        public double LastUpdateTotalHours;
        public LCGRandom Rand;
        public int regionX;
        public int regionZ;

        public int cloudTilebasePosX;
        public int cloudTilebasePosZ;



        // Runtime data
        public WeatherDataSnapshot weatherData = new WeatherDataSnapshot();
        public bool IsInitialized;
        public bool IsDummy;

        public WeatherPattern[] WeatherPatterns;
        public WindPattern[] WindPatterns;
        public WeatherEvent[] WeatherEvents;

        protected WeatherSystemBase ws;
        protected WeatherSystemServer wsServer;
        protected ICoreClientAPI capi;
        

        protected float quarterSecAccum = 0;
        protected BlockPos regionCenterPos;
        protected Vec3d tmpVecPos = new Vec3d();



        public IMapRegion MapRegion;

        public static int snowAccumResolution = 2;




        public WeatherSimulationRegion(WeatherSystemBase ws, int regionX, int regionZ)
        {
            this.ws = ws;
            this.regionX = regionX;
            this.regionZ = regionZ;
            this.SnowAccumSnapshots = new RingArray<SnowAccumSnapshot>((int)(ws.api.World.Calendar.DaysPerYear * ws.api.World.Calendar.HoursPerDay) + 1);


            int regsize = ws.api.World.BlockAccessor.RegionSize;

            LastUpdateTotalHours = ws.api.World.Calendar.TotalHours;

            cloudTilebasePosX = (regionX * regsize) / ws.CloudTileSize;
            cloudTilebasePosZ = (regionZ * regsize) / ws.CloudTileSize;

            regionCenterPos = new BlockPos(regionX * regsize + regsize/2, 0, regionZ * regsize + regsize/2);


            Rand = new LCGRandom(ws.api.World.Seed);
            Rand.InitPositionSeed(regionX/3, regionZ/3);
            weatherData.Ambient = new AmbientModifier().EnsurePopulated();

            if (ws.api.Side == EnumAppSide.Client)
            {
                capi = ws.api as ICoreClientAPI;

                weatherData.Ambient.FogColor = capi.Ambient.Base.FogColor.Clone();
            } else
            {
                wsServer = ws as WeatherSystemServer;
            }

            ReloadPatterns(ws.api.World.Seed);
        }


        internal void ReloadPatterns(int seed)
        {
            WeatherPatterns = new WeatherPattern[ws.WeatherConfigs.Length];
            for (int i = 0; i < ws.WeatherConfigs.Length; i++)
            {
                WeatherPatterns[i] = new WeatherPattern(ws, ws.WeatherConfigs[i], Rand, cloudTilebasePosX, cloudTilebasePosZ);
                WeatherPatterns[i].State.Index = i;
            }

            WindPatterns = new WindPattern[ws.WindConfigs.Length];
            for (int i = 0; i < ws.WindConfigs.Length; i++) {
                WindPatterns[i] = new WindPattern(ws.api, ws.WindConfigs[i], i, Rand, seed);
            }

            WeatherEvents = new WeatherEvent[ws.WeatherEventConfigs.Length];
            for (int i = 0; i < ws.WeatherEventConfigs.Length; i++)
            {
                WeatherEvents[i] = new WeatherEvent(ws.api, ws.WeatherEventConfigs[i], i, Rand, seed - 876);
            }
        }

        internal void LoadRandomPattern()
        {
            NewWePattern = RandomWeatherPattern();
            OldWePattern = RandomWeatherPattern();

            NewWePattern.OnBeginUse();
            OldWePattern.OnBeginUse();

            CurWindPattern = WindPatterns[Rand.NextInt(WindPatterns.Length)];
            CurWindPattern.OnBeginUse();

            CurWeatherEvent = RandomWeatherEvent();
            CurWeatherEvent.OnBeginUse();

            Weight = 1;

            wsServer?.SendWeatherStateUpdate(new WeatherState()
            {
                RegionX = regionX,
                RegionZ = regionZ,
                NewPattern = NewWePattern.State,
                OldPattern = OldWePattern.State,
                WindPattern = CurWindPattern.State,
                WeatherEvent = CurWeatherEvent?.State,
                TransitionDelay = 0,
                Transitioning = false,
                Weight = Weight,
                updateInstant = false,
                LcgCurrentSeed = Rand.currentSeed,
                LcgMapGenSeed = Rand.mapGenSeed,
                LcgWorldSeed = Rand.worldSeed
            });
        }

        internal void Initialize()
        {
            for (int i = 0; i < WeatherPatterns.Length; i++)
            {
                WeatherPatterns[i].Initialize(i, ws.api.World.Seed);
            }

            NewWePattern = WeatherPatterns[0];
            OldWePattern = WeatherPatterns[0];
            CurWindPattern = WindPatterns[0];
            CurWeatherEvent = WeatherEvents[0];

            IsInitialized = true;
        }

        
        public void UpdateWeatherData()
        {
            weatherData.SetAmbientLerped(OldWePattern, NewWePattern, Weight, capi == null ? 0 : capi.Ambient.Base.FogDensity.Value);
        }

        public void TickEveryInGameHourServer(double nowTotalHours)
        {
            SnowAccumSnapshot latestSnap = new SnowAccumSnapshot() {
                TotalHours = nowTotalHours,
                SnowAccumulationByRegionCorner = new FloatDataMap3D(snowAccumResolution, snowAccumResolution, snowAccumResolution)
            };

            // Idea: We don't want to simulate 512x512 blocks at all times, thats a lot of iterations
            // lets try with just the 8 corner points of the region cuboid and lerp
            BlockPos tmpPos = new BlockPos();
            int regsize = ws.api.World.BlockAccessor.RegionSize;

            double nowTotalDays = (nowTotalHours + 0.5) / ws.api.World.Calendar.HoursPerDay;

            for (int ix = 0; ix < snowAccumResolution; ix++)
            {
                for (int iy = 0; iy < snowAccumResolution; iy++)
                {
                    for (int iz = 0; iz < snowAccumResolution; iz++)
                    {
                        int y = iy == 0 ? ws.api.World.SeaLevel : ws.api.World.BlockAccessor.MapSizeY - 1;

                        tmpPos.Set(
                            regionX * regsize + ix * (regsize - 1),
                            y,
                            regionZ * regsize + iz * (regsize - 1)
                        );

                        ClimateCondition nowcond = ws.api.World.BlockAccessor.GetClimateAt(tmpPos, EnumGetClimateMode.ForSuppliedDateValues, nowTotalDays);
                        if (nowcond == null)
                        {
                            return;
                        }

                        if (nowcond.Temperature > 1.5f || (nowcond.Rainfall < 0.05 && nowcond.Temperature > 0))
                        {
                            latestSnap.SnowAccumulationByRegionCorner.AddValue(ix, iy, iz, -nowcond.Temperature / 15f);
                        }
                        else
                        {
                            latestSnap.SnowAccumulationByRegionCorner.AddValue(ix, iy, iz, nowcond.Rainfall / 3f);
                        }

                    }
                }
            }

            lock (snowAccumSnapshotLock)
            {
                SnowAccumSnapshots.Add(latestSnap);
            }
            latestSnap.Checks++;
        }

        



        public void TickEvery25ms(float dt)
        {
            if (ws.api.Side == EnumAppSide.Client)
            {
                clientUpdate(dt);
            } else
            {
                double nowTotalHours = ws.api.World.Calendar.TotalHours;
                int i = 0;
                while (nowTotalHours - LastUpdateTotalHours > 1 && i++ < 1000)
                {
                    TickEveryInGameHourServer(LastUpdateTotalHours);
                    LastUpdateTotalHours++;
                }

                var rnd = ws.api.World.Rand;
                float targetLightninMinTemp = CurWeatherEvent.State.LightningMinTemp;
                if (rnd.NextDouble() < CurWeatherEvent.State.LightningRate)
                {
                    ClimateCondition nowcond = ws.api.World.BlockAccessor.GetClimateAt(regionCenterPos, EnumGetClimateMode.ForSuppliedDateValues, ws.api.World.Calendar.TotalDays);
                    if (nowcond.Temperature >= targetLightninMinTemp && nowcond.RainCloudOverlay > 0.15)
                    {
                        Vec3d pos = regionCenterPos.ToVec3d().Add(-200 + rnd.NextDouble() * 400, ws.api.World.SeaLevel, -200 + rnd.NextDouble() * 400);
                        ws.SpawnLightningFlash(pos);
                    }
                }
            }

            if (Transitioning)
            {
                float speed = ws.api.World.Calendar.SpeedOfTime / 60f;
                Weight += dt / TransitionDelay * speed;

                if (Weight > 1)
                {
                    Transitioning = false;
                    Weight = 1;
                }
            }
            else
            {
                if (ws.autoChangePatterns && ws.api.Side == EnumAppSide.Server && ws.api.World.Calendar.TotalHours > NewWePattern.State.ActiveUntilTotalHours)
                {
                    TriggerTransition();
                }
            }

            if (ws.autoChangePatterns && ws.api.Side == EnumAppSide.Server)
            {
                bool sendPacket = false;

                if (ws.api.World.Calendar.TotalHours > CurWindPattern.State.ActiveUntilTotalHours)
                {
                    CurWindPattern = WindPatterns[Rand.NextInt(WindPatterns.Length)];
                    CurWindPattern.OnBeginUse();
                    sendPacket = true;
                }

                if (ws.api.World.Calendar.TotalHours > CurWeatherEvent.State.ActiveUntilTotalHours || CurWeatherEvent.ShouldStop(weatherData.climateCond.Rainfall, weatherData.climateCond.Temperature))
                {
                    CurWeatherEvent = RandomWeatherEvent();
                    CurWeatherEvent.OnBeginUse();
                    sendPacket = true;
                }

                if (sendPacket)
                {
                    wsServer.SendWeatherStateUpdate(new WeatherState()
                    {
                        RegionX = regionX,
                        RegionZ = regionZ,
                        NewPattern = NewWePattern.State,
                        OldPattern = OldWePattern.State,
                        WindPattern = CurWindPattern.State,
                        WeatherEvent = CurWeatherEvent?.State,
                        TransitionDelay = TransitionDelay,
                        Transitioning = Transitioning,
                        Weight = Weight,
                        LcgCurrentSeed = Rand.currentSeed,
                        LcgMapGenSeed = Rand.mapGenSeed,
                        LcgWorldSeed = Rand.worldSeed
                    });
                }
            }


            NewWePattern.Update(dt);
            OldWePattern.Update(dt);
            
            CurWindPattern.Update(dt);
            CurWeatherEvent.Update(dt);

            float curWindSpeed = weatherData.curWindSpeed.X;
            float targetWindSpeed = (float)GetWindSpeed(ws.api.World.SeaLevel);

            curWindSpeed += GameMath.Clamp((targetWindSpeed - curWindSpeed) * dt, -0.001f, 0.001f);
            weatherData.curWindSpeed.X = curWindSpeed;

            quarterSecAccum += dt;
            if (quarterSecAccum > 0.25f)
            {
                regionCenterPos.Y = ws.api.World.BlockAccessor.GetRainMapHeightAt(regionCenterPos);
                if (regionCenterPos.Y == 0) regionCenterPos.Y = ws.api.World.SeaLevel; // Map chunk might not be loaded. In that case y will be 0.
                ClimateCondition nowcond = ws.api.World.BlockAccessor.GetClimateAt(regionCenterPos);
                if (nowcond != null)
                {
                    weatherData.climateCond = nowcond;
                }

                quarterSecAccum = 0;
            }

            weatherData.BlendedPrecType = CurWeatherEvent.State.PrecType;
        }



        private void clientUpdate(float dt)
        {
            EntityPlayer eplr = (ws.api as ICoreClientAPI).World.Player.Entity;
            
            regionCenterPos.Y = (int)eplr.Pos.Y;

            float targetNearLightningRate = CurWeatherEvent.State.NearThunderRate;
            float targetDistantLightningRate = CurWeatherEvent.State.DistantThunderRate;
            float targetLightninMinTemp = CurWeatherEvent.State.LightningMinTemp;

            weatherData.nearLightningRate += GameMath.Clamp((targetNearLightningRate - weatherData.nearLightningRate) * dt, -0.001f, 0.001f);
            weatherData.distantLightningRate += GameMath.Clamp((targetDistantLightningRate - weatherData.distantLightningRate) * dt, -0.001f, 0.001f);
            weatherData.lightningMinTemp += GameMath.Clamp((targetLightninMinTemp - weatherData.lightningMinTemp) * dt, -0.001f, 0.001f);
            weatherData.BlendedPrecType = CurWeatherEvent.State.PrecType;
        }

        
        public double GetWindSpeed(double posY)
        {
            if (CurWindPattern == null) return 0;

            double strength = CurWindPattern.Strength;

            if (posY > ws.api.World.SeaLevel)
            {
                // Greater wind at greater heights
                strength *= Math.Max(1, 0.9 + (posY - ws.api.World.SeaLevel) / 100.0);
                strength = Math.Min(strength, 1.5f);
            }
            else
            {
                // Much muuuch lower winds at lower heights
                strength /= 1 + (ws.api.World.SeaLevel - posY) / 4;
            }

            return strength;
        }

        public EnumPrecipitationType GetPrecipitationType()
        {
            return weatherData.BlendedPrecType;
        }

        public bool SetWindPattern(string code, bool updateInstant)
        {
            WindPattern pattern = WindPatterns.FirstOrDefault(p => p.config.Code == code);
            if (pattern == null) return false;

            CurWindPattern = pattern;
            CurWindPattern.OnBeginUse();

            sendState(updateInstant);
            return true;
        }


        public bool SetWeatherEvent(string code, bool updateInstant)
        {
            WeatherEvent pattern = WeatherEvents.FirstOrDefault(p => p.config.Code == code);
            if (pattern == null) return false;

            CurWeatherEvent = pattern;
            CurWeatherEvent.OnBeginUse();

            sendState(updateInstant);
            return true;
        }

        public bool SetWeatherPattern(string code, bool updateInstant)
        {
            WeatherPattern pattern = WeatherPatterns.FirstOrDefault(p => p.config.Code == code);
            if (pattern == null) return false;

            OldWePattern = NewWePattern;
            NewWePattern = pattern;
            Weight = 1;
            Transitioning = false;
            TransitionDelay = 0;
            if (NewWePattern != OldWePattern || updateInstant) NewWePattern.OnBeginUse();

            UpdateWeatherData();

            sendState(updateInstant);

            return true;
        }



        void sendState(bool updateInstant)
        {
            wsServer.SendWeatherStateUpdate(new WeatherState()
            {
                RegionX = regionX,
                RegionZ = regionZ,
                NewPattern = NewWePattern.State,
                OldPattern = OldWePattern.State,
                WindPattern = CurWindPattern.State,
                WeatherEvent = CurWeatherEvent?.State,
                TransitionDelay = 0,
                Transitioning = false,
                Weight = Weight,
                updateInstant = updateInstant,
                LcgCurrentSeed = Rand.currentSeed,
                LcgMapGenSeed = Rand.mapGenSeed,
                LcgWorldSeed = Rand.worldSeed
            });

        }


        public void TriggerTransition()
        {
            TriggerTransition(30 + Rand.NextFloat() * 60 * 60 / ws.api.World.Calendar.SpeedOfTime);
        }

        public void TriggerTransition(float delay)
        {
            Transitioning = true;
            TransitionDelay = delay;

            Weight = 0;
            OldWePattern = NewWePattern;
            NewWePattern = RandomWeatherPattern();
            if (NewWePattern != OldWePattern) NewWePattern.OnBeginUse();


            wsServer.SendWeatherStateUpdate(new WeatherState()
            {
                RegionX = regionX,
                RegionZ = regionZ,
                NewPattern = NewWePattern.State,
                OldPattern = OldWePattern.State,
                WindPattern = CurWindPattern.State,
                WeatherEvent = CurWeatherEvent?.State,
                TransitionDelay = TransitionDelay,
                Transitioning = true,
                Weight = Weight,
                LcgCurrentSeed = Rand.currentSeed,
                LcgMapGenSeed = Rand.mapGenSeed,
                LcgWorldSeed = Rand.worldSeed
            });
        }

        public WeatherEvent RandomWeatherEvent()
        {
            float totalChance = 0;
            for (int i = 0; i < WeatherEvents.Length; i++)
            {
                WeatherEvents[i].updateHereChance(weatherData.climateCond.Rainfall, weatherData.climateCond.Temperature);
                totalChance += WeatherEvents[i].hereChance;
            }

            float rndVal = Rand.NextFloat() * totalChance;

            for (int i = 0; i < WeatherEvents.Length; i++)
            {
                rndVal -= WeatherEvents[i].config.Weight;
                if (rndVal <= 0)
                {
                    return WeatherEvents[i];
                }
            }

            return WeatherEvents[WeatherEvents.Length - 1];
        }

        public WeatherPattern RandomWeatherPattern()
        {
            float totalChance = 0;
            for (int i = 0; i < WeatherPatterns.Length; i++)
            {
                WeatherPatterns[i].updateHereChance(weatherData.climateCond.Rainfall, weatherData.climateCond.Temperature);
                totalChance += WeatherPatterns[i].hereChance;
            }

            float rndVal = Rand.NextFloat() * totalChance;

            for (int i = 0; i < WeatherPatterns.Length; i++)
            {
                rndVal -= WeatherPatterns[i].hereChance;
                if (rndVal <= 0)
                {
                    return WeatherPatterns[i];
                }
            }

            return WeatherPatterns[WeatherPatterns.Length - 1];
        }




        public double GetBlendedCloudThicknessAt(int cloudTilePosX, int cloudTilePosZ)
        {
            if (IsDummy) return 0;

            int x = cloudTilePosX - cloudTilebasePosX;
            int z = cloudTilePosZ - cloudTilebasePosZ;

            return (NewWePattern.GetCloudDensityAt(x, z) * Weight + OldWePattern.GetCloudDensityAt(x, z) * (1 - Weight));
        }

        public double GetBlendedCloudOpaqueness()
        {
            return NewWePattern.State.nowbaseOpaqueness * Weight + OldWePattern.State.nowbaseOpaqueness * (1 - Weight);
        }

        public double GetBlendedCloudBrightness(float b)
        {
            float w = weatherData.Ambient.CloudBrightness.Weight;

            float bc = weatherData.Ambient.CloudBrightness.Value * weatherData.Ambient.SceneBrightness.Value;

            return b * (1-w) + bc * w;
        }

        public double GetBlendedThinCloudModeness()
        {
            return NewWePattern.State.nowThinCloudModeness * Weight + OldWePattern.State.nowThinCloudModeness * (1 - Weight);
        }

        public double GetBlendedUndulatingCloudModeness()
        {
            return NewWePattern.State.nowUndulatingCloudModeness * Weight + OldWePattern.State.nowUndulatingCloudModeness * (1 - Weight);
        }

        internal void EnsureCloudTileCacheIsFresh(Vec3i tilePos)
        {
            if (IsDummy) return;

            NewWePattern.EnsureCloudTileCacheIsFresh(tilePos);
            OldWePattern.EnsureCloudTileCacheIsFresh(tilePos);
        }

        

        public byte[] ToBytes()
        {
            WeatherState state = new WeatherState()
            {
                NewPattern = NewWePattern?.State ?? null,
                OldPattern = OldWePattern?.State ?? null,
                WindPattern = CurWindPattern?.State ?? null,
                WeatherEvent = CurWeatherEvent?.State ?? null,
                Weight = Weight,
                TransitionDelay = TransitionDelay,
                Transitioning = Transitioning,
                LastUpdateTotalHours = LastUpdateTotalHours,
                LcgCurrentSeed = Rand.currentSeed,
                LcgMapGenSeed = Rand.mapGenSeed,
                LcgWorldSeed = Rand.worldSeed,
                SnowAccumSnapshots = SnowAccumSnapshots?.Values
            };

            return SerializerUtil.Serialize(state);
        }

        internal void FromBytes(byte[] data)
        {
            if (data == null)
            {
                LoadRandomPattern();
                NewWePattern.OnBeginUse();
            }
            else
            {
                WeatherState state = SerializerUtil.Deserialize<WeatherState>(data);

                if (state.NewPattern != null)
                {
                    NewWePattern = WeatherPatterns[GameMath.Clamp(state.NewPattern.Index, 0, WeatherPatterns.Length - 1)];
                    NewWePattern.State = state.NewPattern;
                } else
                {
                    NewWePattern = WeatherPatterns[0];
                }

                if (state.OldPattern != null && state.OldPattern.Index < WeatherPatterns.Length)
                {
                    OldWePattern = WeatherPatterns[GameMath.Clamp(state.OldPattern.Index, 0, WeatherPatterns.Length - 1)];
                    OldWePattern.State = state.OldPattern;
                } else
                {
                    OldWePattern = WeatherPatterns[0];
                }

                if (state.WindPattern != null)
                {
                    CurWindPattern = WindPatterns[GameMath.Clamp(state.WindPattern.Index, 0, WindPatterns.Length - 1)];
                    CurWindPattern.State = state.WindPattern;
                }

                Weight = state.Weight;
                TransitionDelay = state.TransitionDelay;
                Transitioning = state.Transitioning;
                LastUpdateTotalHours = state.LastUpdateTotalHours;
                Rand.worldSeed = state.LcgWorldSeed;
                Rand.currentSeed = state.LcgCurrentSeed;
                Rand.mapGenSeed = state.LcgMapGenSeed;

                double nowTotalHours = ws.api.World.Calendar.TotalHours;
                // Cap that at max 1 year or we simulate forever on startup
                LastUpdateTotalHours = Math.Max(LastUpdateTotalHours, nowTotalHours - 12 * 12 * 24);

                SnowAccumSnapshots = new RingArray<SnowAccumSnapshot>((int)(ws.api.World.Calendar.DaysPerYear * ws.api.World.Calendar.HoursPerDay) + 1, state.SnowAccumSnapshots);

                if (state.WeatherEvent != null)
                {
                    CurWeatherEvent = WeatherEvents[state.WeatherEvent.Index];
                    CurWeatherEvent.State = state.WeatherEvent;
                }

                if (CurWeatherEvent == null)
                {
                    CurWeatherEvent = RandomWeatherEvent();
                    CurWeatherEvent.OnBeginUse();
                }
            }

        }
    }

}
