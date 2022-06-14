using ProtoBuf;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class MapLayerUpdate
    {
        public MapLayerData[] Maplayers;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class MapLayerData
    {
        public string ForMapLayer;
        public byte[] Data;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OnMapToggle
    {
        public bool OpenOrClose;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OnViewChangedPacket
    {
        public List<Vec2i> NowVisible = new List<Vec2i>();
        public List<Vec2i> NowHidden = new List<Vec2i>();
    }


    public class WorldMapManager : ModSystem, IWorldMapManager
    {
        public Dictionary<string, Type> MapLayerRegistry = new Dictionary<string, Type>();

        ICoreAPI api;

        // Client side stuff
        ICoreClientAPI capi;
        IClientNetworkChannel clientChannel;

        public GuiDialogWorldMap worldMapDlg;
        public List<MapLayer> MapLayers = new List<MapLayer>();
        public bool IsOpened => worldMapDlg?.IsOpened() == true;


        // Client and Server side stuff
        Thread mapLayerGenThread;
        public bool IsShuttingDown { get; set; }

        // Server side stuff
        IServerNetworkChannel serverChannel;


        




        public override bool ShouldLoad(EnumAppSide side)
        {
            return true;
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            RegisterDefaultMapLayers();
            this.api = api;
        }

        public void RegisterDefaultMapLayers()
        {
            RegisterMapLayer<ChunkMapLayer>("chunks");
            RegisterMapLayer<PlayerMapLayer>("players");
            RegisterMapLayer<WaypointMapLayer>("waypoints");
        }

        public void RegisterMapLayer<T>(string code) where T : MapLayer
        {
            MapLayerRegistry[code] = typeof(T);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            capi = api;
            capi.Event.BlockTexturesLoaded += OnLoaded;
            capi.Event.LevelFinalize += OnLvlFinalize;
            capi.Event.RegisterGameTickListener(OnClientTick, 20);

            capi.Settings.AddWatcher<bool>("showMinimapHud", (on) => {
                ToggleMap(EnumDialogType.HUD);
            });

            capi.Event.LeaveWorld += () =>
            {
                IsShuttingDown = true;
                int i = 0;
                while (mapLayerGenThread != null && mapLayerGenThread.IsAlive && i < 20)
                {
                    Thread.Sleep(50);
                    i++;
                }

                worldMapDlg?.Dispose();

                foreach (var layer in MapLayers)
                {
                    layer?.OnShutDown();
                    layer?.Dispose();
                }
            };

            clientChannel =
                api.Network.RegisterChannel("worldmap")
               .RegisterMessageType(typeof(MapLayerUpdate))
               .RegisterMessageType(typeof(OnViewChangedPacket))
               .RegisterMessageType(typeof(OnMapToggle))
               .SetMessageHandler<MapLayerUpdate>(OnMapLayerDataReceivedClient)
            ;
        }


        private void onWorldMapLinkClicked(LinkTextComponent linkcomp)
        {
            string[] xyzstr = linkcomp.Href.Substring("worldmap://".Length).Split('=');
            int x = xyzstr[1].ToInt();
            int y = xyzstr[2].ToInt();
            int z = xyzstr[3].ToInt();
            string text = xyzstr.Length >= 5 ? xyzstr[4] : "";

            if (worldMapDlg == null || !worldMapDlg.IsOpened() || (worldMapDlg.IsOpened() && worldMapDlg.DialogType == EnumDialogType.HUD))
            {
                ToggleMap(EnumDialogType.Dialog);
            }

            bool exists = false;
            var elem = (worldMapDlg.SingleComposer.GetElement("mapElem") as GuiElementMap);
            var wml = (elem?.mapLayers.FirstOrDefault(ml => ml is WaypointMapLayer) as WaypointMapLayer);
            Vec3d pos = new Vec3d(x, y, z);
            if (wml != null)
            {
                foreach (var wp in wml.ownWaypoints)
                {
                    if (wp.Position.Equals(pos, 0.01))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (!exists)
            {
                capi.SendChatMessage(string.Format("/waypoint addati {0} ={1} ={2} ={3} {4} {5} {6}", "circle", x, y, z, false, "steelblue", text));
            }

            elem?.CenterMapTo(new BlockPos(x, y, z));
        }

        private void OnClientTick(float dt)
        {
            foreach (MapLayer layer in MapLayers)
            {
                layer.OnTick(dt);
            }
        }

        private void OnLvlFinalize()
        {
            if (capi != null && (capi.Settings.Bool["showMinimapHud"] || !capi.Settings.Bool.Exists("showMinimapHud")) && (worldMapDlg == null || !worldMapDlg.IsOpened()))
            {
                ToggleMap(EnumDialogType.HUD);
            }

        }

        private void OnMapLayerDataReceivedClient(MapLayerUpdate msg)
        {
            for (int i = 0; i < msg.Maplayers.Length; i++)
            {
                Type type = MapLayerRegistry[msg.Maplayers[i].ForMapLayer];
                MapLayers.FirstOrDefault(x => x.GetType() == type)?.OnDataFromServer(msg.Maplayers[i].Data);
            }
        }

        private void OnLoaded()
        {
            if (capi != null && capi.World.Config.GetBool("allowMap", true))
            {
                capi.Input.RegisterHotKey("worldmaphud", Lang.Get("Show/Hide Minimap"), GlKeys.F6, HotkeyType.HelpAndOverlays);
                capi.Input.RegisterHotKey("worldmapdialog", Lang.Get("Show World Map"), GlKeys.M, HotkeyType.HelpAndOverlays);
                capi.Input.SetHotKeyHandler("worldmaphud", OnHotKeyWorldMapHud);
                capi.Input.SetHotKeyHandler("worldmapdialog", OnHotKeyWorldMapDlg);
                capi.RegisterLinkProtocol("worldmap", onWorldMapLinkClicked);
            }

            foreach (var val in MapLayerRegistry)
            {
                MapLayers.Add((MapLayer)Activator.CreateInstance(val.Value, api, this));
            }
            

            foreach (MapLayer layer in MapLayers)
            {
                layer.OnLoaded();
            }

            mapLayerGenThread = new Thread(new ThreadStart(() =>
            {
                while (!IsShuttingDown)
                {
                    foreach (MapLayer layer in MapLayers)
                    {
                        layer.OnOffThreadTick(20/1000f);
                    }

                    Thread.Sleep(20);
                }
            }));

            mapLayerGenThread.IsBackground = true;
            mapLayerGenThread.Start();

        }

        private bool OnHotKeyWorldMapHud(KeyCombination comb)
        {
            ToggleMap(EnumDialogType.HUD);
            return true;
        }

        private bool OnHotKeyWorldMapDlg(KeyCombination comb)
        {
            ToggleMap(EnumDialogType.Dialog);
            return true;
        }


        public void ToggleMap(EnumDialogType asType)
        {
            bool isDlgOpened = worldMapDlg != null && worldMapDlg.IsOpened();

            if (!capi.World.Config.GetBool("allowMap", true))
            {
                if (isDlgOpened) worldMapDlg.TryClose();
                return;
            }

            if (worldMapDlg != null)
            {
                if (!isDlgOpened)
                {
                    if (asType == EnumDialogType.HUD) capi.Settings.Bool["showMinimapHud"] = true;

                    worldMapDlg.Open(asType);
                    foreach (MapLayer layer in MapLayers) layer.OnMapOpenedClient();
                    clientChannel.SendPacket(new OnMapToggle() { OpenOrClose = true });

                    return;
                }
                else
                {
                    if (worldMapDlg.DialogType != asType)
                    {
                        worldMapDlg.Open(asType);
                        return;
                    }

                    if (asType == EnumDialogType.HUD)
                    {
                        capi.Settings.Bool["showMinimapHud"] = false;
                    }
                    else if (capi.Settings.Bool["showMinimapHud"])
                    {
                        worldMapDlg.Open(EnumDialogType.HUD);
                        return;
                    }
                                
                }

                worldMapDlg.TryClose();
                return;
            }

            worldMapDlg = new GuiDialogWorldMap(onViewChangedClient, capi);
            worldMapDlg.OnClosed += () => {
                foreach (MapLayer layer in MapLayers) layer.OnMapClosedClient();
                clientChannel.SendPacket(new OnMapToggle() { OpenOrClose = false });
                
            };

            worldMapDlg.Open(asType);
            foreach (MapLayer layer in MapLayers) layer.OnMapOpenedClient();
            clientChannel.SendPacket(new OnMapToggle() { OpenOrClose = true });

            if (asType == EnumDialogType.HUD) capi.Settings.Bool["showMinimapHud"] = true;
        }





        private void onViewChangedClient(List<Vec2i> nowVisible, List<Vec2i> nowHidden)
        {
            foreach (MapLayer layer in MapLayers)
            {
                layer.OnViewChangedClient(nowVisible, nowHidden);
            }

            clientChannel.SendPacket(new OnViewChangedPacket() { NowVisible = nowVisible, NowHidden = nowHidden });
        }

        
        public void TranslateWorldPosToViewPos(Vec3d worldPos, ref Vec2f viewPos)
        {
            worldMapDlg.TranslateWorldPosToViewPos(worldPos, ref viewPos);
        }

        public void SendMapDataToServer(MapLayer forMapLayer, byte[] data)
        {
            if (api.Side == EnumAppSide.Server) return;

            List<MapLayerData> maplayerdatas = new List<MapLayerData>();

            maplayerdatas.Add(new MapLayerData()
            {
                Data = data,
                ForMapLayer = MapLayerRegistry.FirstOrDefault(x => x.Value == forMapLayer.GetType()).Key
            });

            clientChannel.SendPacket(new MapLayerUpdate() { Maplayers = maplayerdatas.ToArray() });
        }


        #region Server Side


        public override void StartServerSide(ICoreServerAPI sapi)
        {
            sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, OnLoaded);
            sapi.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, () => IsShuttingDown = true);

            serverChannel =
               sapi.Network.RegisterChannel("worldmap")
               .RegisterMessageType(typeof(MapLayerUpdate))
               .RegisterMessageType(typeof(OnViewChangedPacket))
               .RegisterMessageType(typeof(OnMapToggle))
               .SetMessageHandler<OnMapToggle>(OnMapToggledServer)
               .SetMessageHandler<OnViewChangedPacket>(OnViewChangedServer)
               .SetMessageHandler<MapLayerUpdate>(OnMapLayerDataReceivedServer)
            ;
            
        }

        private void OnMapLayerDataReceivedServer(IServerPlayer fromPlayer, MapLayerUpdate msg)
        {
            for (int i = 0; i < msg.Maplayers.Length; i++)
            {
                Type type = MapLayerRegistry[msg.Maplayers[i].ForMapLayer];
                MapLayers.FirstOrDefault(x => x.GetType() == type)?.OnDataFromClient(msg.Maplayers[i].Data);
            }
        }

        private void OnMapToggledServer(IServerPlayer fromPlayer, OnMapToggle msg)
        {
            foreach (MapLayer layer in MapLayers)
            {
                if (layer.DataSide == EnumMapAppSide.Client) continue;

                if (msg.OpenOrClose)
                {
                    layer.OnMapOpenedServer(fromPlayer);
                }
                else
                {
                    layer.OnMapClosedServer(fromPlayer);
                }
            }
        }

        private void OnViewChangedServer(IServerPlayer fromPlayer, OnViewChangedPacket networkMessage)
        {
            List<Vec2i> empty = new List<Vec2i>();

            foreach (MapLayer layer in MapLayers)
            {
                if (layer.DataSide == EnumMapAppSide.Client) continue;

                layer.OnViewChangedServer(fromPlayer, networkMessage.NowVisible, empty);
            }
        }

        public void SendMapDataToClient(MapLayer forMapLayer, IServerPlayer forPlayer, byte[] data)
        {
            if (api.Side == EnumAppSide.Client) return;
            if (forPlayer.ConnectionState != EnumClientState.Playing) return;

            List<MapLayerData> maplayerdatas = new List<MapLayerData>();

            maplayerdatas.Add(new MapLayerData()
            {
                Data = data,
                ForMapLayer = MapLayerRegistry.FirstOrDefault(x => x.Value == forMapLayer.GetType()).Key
            });

            serverChannel.SendPacket(new MapLayerUpdate() { Maplayers = maplayerdatas.ToArray() }, forPlayer);
        }

        #endregion

    }
}
