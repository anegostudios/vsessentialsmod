using ProtoBuf;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

#nullable enable

namespace Vintagestory.GameContent
{
    [ProtoContract]
    public class PacketPlayerPosition
    {
        [ProtoMember(1)]
        public string PlayerUid = "";

        [ProtoMember(2)]
        public double PosX;

        [ProtoMember(3)]
        public double PosZ;

        [ProtoMember(4)]
        public double Yaw;

        [ProtoMember(5)]
        public bool Despawn;

        public IPlayer? AssociatedPlayer;

        public void SetFrom(PacketPlayerPosition packet)
        {
            PosX = packet.PosX;
            PosZ = packet.PosZ;
            Yaw = packet.Yaw;
        }
    }

    public class SystemRemotePlayerTracking : ModSystem
    {
        private INetworkChannel channel = null!;
        private ICoreAPI api = null!;

        private readonly Queue<IServerPlayer> playerQueue = [];
        private readonly Dictionary<string, PacketPlayerPosition> trackedPlayerPackets = new();

        /// <summary>
        /// Gets or creates a tracked player position for a UID on the client.
        /// </summary>
        public PacketPlayerPosition? GetPlayerPositionInformation(string uid)
        {
            return trackedPlayerPackets.GetValueOrDefault(uid);
        }

        public IEnumerable<PacketPlayerPosition> GetAllTrackedPlayerPositions()
        {
            return trackedPlayerPackets.Values;
        }

        public override void StartPre(ICoreAPI api)
        {
            channel = api.Network.RegisterChannel("rpt");
            channel.RegisterMessageType<PacketPlayerPosition>();

            this.api = api;

            switch (api)
            {
                case ICoreServerAPI sapi when !api.World.Config.GetBool("mapHideOtherPlayers"):
                    sapi.Event.PlayerJoin += ServerEvent_PlayerJoin;
                    sapi.Event.PlayerLeave += ServerEvent_PlayerLeave;

                    sapi.Event.RegisterGameTickListener(OnTick, 100);
                    break;
                case ICoreClientAPI:
                    ((IClientNetworkChannel)channel).SetMessageHandler<PacketPlayerPosition>(p =>
                    {
                        IPlayer? player = api.World.PlayerByUid(p.PlayerUid);
                        if (player == null) return;

                        if (p.Despawn)
                        {
                            trackedPlayerPackets.Remove(p.PlayerUid); // Player has left or not tracked.
                            return;
                        }

                        p.AssociatedPlayer = player;
                        trackedPlayerPackets[p.PlayerUid] = p;
                    });
                    break;
            }
        }

        private void OnTick(float dt)
        {
            if (playerQueue.Count == 0) return;

            double playerRenderDistance = api.World.Config.GetFloat("mapPlayerRenderDistance", 1000);
            bool showGroupPlayers = api.World.Config.GetBool("mapShowGroupPlayers", true);

            IServerPlayer player = playerQueue.Dequeue();

            if (player.Entity != null && player.ConnectionState == EnumClientState.Playing)
            {
                PacketPlayerPosition packet = new()
                {
                    PlayerUid = player.PlayerUID,
                    PosX = player.Entity.Pos.X,
                    PosZ = player.Entity.Pos.Z,
                    Yaw = player.Entity.Pos.Yaw
                };

                PacketPlayerPosition despawnPacket = new()
                {
                    PlayerUid = player.PlayerUID,
                    Despawn = true
                };

                // Hide spectators.
                bool globalDespawn = player.WorldData.CurrentGameMode == EnumGameMode.Spectator;
                string[] ourGroups = player.GetGroups().Select(group => group.GroupName).ToArray();

                foreach (IPlayer playerToReceive in api.World.AllOnlinePlayers)
                {
                    if (playerToReceive.Entity == null) continue;

                    // Check whether this player should appear for the player who will receive the packet
                    if (globalDespawn || (playerToReceive.Entity.Pos.DistanceTo(player.Entity.Pos) > playerRenderDistance &&
                        (!showGroupPlayers || ourGroups.Length == 0 || !playerToReceive.Groups.Any(group => ourGroups.Contains(group.GroupName)))))
                    {
                        ((IServerNetworkChannel)channel).SendPacket(despawnPacket, (IServerPlayer)playerToReceive);
                        continue;
                    }

                    ((IServerNetworkChannel)channel).SendPacket(packet, (IServerPlayer)playerToReceive);
                }
            }

            playerQueue.Enqueue(player);
        }

        private void ServerEvent_PlayerJoin(IServerPlayer byPlayer)
        {
            playerQueue.Enqueue(byPlayer);
        }

        private void ServerEvent_PlayerLeave(IServerPlayer byPlayer)
        {
            for (int i = playerQueue.Count; i-- > 0;)
            {
                IServerPlayer sp = playerQueue.Dequeue();
                if (sp.PlayerUID != byPlayer.PlayerUID)
                {
                    playerQueue.Enqueue(sp);
                }
            }

            PacketPlayerPosition packet = new()
            {
                PlayerUid = byPlayer.PlayerUID,
                Despawn = true
            };

            ((IServerNetworkChannel)channel).BroadcastPacket(packet);
        }
    }
}
