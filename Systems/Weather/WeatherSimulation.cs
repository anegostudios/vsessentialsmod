using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
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
    public class WeatherSimulation
    {
        // Persistent data
        public bool Transitioning;
        public float TransitionDelay;
        public WeatherPattern NewWePattern;
        public WeatherPattern OldWePattern;

        public WindPattern CurWindPattern;

        public float Weight;
        public double LastUpdateTotalHours;
        public LCGRandom Rand;
        public int regionX;
        public int regionZ;

        



        // Runtime data
        public WeatherDataSnapshot weatherData = new WeatherDataSnapshot();
        public bool IsInitialized;
        public bool IsDummy;

        public WeatherPattern[] WeatherPatterns;
        public WindPattern[] WindPatterns;
        protected WeatherSystemBase ws;
        protected WeatherSystemServer wsServer;
        protected ICoreClientAPI capi;
        

        protected float quarterSecAccum = 0;
        protected BlockPos regionCenterPos;
        protected Vec3d tmpVecPos = new Vec3d();

        


        public WeatherSimulation(WeatherSystemBase ws, int regionX, int regionZ)
        {
            this.ws = ws;
            this.regionX = regionX;
            this.regionZ = regionZ;

            int regsize = ws.api.World.BlockAccessor.RegionSize;

            regionCenterPos = new BlockPos(regionX * regsize + regsize/2, 0, regionZ * regsize + regsize/2);


            Rand = new LCGRandom(ws.api.World.Seed);
            Rand.InitPositionSeed(regionX, regionZ);
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
            WeatherPatterns = new WeatherPattern[ws.weatherConfigs.Length];
            for (int i = 0; i < ws.weatherConfigs.Length; i++)
            {
                WeatherPatterns[i] = new WeatherPattern(ws, ws.weatherConfigs[i], Rand);
                WeatherPatterns[i].State.Index = i;
            }

            WindPatterns = new WindPattern[ws.windConfigs.Length];
            for (int i = 0; i < ws.windConfigs.Length; i++) {
                WindPatterns[i] = new WindPattern(ws.api, ws.windConfigs[i], i, Rand, seed);
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

            Weight = 1;

            wsServer?.SendWeatherStateUpdate(new WeatherState()
            {
                RegionX = regionX,
                RegionZ = regionZ,
                NewPattern = NewWePattern.State,
                OldPattern = OldWePattern.State,
                WindPattern = CurWindPattern.State,
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

            IsInitialized = true;
        }

        
        public void UpdateWeatherData()
        {
            // 1-(1.1-x)^4
            // http://fooplot.com/#W3sidHlwZSI6MCwiZXEiOiIxLSgxLjEteCleNCIsImNvbG9yIjoiIzAwMDAwMCJ9LHsidHlwZSI6MTAwMCwid2luZG93IjpbIjAiLCIxIiwiMCIsIjEiXX1d
            float drynessMultiplier = GameMath.Clamp(1 - (float)Math.Pow(1.1 - weatherData.climateCond.Rainfall, 4), 0, 1);
            float fogMultiplier = drynessMultiplier;


            weatherData.Ambient.FlatFogDensity.Value = (NewWePattern.State.nowMistDensity * Weight + OldWePattern.State.nowMistDensity * (1 - Weight)) / 250f;
            weatherData.Ambient.FlatFogDensity.Weight = 1;
            weatherData.Ambient.FlatFogDensity.Weight *= fogMultiplier;


            weatherData.Ambient.FlatFogYPos.Value = NewWePattern.State.nowMistYPos * Weight + OldWePattern.State.nowMistYPos * (1 - Weight);
            weatherData.Ambient.FlatFogYPos.Weight = 1;

            weatherData.Ambient.FogDensity.Value = ((capi == null ? 0 : capi.Ambient.Base.FogDensity.Value) + NewWePattern.State.nowFogDensity * Weight + OldWePattern.State.nowFogDensity * (1 - Weight)) / 1000f;
            weatherData.Ambient.FogDensity.Weight = fogMultiplier;

            weatherData.Ambient.CloudBrightness.Value = NewWePattern.State.nowBrightness * Weight + OldWePattern.State.nowBrightness * (1 - Weight);
            weatherData.Ambient.CloudBrightness.Weight = 1;

            if (Weight > 0.5) weatherData.BlendedPrecType = NewWePattern.State.nowPrecType;
            else weatherData.BlendedPrecType = OldWePattern.State.nowPrecType;

            weatherData.nowPrecType = weatherData.BlendedPrecType;
            if (weatherData.nowPrecType == EnumPrecipitationType.Auto)
            {
                weatherData.nowPrecType = weatherData.climateCond.Temperature < weatherData.snowThresholdTemp ? EnumPrecipitationType.Snow : EnumPrecipitationType.Rain;
            }

            weatherData.PrecParticleSize = NewWePattern.State.nowPrecParticleSize * Weight + OldWePattern.State.nowPrecParticleSize * (1 - Weight);
            weatherData.PrecIntensity = drynessMultiplier * NewWePattern.State.nowPrecIntensity * Weight + OldWePattern.State.nowPrecIntensity * (1 - Weight);

            weatherData.Ambient.CloudDensity.Value = NewWePattern.State.nowbaseThickness * Weight + OldWePattern.State.nowbaseThickness * (1 - Weight);
            weatherData.Ambient.CloudDensity.Weight = 1;


            weatherData.Ambient.SceneBrightness.Value = NewWePattern.State.nowSceneBrightness * Weight + OldWePattern.State.nowSceneBrightness * (1 - Weight);
            weatherData.Ambient.SceneBrightness.Weight = 1f;

            weatherData.Ambient.FogBrightness.Value = NewWePattern.State.nowFogBrightness * Weight + OldWePattern.State.nowFogBrightness * (1 - Weight);
            weatherData.Ambient.FogBrightness.Weight = 1f;
        }

        public void TickEvery25ms(float dt)
        {
            if (ws.api.Side == EnumAppSide.Client)
            {
                clientUpdate(dt);
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

            if (ws.autoChangePatterns && ws.api.Side == EnumAppSide.Server && ws.api.World.Calendar.TotalHours > CurWindPattern.State.ActiveUntilTotalHours)
            {
                CurWindPattern = WindPatterns[Rand.NextInt(WindPatterns.Length)];
                CurWindPattern.OnBeginUse();

                wsServer.SendWeatherStateUpdate(new WeatherState()
                {
                    RegionX = regionX,
                    RegionZ = regionZ,
                    NewPattern = NewWePattern.State,
                    OldPattern = OldWePattern.State,
                    WindPattern = CurWindPattern.State,
                    TransitionDelay = TransitionDelay,
                    Transitioning = Transitioning,
                    Weight = Weight,
                    LcgCurrentSeed = Rand.currentSeed,
                    LcgMapGenSeed = Rand.mapGenSeed,
                    LcgWorldSeed = Rand.worldSeed
                });
            }


            NewWePattern.Update(dt);
            OldWePattern.Update(dt);
            
            CurWindPattern.Update(dt);

            float curWindSpeed = weatherData.curWindSpeed.X;
            float targetWindSpeed = (float)GetWindSpeed(ws.api.World.SeaLevel);

            curWindSpeed += GameMath.Clamp((targetWindSpeed - curWindSpeed) * dt, -0.001f, 0.001f);

            weatherData.curWindSpeed.X = curWindSpeed;

            quarterSecAccum += dt;
            if (quarterSecAccum > 0.25f)
            {
                regionCenterPos.Y = ws.api.World.BlockAccessor.GetRainMapHeightAt(regionCenterPos);
                ClimateCondition nowcond = ws.api.World.BlockAccessor.GetClimateAt(regionCenterPos);
                if (nowcond != null)
                {

                    weatherData.climateCond = nowcond;
                }
                quarterSecAccum = 0;
            }
        }


        private void clientUpdate(float dt)
        {
            EntityPlayer eplr = (ws.api as ICoreClientAPI).World.Player.Entity;
            
            regionCenterPos.Y = (int)eplr.Pos.Y;

            weatherData.nearLightningRate = NewWePattern.State.nowNearLightningRate * Weight + OldWePattern.State.nowNearLightningRate * (1 - Weight);
            weatherData.distantLightningRate = NewWePattern.State.nowDistantLightningRate * Weight + OldWePattern.State.nowDistantLightningRate * (1 - Weight);
            weatherData.lightningMinTemp = NewWePattern.State.nowLightningMinTempature * Weight + OldWePattern.State.nowLightningMinTempature * (1 - Weight);
        }

        public double GetWindSpeed(double posY)
        {
            double strength = CurWindPattern.Strength;

            if (posY > ws.api.World.SeaLevel)
            {
                // Greater wind at greater heights
                strength *= Math.Max(1, (posY - ws.api.World.SeaLevel) / 100.0);
            }
            else
            {
                // Much muuuch lower winds at lower heights
                strength /= 1 + (ws.api.World.SeaLevel - posY) / 4;
            }

            return strength;
        }

        public bool SetWindPattern(string code, bool updateInstant)
        {
            WindPattern pattern = WindPatterns.FirstOrDefault(p => p.config.Code == code);
            if (pattern == null) return false;

            CurWindPattern = pattern;
            CurWindPattern.OnBeginUse();

            wsServer.SendWeatherStateUpdate(new WeatherState()
            {
                RegionX = regionX,
                RegionZ = regionZ,
                NewPattern = NewWePattern.State,
                OldPattern = OldWePattern.State,
                WindPattern = CurWindPattern.State,
                TransitionDelay = 0,
                Transitioning = false,
                Weight = Weight,
                updateInstant = updateInstant,
                LcgCurrentSeed = Rand.currentSeed,
                LcgMapGenSeed = Rand.mapGenSeed,
                LcgWorldSeed = Rand.worldSeed
            });

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

            wsServer.SendWeatherStateUpdate(new WeatherState()
            {
                RegionX = regionX,
                RegionZ = regionZ,
                NewPattern = NewWePattern.State,
                OldPattern = OldWePattern.State,
                WindPattern = CurWindPattern.State,
                TransitionDelay = 0,
                Transitioning = false,
                Weight = Weight,
                updateInstant = updateInstant,
                LcgCurrentSeed = Rand.currentSeed,
                LcgMapGenSeed = Rand.mapGenSeed,
                LcgWorldSeed = Rand.worldSeed
            });

            return true;
        }

        public void TriggerTransition()
        {
            TriggerTransition(30 + (float)Rand.NextDouble() * 60 * 60 / ws.api.World.Calendar.SpeedOfTime);
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
                TransitionDelay = TransitionDelay,
                Transitioning = true,
                Weight = Weight,
                LcgCurrentSeed = Rand.currentSeed,
                LcgMapGenSeed = Rand.mapGenSeed,
                LcgWorldSeed = Rand.worldSeed
            });
        }
       
        public WeatherPattern RandomWeatherPattern()
        {
            float totalChance = 0;
            for (int i = 0; i < WeatherPatterns.Length; i++)
            {
                WeatherPatterns[i].updateHereChance(weatherData.climateCond.Rainfall, weatherData.climateCond.Temperature);
                totalChance += WeatherPatterns[i].hereChance;
            }

            float rndVal = (float)Rand.NextDouble() * totalChance;

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




        public double GetBlendedCloudThicknessAt(int dx, int dz)
        {
            if (IsDummy) return 0;

            return (NewWePattern.GetCloudDensityAt(dx, dz) * Weight + OldWePattern.GetCloudDensityAt(dx, dz) * (1 - Weight));
        }

        public double GetBlendedCloudOpaqueness()
        {
            return NewWePattern.State.nowbaseOpaqueness * Weight + OldWePattern.State.nowbaseOpaqueness * (1 - Weight);
        }

        public double GetBlendedThinCloudModeness()
        {
            return NewWePattern.State.nowThinCloudModeness * Weight + OldWePattern.State.nowThinCloudModeness * (1 - Weight);
        }

        public double GetBlendedUndulatingCloudModeness()
        {
            return NewWePattern.State.nowUndulatingCloudModeness * Weight + OldWePattern.State.nowUndulatingCloudModeness * (1 - Weight);
        }

        internal void EnsureNoiseCacheIsFresh()
        {
            if (IsDummy) return;

            NewWePattern.EnsureNoiseCacheIsFresh();
            OldWePattern.EnsureNoiseCacheIsFresh();
        }

        

        public byte[] ToBytes()
        {
            WeatherState state = new WeatherState()
            {
                NewPattern = NewWePattern?.State ?? null,
                OldPattern = OldWePattern?.State ?? null,
                WindPattern = CurWindPattern?.State ?? null,
                Weight = Weight,
                TransitionDelay = TransitionDelay,
                Transitioning = Transitioning,
                LastUpdateTotalHours = LastUpdateTotalHours,
                LcgCurrentSeed = Rand.currentSeed,
                LcgMapGenSeed = Rand.mapGenSeed,
                LcgWorldSeed = Rand.worldSeed
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
                    NewWePattern = WeatherPatterns[state.NewPattern.Index];
                    NewWePattern.State = state.NewPattern;
                } else
                {
                    NewWePattern = WeatherPatterns[0];
                }

                if (state.OldPattern != null)
                {
                    OldWePattern = WeatherPatterns[state.OldPattern.Index];
                    OldWePattern.State = state.OldPattern;
                } else
                {
                    OldWePattern = WeatherPatterns[0];
                }

                if (state.WindPattern != null)
                {
                    CurWindPattern = WindPatterns[state.WindPattern.Index];
                    CurWindPattern.State = state.WindPattern;
                }

                Weight = state.Weight;
                TransitionDelay = state.TransitionDelay;
                Transitioning = state.Transitioning;
                LastUpdateTotalHours = state.LastUpdateTotalHours;
                Rand.worldSeed = state.LcgWorldSeed;
                Rand.currentSeed = state.LcgCurrentSeed;
                Rand.mapGenSeed = state.LcgMapGenSeed;
            }

        }
    }

}
