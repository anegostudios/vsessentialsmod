using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent;

    public class WorldMapManager : ModSystem, IWorldMapManager
    {
        public Dictionary<string, Type> MapLayerRegistry { get; } = new Dictionary<string, Type>();
        public Dictionary<string, double> LayerGroupPositions { get; } = new Dictionary<string, double>();
        public bool IsShuttingDown { get; set; }

        private ICoreAPI _api;

        // Client side stuff
        private ICoreClientAPI _capi;
        private IClientNetworkChannel _clientChannel;
        public GuiDialogWorldMap worldMapDlg { get; set; }
        public bool IsOpened => worldMapDlg?.IsOpened() == true;

        // Client and Server side stuff
        public List<MapLayer> MapLayers { get; set; } = new List<MapLayer>();
        private Thread _mapLayerGenThread;
        private readonly object _mapLayerThreadLock = new();
        private const float _tickInterval = 20 / 1000f;
        
        // Server side stuff
        private IServerNetworkChannel _serverChannel;

        public override bool ShouldLoad(EnumAppSide side)
        {
            return true;
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            RegisterDefaultMapLayers();
            _api = api;
        }

        public void RegisterDefaultMapLayers()
        {
            RegisterMapLayer<ChunkMapLayer>("chunks", 0);
            RegisterMapLayer<PlayerMapLayer>("players", 0.5);
            RegisterMapLayer<EntityMapLayer>("entities", 0.5);
            RegisterMapLayer<WaypointMapLayer>("waypoints", 1);
        }

        public void RegisterMapLayer<T>(string code, double position) where T : MapLayer
        {
            MapLayerRegistry[code] = typeof(T);
            LayerGroupPositions[code] = position;
        }

        private void CreateMapLayerGenerationThread()
        {
            if (_mapLayerGenThread is { IsAlive: true }) return;
            _mapLayerGenThread = new Thread(new ThreadStart(() =>
            {
                while (!IsShuttingDown)
                {
                    lock (_mapLayerThreadLock)
                    {
                        var mapLayersSnapshot = MapLayers.ToList();
                        foreach (var layer in mapLayersSnapshot)
                        {
                            layer.OnOffThreadTick(_tickInterval);
                        }
                    }

                    Thread.Sleep(20);
                }
            }))
            {
                IsBackground = true
            };
            _mapLayerGenThread.Start();
        }

        private void LoadMapLayersFromRegistry()
        {
            lock (_mapLayerThreadLock)
            {
                MapLayers.Clear();

                var mapLayerRegistrySnapshot = MapLayerRegistry.ToDictionary(k => k.Key, v => v.Value);
                foreach (var val in mapLayerRegistrySnapshot)
                {
                    if (val.Key == "entities" && !_api.World.Config.GetAsBool("entityMapLayer")) continue;
                    var instance = (MapLayer)Activator.CreateInstance(val.Value, _api, this);
                    MapLayers.Add(instance);
                }

                var mapLayersSnapshot = MapLayers.ToList();
                foreach (var layer in mapLayersSnapshot)
                {
                    layer.OnLoaded();
                }
            }
        }

        #region Client side

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            _capi = api;
            _capi.Event.LevelFinalize += OnLevelFinaliseClient;
            _capi.Event.RegisterGameTickListener(OnClientTick, 20);
            _capi.Settings.AddWatcher<bool>("showMinimapHud", (on) =>
            {
                ToggleMap(EnumDialogType.HUD);
            });

            _capi.Event.LeaveWorld += OnPlayerLeaveWorld;

            _clientChannel =
                api.Network.RegisterChannel("worldmap")
               .RegisterMessageType(typeof(MapLayerUpdate))
               .RegisterMessageType(typeof(OnViewChangedPacket))
               .RegisterMessageType(typeof(OnMapToggle))
               .SetMessageHandler<MapLayerUpdate>(OnMapLayerDataReceivedClient)
            ;
        }

        private void OnPlayerLeaveWorld()
        {
            IsShuttingDown = true;
            int i = 0;
            while (_mapLayerGenThread != null && _mapLayerGenThread.IsAlive && i < 20)
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
        }

        private void OnWorldMapLinkClicked(LinkTextComponent linkcomp)
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
            var elem = worldMapDlg.SingleComposer.GetElement("mapElem") as GuiElementMap;
            var wml = elem?.mapLayers.FirstOrDefault(ml => ml is WaypointMapLayer) as WaypointMapLayer;
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
                _capi.SendChatMessage(string.Format("/waypoint addati {0} ={1} ={2} ={3} {4} {5} {6}", "circle", x, y, z, false, "steelblue", text));
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

        private void OnMapLayerDataReceivedClient(MapLayerUpdate msg)
        {
            for (int i = 0; i < msg.Maplayers.Length; i++)
            {
                Type type = MapLayerRegistry[msg.Maplayers[i].ForMapLayer];
                MapLayers.FirstOrDefault(x => x.GetType() == type)?.OnDataFromServer(msg.Maplayers[i].Data);
            }
        }


        public bool mapAllowedClient()
        {
            return _capi.World.Config.GetBool("allowMap", true) || _capi.World.Player.Privileges.IndexOf("allowMap") != -1;
        }

        private bool OnHotKeyWorldMapHud(KeyCombination comb)
        {
            ToggleMap(EnumDialogType.HUD);
            return true;
        }

        private bool OnHotKeyMinimapPosition(KeyCombination comb)
        {
            int prev = _capi.Settings.Int["minimapHudPosition"];
            _capi.Settings.Int["minimapHudPosition"] = (prev + 1) % 4;

            if (worldMapDlg == null || !worldMapDlg.IsOpened()) ToggleMap(EnumDialogType.HUD);
            else
            {
                if (worldMapDlg.DialogType == EnumDialogType.HUD)
                {
                    worldMapDlg.Recompose();
                }
            }
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

            if (!mapAllowedClient())
            {
                if (isDlgOpened) worldMapDlg.TryClose();
                return;
            }

            if (worldMapDlg != null)
            {
                if (!isDlgOpened)
                {
                    if (asType == EnumDialogType.HUD) _capi.Settings.Bool.Set("showMinimapHud", true, false);

                    worldMapDlg.Open(asType);
                    foreach (MapLayer layer in MapLayers) layer.OnMapOpenedClient();
                    _clientChannel.SendPacket(new OnMapToggle() { OpenOrClose = true });

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
                        _capi.Settings.Bool.Set("showMinimapHud", false, false);
                    }
                    else if (_capi.Settings.Bool["showMinimapHud"])
                    {
                        worldMapDlg.Open(EnumDialogType.HUD);
                        return;
                    }

                }

                worldMapDlg.TryClose();
                return;
            }

            worldMapDlg = new GuiDialogWorldMap(OnViewChangedClient, SyncViewChange, _capi, GetTabsOrdered());
            worldMapDlg.OnClosed += () =>
            {
                foreach (MapLayer layer in MapLayers) layer.OnMapClosedClient();
                _clientChannel.SendPacket(new OnMapToggle() { OpenOrClose = false });

            };

            worldMapDlg.Open(asType);
            foreach (MapLayer layer in MapLayers) layer.OnMapOpenedClient();
            _clientChannel.SendPacket(new OnMapToggle() { OpenOrClose = true });

            if (asType == EnumDialogType.HUD) _capi.Settings.Bool.Set("showMinimapHud", true, false);   // Don't trigger the watcher which will call Toggle again recursively!
        }

        private List<string> GetTabsOrdered()
        {
            Dictionary<string, double> tabs = new Dictionary<string, double>();

            foreach (MapLayer layer in MapLayers)
            {
                if (!tabs.ContainsKey(layer.LayerGroupCode))
                {
                    if (!LayerGroupPositions.TryGetValue(layer.LayerGroupCode, out double pos)) pos = 1;
                    tabs[layer.LayerGroupCode] = pos;
                }
            }

            return tabs.OrderBy(val => val.Value).Select(val => val.Key).ToList();
        }

        private void OnViewChangedClient(List<FastVec2i> nowVisible, List<FastVec2i> nowHidden)
        {
            foreach (MapLayer layer in MapLayers)
            {
                layer.OnViewChangedClient(nowVisible, nowHidden);
            }
        }

        private void SyncViewChange(int x1, int z1, int x2, int z2)
        {
            _clientChannel.SendPacket(new OnViewChangedPacket() { X1 = x1, Z1 = z1, X2 = x2, Z2 = z2 });
        }

        public void TranslateWorldPosToViewPos(Vec3d worldPos, ref Vec2f viewPos)
        {
            worldMapDlg.TranslateWorldPosToViewPos(worldPos, ref viewPos);
        }

        public void SendMapDataToServer(MapLayer forMapLayer, byte[] data)
        {
            if (_api.Side == EnumAppSide.Server) return;

            List<MapLayerData> maplayerdatas = new List<MapLayerData>();

            maplayerdatas.Add(new MapLayerData()
            {
                Data = data,
                ForMapLayer = MapLayerRegistry.FirstOrDefault(x => x.Value == forMapLayer.GetType()).Key
            });

            _clientChannel.SendPacket(new MapLayerUpdate() { Maplayers = maplayerdatas.ToArray() });
        }

        private void OnLevelFinaliseClient()
        {
            if (_capi is null) return;
            RegisterClientHotkeys();
            LoadMapLayersFromRegistry();
            CreateMapLayerGenerationThread();
            OpenMiniMap();
        }

        private void RegisterClientHotkeys()
        {
            if (_capi is null) return;
            if (!mapAllowedClient()) return;

            _capi.Input.RegisterHotKey("worldmaphud", Lang.Get("Show/Hide Minimap"), GlKeys.F6, HotkeyType.HelpAndOverlays);
            _capi.Input.RegisterHotKey("minimapposition", Lang.Get("keycontrol-minimap-position"), GlKeys.F6, HotkeyType.HelpAndOverlays, false, true, false);
            _capi.Input.RegisterHotKey("worldmapdialog", Lang.Get("Show World Map"), GlKeys.M, HotkeyType.HelpAndOverlays);
            _capi.Input.SetHotKeyHandler("worldmaphud", OnHotKeyWorldMapHud);
            _capi.Input.SetHotKeyHandler("minimapposition", OnHotKeyMinimapPosition);
            _capi.Input.SetHotKeyHandler("worldmapdialog", OnHotKeyWorldMapDlg);

            _capi.RegisterLinkProtocol("worldmap", OnWorldMapLinkClicked);
        }

        private void OpenMiniMap()
        {
            if (!(worldMapDlg is null) && worldMapDlg.IsOpened()) return;
            if (!_capi.Settings.Bool["showMinimapHud"] && _capi.Settings.Bool.Exists("showMinimapHud")) return;
            ToggleMap(EnumDialogType.HUD);
        }

        #endregion

        #region Server Side

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, OnLevelFinaliseServer);
            sapi.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, () => IsShuttingDown = true);

            _serverChannel =
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
            foreach (MapLayer layer in MapLayers)
            {
                if (layer.DataSide == EnumMapAppSide.Client) continue;

                layer.OnViewChangedServer(fromPlayer, networkMessage.X1, networkMessage.Z1, networkMessage.X2, networkMessage.Z2);
            }
        }

        public void SendMapDataToClient(MapLayer forMapLayer, IServerPlayer forPlayer, byte[] data)
        {
            if (_api.Side == EnumAppSide.Client) return;
            if (forPlayer.ConnectionState != EnumClientState.Playing) return;

            MapLayerData[] maplayerdatas = new MapLayerData[1] {
                new MapLayerData()
                {
                    Data = data,
                    ForMapLayer = MapLayerRegistry.FirstOrDefault(x => x.Value == forMapLayer.GetType()).Key
                }
            };

            _serverChannel.SendPacket(new MapLayerUpdate() { Maplayers = maplayerdatas }, forPlayer);
        }

        private void OnLevelFinaliseServer()
        {
            LoadMapLayersFromRegistry();
            CreateMapLayerGenerationThread();
        }

        #endregion
    }
}