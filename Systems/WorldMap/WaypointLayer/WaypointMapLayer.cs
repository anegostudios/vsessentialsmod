using Cairo;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    [ProtoContract]
    public class Waypoint
    {
        [ProtoMember(6)]
        public Vec3d Position = new Vec3d();
        [ProtoMember(10)]
        public string Title;
        [ProtoMember(9)]
        public string Text;
        [ProtoMember(1)]
        public int Color;
        [ProtoMember(2)]
        public string Icon = "circle";
        [ProtoMember(7)]
        public bool ShowInWorld;
        [ProtoMember(5)]
        public bool Pinned;

        [ProtoMember(4)]
        public string OwningPlayerUid = null;
        [ProtoMember(3)]
        public int OwningPlayerGroupId = -1;

        [ProtoMember(8)]
        public bool Temporary;

        [ProtoMember(11)]
        public string Guid { get; set; }

    }

    public delegate LoadedTexture CreateIconTextureDelegate();


    public class WaypointMapLayer : MarkerMapLayer
    {
        // Server side
        public List<Waypoint> Waypoints = new List<Waypoint>();
        ICoreServerAPI sapi;

        // Client side
        public List<Waypoint> ownWaypoints = new List<Waypoint>();
        List<MapComponent> wayPointComponents = new List<MapComponent>();
        public MeshRef quadModel;

        List<MapComponent> tmpWayPointComponents = new List<MapComponent>();

        public Dictionary<string, LoadedTexture> texturesByIcon;

        public override bool RequireChunkLoaded => false;


        /// <summary>
        /// List 
        /// </summary>
        public OrderedDictionary<string, CreateIconTextureDelegate> WaypointIcons { get; set; } = new OrderedDictionary<string, CreateIconTextureDelegate>();

        static string[] hexcolors = new string[] { 
            "#F9D0DC", "#F179AF", "#F15A4A", "#ED272A", "#A30A35", "#FFDE98", "#EFFD5F", "#F6EA5E", "#FDBB3A", "#C8772E", "#F47832", 
            "C3D941", "#9FAB3A", "#94C948", "#47B749", "#366E4F", "#516D66", "93D7E3", "#7698CF", "#20909E", "#14A4DD", "#204EA2",
            "#28417A", "#C395C4", "#92479B", "#8E007E", "#5E3896", "D9D4CE", "#AFAAA8", "#706D64", "#4F4C2B", "#BF9C86", "#9885530", "#5D3D21", "#FFFFFF", "#080504" 
        };

        public List<int> WaypointColors { get; set; } = new List<int>()
        {

        };

        public WaypointMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
        {
            WaypointColors = new List<int>();
            for (int i = 0; i < hexcolors.Length; i++)
            {
                WaypointColors.Add(ColorUtil.Hex2Int(hexcolors[i]));
            }

            var icons = api.Assets.GetMany("textures/icons/worldmap/", null, false);
            var capi = api as ICoreClientAPI;
            foreach (var icon in icons)
            {
                string name = icon.Name.Substring(0, icon.Name.IndexOf("."));

                name = Regex.Replace(name, @"\d+\-", "");

                if (api.Side == EnumAppSide.Server)
                {
                    WaypointIcons[name] = () => null;
                }
                else
                {
                    WaypointIcons[name] = () =>
                    {
                        var size = (int)Math.Ceiling(20 * RuntimeEnv.GUIScale);
                        return capi.Gui.LoadSvg(icon.Location, size, size, size, size, ColorUtil.WhiteArgb);
                    };

                    capi.Gui.Icons.CustomIcons["wp" + name.UcFirst()] = (ctx, x, y, w, h, rgba) =>
                    {
                        var col = ColorUtil.ColorFromRgba(rgba);

                        capi.Gui.DrawSvg(icon, ctx.GetTarget() as ImageSurface, ctx.Matrix, x, y, (int)w, (int)h, col);
                    };
                }
            }

            if (api.Side == EnumAppSide.Server)
            {
                ICoreServerAPI sapi = api as ICoreServerAPI;
                this.sapi = sapi;

                sapi.Event.GameWorldSave += OnSaveGameGettingSaved;
                sapi.Event.PlayerDeath += Event_PlayerDeath;
                sapi.RegisterCommand("waypoint", "Put a waypoint at this location which will be visible for you on the map", "[add|addat|modify|remove|list|deathwp]", OnCmdWayPoint, Privilege.chat);
                sapi.RegisterCommand("tpwp", "Teleport yourself to a waypoint starting with the supplied name", "[name]", OnCmdTpTo, Privilege.tp);
            } else
            {
                quadModel = (api as ICoreClientAPI).Render.UploadMesh(QuadMeshUtil.GetQuad());
            }
        }

        private void Event_PlayerDeath(IServerPlayer byPlayer, DamageSource damageSource)
        {
            if (!api.World.Config.GetBool("allowMap", true) || !api.World.Config.GetBool("allowDeathwaypointing", true) || !byPlayer.GetModData<bool>("deathWaypointing", true)) return;

            string title = Lang.Get("You died here");
            for (int i = 0; i < Waypoints.Count; i++)
            {
                var wp = Waypoints[i];
                if (wp.OwningPlayerUid == byPlayer.PlayerUID && wp.Title == title)
                {
                    Waypoints.RemoveAt(i);
                    i--;
                }
            }

            Waypoint waypoint = new Waypoint()
            {
                Color = ColorUtil.ColorFromRgba(200,200,200,255),
                OwningPlayerUid = byPlayer.PlayerUID,
                Position = byPlayer.Entity.Pos.XYZ,
                Title = title,
                Icon = "gravestone",
                Pinned = true
            };

            AddWaypoint(waypoint, byPlayer);
        }

        private void OnCmdTpTo(IServerPlayer player, int groupId, CmdArgs args)
        {
            Waypoint[] ownwpaypoints = Waypoints.Where((p) => p.OwningPlayerUid == player.PlayerUID).ToArray();

            if (args.Length == 0) return;

            string name = args.PopWord().ToLowerInvariant();

            foreach (var wp in ownwpaypoints)
            {
                if (wp.Title != null && wp.Title.ToLowerInvariant().StartsWith(name))
                {
                    player.Entity.TeleportTo(wp.Position);
                    player.SendMessage(groupId, Lang.Get("Ok teleported you to waypoint {0}.", wp.Title), EnumChatType.CommandSuccess);
                    return;
                }
            }
        }

        private void OnCmdWayPoint(IServerPlayer player, int groupId, CmdArgs args)
        {
            string cmd = args.PopWord();

            if (!api.World.Config.GetBool("allowMap", true))
            {
                player.SendMessage(groupId, Lang.Get("Maps are disabled on this server"), EnumChatType.CommandError);
                return;
            }

            switch (cmd)
            {
                case "deathwp":
                    if (!api.World.Config.GetBool("allowDeathwaypointing", true))
                    {
                        player.SendMessage(groupId, Lang.Get("Death waypointing is disabled on this server"), EnumChatType.CommandError);
                        return;
                    }

                    if (args.Length == 0)
                    {
                        bool on = player.GetModData<bool>("deathWaypointing");
                        player.SendMessage(groupId, Lang.Get("Death waypoint is {0}", on ? Lang.Get("on") : Lang.Get("off")), EnumChatType.CommandError);
                    }
                    else
                    {
                        bool on = (bool)args.PopBool(false);
                        player.SetModData<bool>("deathWaypointing", on);
                        player.SendMessage(groupId, Lang.Get("Death waypoint now {0}", on ? Lang.Get("on") : Lang.Get("off")), EnumChatType.CommandError);
                    }
                    break;

                case "add":
                    AddWp(player.Entity.ServerPos.XYZ, args, player, groupId, "circle", false); 
                    break;

                case "addp":
                    AddWp(player.Entity.ServerPos.XYZ, args, player, groupId, "circle", true);
                    break;

                case "addat":
                    {
                        Vec3d spawnpos = sapi.World.DefaultSpawnPosition.XYZ;
                        spawnpos.Y = 0;
                        Vec3d targetpos = args.PopFlexiblePos(player.Entity.Pos.XYZ, spawnpos);

                        if (targetpos == null)
                        {
                            player.SendMessage(groupId, Lang.Get("Syntax: /waypoint addat x y z pinned color title"), EnumChatType.CommandError);
                            return;
                        }

                        AddWp(targetpos, args, player, groupId, "circle", (bool)args.PopBool(false));
                        break;
                    }

                case "addati":
                    {
                        if (args.Length == 0)
                        {
                            player.SendMessage(groupId, Lang.Get("Syntax: /waypoint addati icon x y z pinned color title"), EnumChatType.CommandError);
                            return;
                        }

                        string icon = args.PopWord();

                        Vec3d spawnpos = sapi.World.DefaultSpawnPosition.XYZ;
                        spawnpos.Y = 0;
                        Vec3d targetpos = args.PopFlexiblePos(player.Entity.Pos.XYZ, spawnpos);

                        if (targetpos == null)
                        {
                            player.SendMessage(groupId, Lang.Get("Invalid position. Syntax: /waypoint addati icon x y z pinned color title"), EnumChatType.CommandError);
                            return;
                        }

                        AddWp(targetpos, args, player, groupId, icon, (bool)args.PopBool(false));
                    }
                    break;

                case "modify":
                    ModWp(args, player, groupId);
                    break;


                case "remove":
                    {
                        int? id = args.PopInt();
                        Waypoint[] ownwpaypoints = Waypoints.Where((p) => p.OwningPlayerUid == player.PlayerUID).ToArray();

                        if (ownwpaypoints.Length == 0)
                        {
                            player.SendMessage(groupId, Lang.Get("You have no waypoints to delete"), EnumChatType.CommandError);
                            return;
                        }

                        if (id == null || id < 0 || id >= ownwpaypoints.Length)
                        {
                            player.SendMessage(groupId, Lang.Get("Invalid waypoint number, valid ones are 0..{0}", ownwpaypoints.Length - 1), EnumChatType.CommandSuccess);
                            return;
                        }

                        Waypoints.Remove(ownwpaypoints[(int)id]);
                        RebuildMapComponents();
                        ResendWaypoints(player);
                        player.SendMessage(groupId, Lang.Get("Ok, deleted waypoint."), EnumChatType.CommandSuccess);
                    }
                    break;


                case "list":

                    StringBuilder wps = new StringBuilder();
                    int i = 0;
                    foreach (Waypoint p in Waypoints.Where((p) => p.OwningPlayerUid == player.PlayerUID).ToArray())
                    {
                        Vec3d pos = p.Position.Clone();
                        pos.X -= api.World.DefaultSpawnPosition.X;
                        pos.Z -= api.World.DefaultSpawnPosition.Z;
                        wps.AppendLine(string.Format("{0}: {1} at {2}", i, p.Title, pos.AsBlockPos));
                        i++;
                    }

                    if (wps.Length == 0)
                    {
                        player.SendMessage(groupId, Lang.Get("You have no waypoints"), EnumChatType.CommandSuccess);
                    } else
                    {
                        player.SendMessage(groupId, Lang.Get("Your waypoints:") + "\n" + wps.ToString(), EnumChatType.CommandSuccess);
                    }
                    
                    break;


                default:
                    player.SendMessage(groupId, Lang.Get("Syntax: /waypoint [add|remove|list]"), EnumChatType.CommandError);
                    break;
            }
        }

        private void ModWp(CmdArgs args, IServerPlayer player, int groupId)
        {
            if (args.Length == 0)
            {
                player.SendMessage(groupId, Lang.Get("command-modwaypoint-syntax"), EnumChatType.CommandError);
                return;
            }

            Waypoint[] ownwpaypoints = Waypoints.Where((p) => p.OwningPlayerUid == player.PlayerUID).ToArray();
            int? wpIndex = args.PopInt();

            if (wpIndex == null || wpIndex < 0 || ownwpaypoints.Length < (int)wpIndex - 1)
            {
                player.SendMessage(groupId, Lang.Get("command-modwaypoint-invalidindex", ownwpaypoints.Length), EnumChatType.CommandError);
                return;
            }

            string colorstring = args.PopWord();
            string icon = args.PopWord();
            bool pinned = (bool)args.PopBool(false);
            string title = args.PopAll();

            System.Drawing.Color parsedColor;

            if (colorstring.StartsWith("#"))
            {
                try
                {
                    int argb = Int32.Parse(colorstring.Replace("#", ""), NumberStyles.HexNumber);
                    parsedColor = System.Drawing.Color.FromArgb(argb);
                }
                catch (FormatException)
                {
                    player.SendMessage(groupId, Lang.Get("command-waypoint-invalidcolor"), EnumChatType.CommandError);
                    return;
                }
            }
            else
            {
                parsedColor = System.Drawing.Color.FromName(colorstring);
            }

            if (title == null || title.Length == 0)
            {
                player.SendMessage(groupId, Lang.Get("command-waypoint-notext"), EnumChatType.CommandError);
                return;
            }

            ownwpaypoints[(int)wpIndex].Color = parsedColor.ToArgb() | (255 << 24);
            ownwpaypoints[(int)wpIndex].Title = title;
            ownwpaypoints[(int)wpIndex].Pinned = pinned;

            if (icon != null)
            {
                ownwpaypoints[(int)wpIndex].Icon = icon;
            }

            player.SendMessage(groupId, Lang.Get("Ok, waypoint nr. {0} modified", (int)wpIndex), EnumChatType.CommandSuccess);
            ResendWaypoints(player);
        }

        private void AddWp(Vec3d pos, CmdArgs args, IServerPlayer player, int groupId, string icon, bool pinned)
        {
            if (args.Length == 0)
            {
                player.SendMessage(groupId, Lang.Get("command-waypoint-syntax"), EnumChatType.CommandError);
                return;
            }

            string colorstring = args.PopWord();
            string title = args.PopAll();

            System.Drawing.Color parsedColor;

            if (colorstring.StartsWith("#"))
            {
                try
                {
                    int argb = int.Parse(colorstring.Replace("#", ""), NumberStyles.HexNumber);
                    parsedColor = System.Drawing.Color.FromArgb(argb);
                }
                catch (FormatException)
                {
                    player.SendMessage(groupId, Lang.Get("command-waypoint-invalidcolor"), EnumChatType.CommandError);
                    return;
                }
            }
            else
            {
                parsedColor = System.Drawing.Color.FromName(colorstring);
            }

            if (title == null || title.Length == 0)
            {
                player.SendMessage(groupId, Lang.Get("command-waypoint-notext"), EnumChatType.CommandError);
                return;
            }

            Waypoint waypoint = new Waypoint()
            {
                Color = parsedColor.ToArgb() | (255 << 24),
                OwningPlayerUid = player.PlayerUID,
                Position = pos,
                Title = title,
                Icon = icon,
                Pinned = pinned,
                Guid = Guid.NewGuid().ToString()
            };

            int nr = AddWaypoint(waypoint, player);
            player.SendMessage(groupId, Lang.Get("Ok, waypoint nr. {0} added", nr), EnumChatType.CommandSuccess);
        }

        public int AddWaypoint(Waypoint waypoint, IServerPlayer player)
        {
            Waypoints.Add(waypoint);

            Waypoint[] ownwpaypoints = Waypoints.Where((p) => p.OwningPlayerUid == player.PlayerUID).ToArray();

            ResendWaypoints(player);

            return ownwpaypoints.Length - 1;
        }

        private void OnSaveGameGettingSaved()
        {
            sapi.WorldManager.SaveGame.StoreData("playerMapMarkers_v2", SerializerUtil.Serialize(Waypoints));
        }
        

        public override void OnViewChangedServer(IServerPlayer fromPlayer, List<Vec2i> nowVisible, List<Vec2i> nowHidden)
        {
            ResendWaypoints(fromPlayer);
        }

        
        public override void OnMapOpenedClient()
        {
            reloadIconTextures();

            ensureIconTexturesLoaded();

            RebuildMapComponents();
        }

        public void reloadIconTextures()
        {
            if (texturesByIcon != null)
            {
                foreach (var val in texturesByIcon)
                {
                    val.Value.Dispose();
                }
            }

            texturesByIcon = null;
            ensureIconTexturesLoaded();
        }

        protected void ensureIconTexturesLoaded()
        {
            if (texturesByIcon != null) return;
            
            texturesByIcon = new Dictionary<string, LoadedTexture>();

            foreach (var val in WaypointIcons)
            {
                texturesByIcon[val.Key] = val.Value();
            }
        }


        public override void OnMapClosedClient()
        {
            foreach (var val in tmpWayPointComponents)
            {
                wayPointComponents.Remove(val);
            }

            tmpWayPointComponents.Clear();
        }

        public override void Dispose()
        {
            if (texturesByIcon != null)
            {
                foreach (var val in texturesByIcon)
                {
                    val.Value.Dispose();
                }
            }
            texturesByIcon = null;
            quadModel?.Dispose();

            base.Dispose();
        }

        public override void OnLoaded()
        {
            if (sapi != null)
            {
                try
                {
                    byte[] data = sapi.WorldManager.SaveGame.GetData("playerMapMarkers_v2");
                    if (data != null)
                    {
                        Waypoints = SerializerUtil.Deserialize<List<Waypoint>>(data);
                        sapi.World.Logger.Notification("Successfully loaded " + Waypoints.Count + " waypoints");
                    }
                    else
                    {
                        data = sapi.WorldManager.SaveGame.GetData("playerMapMarkers");
                        if (data != null) Waypoints = JsonUtil.FromBytes<List<Waypoint>>(data);
                    }

                    for (int i = 0; i < Waypoints.Count; i++)
                    {
                        var wp = Waypoints[i];
                        if (wp.Title == null) wp.Title = wp.Text; // Not sure how this happens. For some reason the title moved into text
                        if (wp == null)
                        {
                            sapi.World.Logger.Error("Waypoint with no position loaded, will remove");
                            Waypoints.RemoveAt(i);
                            i--;
                        }
                    }
                } catch (Exception e)
                {
                    sapi.World.Logger.Error("Failed deserializing player map markers. Won't load them, sorry! Exception thrown: {0}", e);
                }

                foreach (var wp in Waypoints)
                {
                    if (wp.Guid == null) wp.Guid = Guid.NewGuid().ToString();
                }
            }
            
        }

        public override void OnDataFromServer(byte[] data)
        {
            ownWaypoints.Clear();
            ownWaypoints.AddRange(SerializerUtil.Deserialize<List<Waypoint>>(data));
            RebuildMapComponents();
        }


        public void AddTemporaryWaypoint(Waypoint waypoint)
        {
            WaypointMapComponent comp = new WaypointMapComponent(ownWaypoints.Count, waypoint, this, api as ICoreClientAPI);
            wayPointComponents.Add(comp);
            tmpWayPointComponents.Add(comp);
        }


        private void RebuildMapComponents()
        {
            if (!mapSink.IsOpened) return;

            foreach (var val in tmpWayPointComponents)
            {
                wayPointComponents.Remove(val);
            }

            foreach (WaypointMapComponent comp in wayPointComponents)
            {
                comp.Dispose();
            }

            wayPointComponents.Clear();

            for (int i = 0; i < ownWaypoints.Count; i++)
            {
                WaypointMapComponent comp = new WaypointMapComponent(i, ownWaypoints[i], this, api as ICoreClientAPI);  

                wayPointComponents.Add(comp);
            }

            wayPointComponents.AddRange(tmpWayPointComponents);
        }


        public override void Render(GuiElementMap mapElem, float dt)
        {
            foreach (var val in wayPointComponents)
            {
                val.Render(mapElem, dt);
            }
        }

        public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
        {
            foreach (var val in wayPointComponents)
            {
                val.OnMouseMove(args, mapElem, hoverText);
            }
        }

        public override void OnMouseUpClient(MouseEvent args, GuiElementMap mapElem)
        {
            foreach (var val in wayPointComponents)
            {
                val.OnMouseUpOnElement(args, mapElem);
                if (args.Handled) break;
            }
        }


        void ResendWaypoints(IServerPlayer toPlayer)
        {
            Dictionary<int, PlayerGroupMembership> memberOfGroups = toPlayer.ServerData.PlayerGroupMemberships;
            List<Waypoint> hisMarkers = new List<Waypoint>();

            foreach (Waypoint marker in Waypoints)
            {
                if (toPlayer.PlayerUID != marker.OwningPlayerUid && !memberOfGroups.ContainsKey(marker.OwningPlayerGroupId)) continue;
                hisMarkers.Add(marker);
            }

            mapSink.SendMapDataToClient(this, toPlayer, SerializerUtil.Serialize(hisMarkers));
        }



        public override string Title => "Player Set Markers";
        public override EnumMapAppSide DataSide => EnumMapAppSide.Server;
    }
}
