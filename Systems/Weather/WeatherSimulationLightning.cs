using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

#nullable disable

namespace Vintagestory.GameContent
{

    public class WeatherSimulationLightning : IRenderer
    {
        WeatherSystemBase weatherSys;
        WeatherSystemClient weatherSysc;
        ICoreClientAPI capi;

        IShaderProgram prog;

        public float lightningTime;
        public float lightningIntensity;
        public AmbientModifier LightningAmbient;
        public AmbientModifier actualSunGlowAmb = new AmbientModifier().EnsurePopulated();
        float nearLightningCoolDown = 0f;

        public double RenderOrder => 0.35;
        public int RenderRange => 9999;

        public List<LightningFlash> lightningFlashes = new List<LightningFlash>();



        public WeatherSimulationLightning(ICoreAPI api, WeatherSystemBase weatherSys)
        {
            this.weatherSys = weatherSys;
            weatherSysc = weatherSys as WeatherSystemClient;
            this.capi = api as ICoreClientAPI;

            if (api.Side == EnumAppSide.Client)
            {
                LightningAmbient = new AmbientModifier().EnsurePopulated();

                capi.Ambient.CurrentModifiers["lightningambient"] = LightningAmbient;

                capi.Event.ReloadShader += LoadShader;
                LoadShader();

                capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "lightning");
            } else
            {
                api.Event.RegisterGameTickListener(OnServerTick, 40, 3);

                api.ChatCommands.GetOrCreate("debug")
                    .BeginSubCommand("lntest")
                        .BeginSubCommand("spawn")
                            .WithDescription("Lightning test")
                            .WithArgs(api.ChatCommands.Parsers.OptionalInt("range", 10))
                            .RequiresPlayer()
                            .RequiresPrivilege(Privilege.controlserver)
                            .HandleWith(OnCmdLineTestServer)
                        .EndSubCommand()
                        .BeginSubCommand("clear")
                            .WithDescription("Clear all lightning flashes")
                            .RequiresPrivilege(Privilege.controlserver)
                            .HandleWith(OnCmdLineTestServerClear)
                        .EndSubCommand()
                    .EndSubCommand()
                    ;
            }
        }

        private TextCommandResult OnCmdLineTestServerClear(TextCommandCallingArgs args)
        {
            foreach (var val in lightningFlashes) val.Dispose();
            lightningFlashes.Clear();
            return TextCommandResult.Success("Cleared all lightning flashes");
        }

        private TextCommandResult OnCmdLineTestServer(TextCommandCallingArgs args)
        {
            var range = (int)args.Parsers[0].GetValue();
            var pos = args.Caller.Entity.Pos.AheadCopy(range).XYZ;
            weatherSys.SpawnLightningFlash(pos);
            return TextCommandResult.Success($"Spawned lightning {range} block ahead");
        }

