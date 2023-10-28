namespace Vintagestory.ServerMods
{
    /*public class NoiseWeatherPattern
    {
        public float scale;
        LCGRandom rand;
        WeatherPatternConfig[] weatherConfigs;

        public NoiseWeatherPattern(long seed, float scale, WeatherPatternConfig[] weatherConfigs)
        {
            rand = new LCGRandom(seed);
            this.scale = scale;
            this.weatherConfigs = weatherConfigs;
        }

        public int GetLandformIndexAt(int unscaledXpos, int unscaledZpos, int temp, int rain)
        {
            float xpos = (float)unscaledXpos / scale;
            float zpos = (float)unscaledZpos / scale;

            int xposInt = (int)xpos;
            int zposInt = (int)zpos;

            int parentIndex = GetParentLandformIndexAt(xposInt, zposInt, temp, rain);

            return parentIndex;
        }


        public int GetParentLandformIndexAt(int xpos, int zpos, int temp, int rain)
        {
            rand.InitPositionSeed(xpos, zpos);

            double weightSum = 0;
            int i;
            for (i = 0; i < weatherConfigs.Length; i++)
            {
                WeatherPatternConfig wp = weatherConfigs[i];

                wp.updateHereChance(weatherData.climateCond.Rainfall, weatherData.climateCond.Temperature);
                totalChance += WeatherPatterns[i].hereChance;

                if (landforms.Variants[i].UseClimateMap)
                {
                    int distRain = rain - GameMath.Clamp(rain, landforms.Variants[i].MinRain, landforms.Variants[i].MaxRain);
                    double distTemp = temp - GameMath.Clamp(temp, landforms.Variants[i].MinTemp, landforms.Variants[i].MaxTemp);
                    if (distRain > 0 || distTemp > 0) weight = 0;
                }

                landforms.Variants[i].WeightTmp = weight;
                weightSum += weight;
            }

            double randval = weightSum * NextInt(10000) / 10000.0;

            for (i = 0; i < landforms.Variants.Length; i++)
            {
                randval -= landforms.Variants[i].WeightTmp;
                if (randval <= 0) return landforms.Variants[i].index;
            }

            return landforms.Variants[i].index;
        }


    }*/
}
