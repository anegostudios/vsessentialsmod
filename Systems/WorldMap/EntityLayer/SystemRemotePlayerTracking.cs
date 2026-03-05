using ProtoBuf;
using System.Collections.Generic;
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
        private readonly Queue<IServerPlayer> playerQueue = [];
        private readonly Dictionary<string, PacketPlayerPosition> trackedPlayerPackets = new();

        /// <summary>
        /// Gets or creates a tracked player position for a uid on the client.
        /// </summary>
        public PacketPlayerPosition? GetPlayerPositionInformation(string uid)
        {
            return trackedPlayerPackets.TryGetValue(uid, out PacketPlayerPosition? position) ? position : null;
        }

        public override void StartPre(ICoreAPI api)
        {
            channel = api.Network.RegisterChannel("rpt");
            channel.RegisterMessageType<PacketPlayerPosition>();

            if (api is ICoreServerAPI sapi && !api.World.Config.GetBool("mapHideOtherPlayers", false))
            {
                sapi.Event.PlayerJoin += ServerEvent_PlayerJoin;
                sapi.Event.PlayerLeave += ServerEvent_PlayerLeave;

                sapi.Event.RegisterGameTickListener(OnTick, 100);
            }

            if (api is ICoreClientAPI capi)
            {
                ((IClientNetworkChannel)channel).SetMessageHandler<PacketPlayerPosition>(p =>
                {
                    if (p.PlayerUid == "") return;

                    if (p.Despawn) // Player has left!
                    {
                        trackedPlayerPackets.Remove(p.PlayerUid);
                    }
                    else
                    {
                        trackedPlayerPackets[p.PlayerUid] = p;
                    }
                });
            }
        }

        private void OnTick(float dt)
        {
            if (playerQueue.Count == 0) return;

            IServerPlayer player = playerQueue.Dequeue();

            if (player.Entity != null && player.ConnectionState == EnumClientState.Playing)
            {
                PacketPlayerPosition packet = new()
                {
                    PlayerUid = player.PlayerUID,
                    PosX = player.Entity.Pos.X,
                    PosZ = player.Entity.Pos.Z,
                    Yaw = player.Entity.ServerPos.Yaw
                };

                // Hide spectators.
                if (player.WorldData.CurrentGameMode == EnumGameMode.Spectator)
                {
                    packet.PosX = 0;
                    packet.PosZ = 0;
                }

                ((IServerNetworkChannel)channel).BroadcastPacket(packet);
            }

            playerQueue.Enqueue(player);
        }

        private void ServerEvent_PlayerJoin(IServerPlayer byPlayer)
        {
            playerQueue.Enqueue(byPlayer);
        }

        private void ServerEvent_PlayerLeave(IServerPlayer byPlayer)
        {
            for (int i = playerQueue.Count; i-- > 0; i++)
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