        public void ClientTick(float dt)
        {
            WeatherDataSnapshot weatherData = weatherSysc.BlendedWeatherData;

            if (weatherSysc.clientClimateCond.Temperature >= weatherData.lightningMinTemp)
            {
                float deepnessSub = GameMath.Clamp(1 - (float)Math.Pow(capi.World.Player.Entity.Pos.Y / capi.World.SeaLevel * 1.5 - 0.5, 1.5) - WeatherSimulationSound.roomVolumePitchLoss * 0.5f, 0, 1);

                var rand = capi.World.Rand;

                double rndval = rand.NextDouble();
                rndval -= weatherData.distantLightningRate * weatherSysc.clientClimateCond.RainCloudOverlay;
                if (rndval <= 0)
                {
                    lightningTime = 0.07f + (float)rand.NextDouble() * 0.17f;
                    lightningIntensity = 0.25f + (float)rand.NextDouble();

                    float pitch = GameMath.Clamp((float)rand.NextDouble() * 0.3f + lightningTime / 2 + lightningIntensity / 2 - deepnessSub / 2, 0.6f, 1.15f);
                    float volume = GameMath.Clamp(Math.Min(1, 0.25f + lightningTime + lightningIntensity / 2) - 2f * deepnessSub, 0, 1);

                    capi.World.PlaySoundAt(new AssetLocation("sounds/weather/lightning-distant.ogg"), 0, 0, 0, null, EnumSoundType.Weather, pitch, 32, volume);
                }
                else if (nearLightningCoolDown <= 0)
                {
                    rndval -= weatherData.nearLightningRate * weatherSysc.clientClimateCond.RainCloudOverlay;
                    if (rndval <= 0)
                    {
                        lightningTime = 0.07f + (float)rand.NextDouble() * 0.17f;
                        lightningIntensity = 1 + (float)rand.NextDouble() * 0.9f;

                        float pitch = GameMath.Clamp(0.75f + (float)rand.NextDouble() * 0.3f - deepnessSub / 2, 0.5f, 1.2f);
                        float volume = GameMath.Clamp(0.5f + (float)rand.NextDouble() * 0.5f - 2f * deepnessSub, 0, 1);
                        AssetLocation loc;

                        if (rand.NextDouble() > 0.25)
                        {
                            loc = new AssetLocation("sounds/weather/lightning-near.ogg");
                            nearLightningCoolDown = 5;
                        }
                        else
                        {
                            loc = new AssetLocation("sounds/weather/lightning-verynear.ogg");
                            nearLightningCoolDown = 10;
                        }


                        capi.World.PlaySoundAt(loc, 0, 0, 0, null, EnumSoundType.Weather, pitch, 32, volume);
                    }
                }
            }
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            if (prog.LoadError) return;

            if (stage == EnumRenderStage.Opaque)
            {
                prog.Use();
                prog.UniformMatrix("projection", capi.Render.CurrentProjectionMatrix);
                prog.UniformMatrix("view", capi.Render.CameraMatrixOriginf);

                for (int i = 0; i < lightningFlashes.Count; i++)
                {
                    var lflash = lightningFlashes[i];
                    lflash.Render(dt);

                    if (!lflash.Alive)
                    {
                        lflash.Dispose();
                        lightningFlashes.RemoveAt(i);
                        i--;
                    }
                }

                prog.Stop();
                return;
            }

            if (stage == EnumRenderStage.Done)
            {
                AmbientModifier sunGlowAmb = capi.Ambient.CurrentModifiers["sunglow"];
                actualSunGlowAmb.FogColor.Weight = sunGlowAmb.FogColor.Weight;

                dt = Math.Min(0.5f, dt);

                if (nearLightningCoolDown > 0)
                {
                    nearLightningCoolDown -= dt;
                }

                return;
            }

            if (lightningTime > 0)
            {
                float mul = Math.Min(10 * lightningIntensity * lightningTime, 1.5f);

                WeatherDataSnapshot weatherData = weatherSysc.BlendedWeatherData;

                LightningAmbient.CloudBrightness.Value = Math.Max(weatherData.Ambient.SceneBrightness.Value, mul);
                LightningAmbient.FogBrightness.Value = Math.Max(weatherData.Ambient.FogBrightness.Value, mul);

                LightningAmbient.CloudBrightness.Weight = Math.Min(1, mul);
                LightningAmbient.FogBrightness.Weight = Math.Min(1, mul);

                float sceneBrightIncrease = GameMath.Min(mul, GameMath.Max(0, lightningIntensity - 0.75f));

                if (sceneBrightIncrease > 0)
                {
                    LightningAmbient.SceneBrightness.Weight = Math.Min(1, sceneBrightIncrease);
                    LightningAmbient.SceneBrightness.Value = 1;

                    AmbientModifier sunGlowAmb = capi.Ambient.CurrentModifiers["sunglow"];

                    float nowWeight = GameMath.Clamp(1 - sceneBrightIncrease, 0, 1);

                    sunGlowAmb.FogColor.Weight = Math.Min(sunGlowAmb.FogColor.Weight, nowWeight);
                    sunGlowAmb.AmbientColor.Weight = Math.Min(sunGlowAmb.AmbientColor.Weight, nowWeight);
                }

                lightningTime -= dt / 1.7f;

                if (lightningTime <= 0)
                {
                    // Restore previous values
                    AmbientModifier sunGlowAmb = capi.Ambient.CurrentModifiers["sunglow"];
                    sunGlowAmb.FogColor.Weight = actualSunGlowAmb.FogColor.Weight;
                    sunGlowAmb.AmbientColor.Weight = actualSunGlowAmb.AmbientColor.Weight;

                    LightningAmbient.CloudBrightness.Weight = 0;
                    LightningAmbient.FogBrightness.Weight = 0;
                    LightningAmbient.SceneBrightness.Weight = 0;
                }
            }



        }

        public void OnServerTick(float dt)
        {
            for (int i = 0; i < lightningFlashes.Count; i++)
            {
                var lflash = lightningFlashes[i];
                lflash.GameTick(dt);

                if (!lflash.Alive)
                {
                    lflash.Dispose();
                    lightningFlashes.RemoveAt(i);
                    i--;
                }
            }
        }

        public bool LoadShader()
        {
            prog = capi.Shader.NewShaderProgram();

            prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

            capi.Shader.RegisterFileShaderProgram("lines", prog);

            return prog.Compile();
        }



        

        public void genLightningFlash(Vec3d pos, int? seed = null)
        {
            var lflash = new LightningFlash(weatherSys, capi, seed, pos);
            lflash.ClientInit();
            lightningFlashes.Add(lflash);
        }


        public void Dispose() {
            foreach (var lflash in lightningFlashes)
            {
                lflash.Dispose();
            }
        }
    }


}