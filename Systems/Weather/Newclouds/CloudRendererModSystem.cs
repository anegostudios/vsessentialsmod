using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace FluffyClouds {

    public class FluffyCloudsModSystem : ModSystem {

        ICoreClientAPI capi;

        CloudRendererMap map;
        CloudRendererVolumetric volumetric;

        string[] modes = new string[] { "off", "volumetric", "simple" };

        public override void StartClientSide(ICoreClientAPI capi) {

            this.capi = capi;

            capi.Settings.Int.AddWatcher("cloudRenderMode", (int i) => {

                registerCloudRenderer(i);
                capi.ShowChatMessage("cloud renderer: " + modes[i]);
            });

            capi.Event.LevelFinalize += () => {

                map = new CloudRendererMap(this, capi);
                volumetric = new CloudRendererVolumetric(this, capi, map);

                capi.Event.RegisterRenderer(map, EnumRenderStage.OIT, "cloudsmap");

                int mode = capi.Settings.Int.Get("cloudRenderMode");
                registerCloudRenderer(mode);
            };

            capi.Event.LeaveWorld += () => {

                map?.Dispose();
                volumetric?.Dispose();
            };

        }

        void registerCloudRenderer(int i){

            if (i != 1)
            {
                capi.Event.UnregisterRenderer(volumetric, EnumRenderStage.OIT);
            } else
            {
                capi.Event.RegisterRenderer(volumetric, EnumRenderStage.OIT, "cloudsvolumetric");
            }
        }
    }
}