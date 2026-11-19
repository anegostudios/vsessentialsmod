using ProtoBuf;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

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

        /// <summary>
        /// Return every currently tracked position on the client.
        /// </summary>
        public IEnumerable<PacketPlayerPosition> GetAllTrackedPlayerPositions()
        {
            return trackedPlayerPackets.Values;
        }

        public override void StartPre(ICoreAPI api)
        {
            channel = api.Network.RegisterChannel("remoteplayertracker");
            channel.RegisterMessageType<PacketPlayerPosition>();

            this.api = api;

            switch (api)
            {
                case ICoreServerAPI sapi when api.World.Config.GetBool("allowMap"):
                    sapi.Event.PlayerJoin += ServerEvent_PlayerJoin;
                    sapi.Event.PlayerDisconnect += ServerEvent_PlayerDisconnect;

                    sapi.Event.RegisterGameTickListener(OnTick, 100);
                    break;
                case ICoreClientAPI:
                    ((IClientNetworkChannel)channel).SetMessageHandler<PacketPlayerPosition>(p =>
                    {
                        if (string.IsNullOrEmpty(p.PlayerUid)) return;

                        if (p.Despawn)
                        {
                            trackedPlayerPackets.Remove(p.PlayerUid); // Player has left or not tracked.
                            return;
                        }

                        p.AssociatedPlayer = api.World.PlayerByUid(p.PlayerUid);
                        trackedPlayerPackets[p.PlayerUid] = p;
                    });
                    break;
            }
        }

        private void OnTick(float dt)
        {
            if (playerQueue.Count == 0) return;

            IServerPlayer player = playerQueue.Dequeue();

            if (player.Entity != null && player.ConnectionState == EnumClientState.Playing)
            {
                sendPlayerPacket(player);
            }

            playerQueue.Enqueue(player);
        }

        private void sendPlayerPacket(IServerPlayer player)
        {
            var playerPos = player.Entity.Pos;
            PacketPlayerPosition packet = new()
            {
                PlayerUid = player.PlayerUID,
                PosX = playerPos.X,
                PosZ = playerPos.Z,
                Yaw = playerPos.Yaw
            };

            PacketPlayerPosition despawnPacket = new()
            {
                PlayerUid = player.PlayerUID,
                Despawn = true
            };

            ((IServerNetworkChannel)channel).SendPacket(packet, player); // Always track the  client player

            // We only show the player to themselves if they're in spectator mode or hide other players is on
            if (api.World.Config.GetBool("mapHideOtherPlayers") || player.WorldData.CurrentGameMode == EnumGameMode.Spectator)
            {
                ((IServerNetworkChannel)channel).BroadcastPacket(despawnPacket, player);
                return;
            }

            double playerRenderDistance = api.World.Config.GetFloat("mapPlayerRenderDistance", 1000);

            if (playerRenderDistance < 0) // We know that all players will see this player
            {
                ((IServerNetworkChannel)channel).BroadcastPacket(packet, player);
                return;
            }

            int[] ourGroups = player.Groups.Select(group => group.GroupUid).ToArray();
            bool shouldCheckGroups = api.World.Config.GetBool("mapShowGroupPlayers", true) && ourGroups.Length > 0;

            foreach (var playerToReceive in api.World.AllOnlinePlayers.Cast<IServerPlayer>())
            {
                if (playerToReceive.PlayerUID == player.PlayerUID) continue;

                // Check whether the player who will receive the packet can track this player
                if (playerToReceive.Entity?.Pos.HorDistanceTo(playerPos) <= playerRenderDistance ||
                    (shouldCheckGroups && playerToReceive.Groups.Any(group => ourGroups.Contains(group.GroupUid))))
                {
                    ((IServerNetworkChannel)channel).SendPacket(packet, playerToReceive);
                    continue;
                }

                ((IServerNetworkChannel)channel).SendPacket(despawnPacket, playerToReceive);
            }
        }

        private void ServerEvent_PlayerJoin(IServerPlayer byPlayer)
        {
            playerQueue.Enqueue(byPlayer);
        }

        private void ServerEvent_PlayerDisconnect(IServerPlayer byPlayer)
        {
            for (int i = playerQueue.Count; i > 0; i--)
            {
                IServerPlayer sp = playerQueue.Dequeue();
                if (sp.PlayerUID == byPlayer.PlayerUID) break;
                playerQueue.Enqueue(sp);
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
