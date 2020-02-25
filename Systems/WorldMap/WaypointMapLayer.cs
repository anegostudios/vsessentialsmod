using Cairo;
using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    public class Waypoint
    {
        public Vec3d Position;
        public string Title;
        public string Text;
        public int Color;

        public string Icon = "circle";
        public bool ShowInWorld;

        public string OwningPlayerUid = null;
        public int OwningPlayerGroupId = -1;
    }


    

    public class WaypointMapLayer : MarkerMapLayer
    {
        // Server side
        public List<Waypoint> Waypoints = new List<Waypoint>();
        ICoreServerAPI sapi;

        // Client side
        public List<Waypoint> ownWaypoints = new List<Waypoint>();
        List<MapComponent> wayPointComponents = new List<MapComponent>();

        public Dictionary<string, LoadedTexture> texturesByIcon;

        public override bool RequireChunkLoaded => false;
        
        
        public WaypointMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
        {
            if (api.Side == EnumAppSide.Server)
            {
                ICoreServerAPI sapi = api as ICoreServerAPI;
                this.sapi = sapi;

                sapi.Event.GameWorldSave += OnSaveGameGettingSaved;
                sapi.RegisterCommand("waypoint", "Put a waypoint at this location which will be visible for you on the map", "[add|addat|remove|list]", OnCmdWayPoint, Privilege.chat);
            }
        }

        private void OnCmdWayPoint(IServerPlayer player, int groupId, CmdArgs args)
        {
            string cmd = args.PopWord();

            switch (cmd)
            {
                case "add":
                    AddWp(player.Entity.ServerPos.XYZ, args, player, groupId, "circle"); 
                    break;
                case "addat":
                    {
                        Vec3d spawnpos = sapi.World.DefaultSpawnPosition.XYZ;
                        spawnpos.Y = 0;
                        Vec3d targetpos = args.PopFlexiblePos(player.Entity.Pos.XYZ, spawnpos);

                        AddWp(targetpos, args, player, groupId, "circle");
                        break;
                    }

                case "addati":
                    {
                        string icon = args.PopWord();

                        Vec3d spawnpos = sapi.World.DefaultSpawnPosition.XYZ;
                        spawnpos.Y = 0;
                        Vec3d targetpos = args.PopFlexiblePos(player.Entity.Pos.XYZ, spawnpos);

                        AddWp(targetpos, args, player, groupId, icon);
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
                        player.SendMessage(groupId, Lang.Get("Your waypoints:\n" + wps.ToString()), EnumChatType.CommandSuccess);
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
            if (icon != null)
            {
                ownwpaypoints[(int)wpIndex].Icon = icon;
            }

            player.SendMessage(groupId, Lang.Get("Ok, waypoint nr. {0} modified", (int)wpIndex), EnumChatType.CommandSuccess);
            ResendWaypoints(player);
        }

        private void AddWp(Vec3d pos, CmdArgs args, IServerPlayer player, int groupId, string icon)
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

            

            Waypoint waypoint = new Waypoint()
            {
                Color = parsedColor.ToArgb() | (255 << 24),
                OwningPlayerUid = player.PlayerUID,
                Position = pos,
                Title = title,
                Icon = icon
            };


            Waypoints.Add(waypoint);

            Waypoint[] ownwpaypoints = Waypoints.Where((p) => p.OwningPlayerUid == player.PlayerUID).ToArray();

            player.SendMessage(groupId, Lang.Get("Ok, waypoint nr. {0} added", ownwpaypoints.Length - 1), EnumChatType.CommandSuccess);
            ResendWaypoints(player);
        }

        private void OnSaveGameGettingSaved()
        {
            sapi.WorldManager.SaveGame.StoreData("playerMapMarkers", JsonUtil.ToBytes(Waypoints));
        }
        

        public override void OnViewChangedServer(IServerPlayer fromPlayer, List<Vec2i> nowVisible, List<Vec2i> nowHidden)
        {
            ResendWaypoints(fromPlayer);
        }

        
        public override void OnMapOpenedClient()
        {
            bool rebuildAnyway = false;
#if DEBUG
            rebuildAnyway = true;
#endif

            if (texturesByIcon == null || rebuildAnyway)
            {
                texturesByIcon = new Dictionary<string, LoadedTexture>();

                ImageSurface surface = new ImageSurface(Format.Argb32, 25, 25);
                Context ctx = new Context(surface);

                string[] icons = new string[] { "circle", "bee", "cave", "home", "ladder", "pick", "rocks", "ruins", "spiral", "star1", "star2", "trader", "vessel" };
                ICoreClientAPI capi = api as ICoreClientAPI;

                foreach (var val in icons)
                {
                    ctx.Operator = Operator.Clear;
                    ctx.SetSourceRGBA(0, 0, 0, 0);
                    ctx.Paint();
                    ctx.Operator = Operator.Over;

                    capi.Gui.Icons.DrawIcon(ctx, "wp" + val.UcFirst(), 1, 1, 23, 23, ColorUtil.WhiteArgbDouble);

                    //surface.WriteToPng("icon-" + val+".png");

                    texturesByIcon[val] = new LoadedTexture(api as ICoreClientAPI, (api as ICoreClientAPI).Gui.LoadCairoTexture(surface, false), 20, 20);
                }

                ctx.Dispose();
                surface.Dispose();
            }
            

            RebuildMapComponents();
        }


        public override void OnMapClosedClient()
        {
            if (texturesByIcon != null)
            {
                foreach (var val in texturesByIcon)
                {
                    val.Value.Dispose();
                }
            }
            texturesByIcon = null;
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

            base.Dispose();
        }

        public override void OnLoaded()
        {
            if (sapi != null)
            {
                try
                {
                    byte[] data = sapi.WorldManager.SaveGame.GetData("playerMapMarkers");
                    if (data != null) Waypoints = JsonUtil.FromBytes<List<Waypoint>>(data);
                } catch (Exception e)
                {
                    sapi.World.Logger.Error("Failed deserializing player map markers. Won't load them, sorry! Exception thrown: ", e);
                }
                
            }
            
        }

        public override void OnDataFromServer(byte[] data)
        {
            ownWaypoints.Clear();
            ownWaypoints.AddRange(SerializerUtil.Deserialize<List<Waypoint>>(data));
            RebuildMapComponents();
        }




        private void RebuildMapComponents()
        {
            if (!mapSink.IsOpened) return;

            foreach (WaypointMapComponent comp in wayPointComponents)
            {
                mapSink.RemoveMapData(comp);
                comp.Dispose();
            }

            wayPointComponents.Clear();

            for (int i = 0; i < ownWaypoints.Count; i++)
            {
                WaypointMapComponent comp = new WaypointMapComponent(i, ownWaypoints[i], this, api as ICoreClientAPI);  

                wayPointComponents.Add(comp);
                mapSink.AddMapData(comp);
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
