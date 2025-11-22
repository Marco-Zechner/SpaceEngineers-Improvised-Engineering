using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace mz_00956.ImprovisedEngineering
{
    /// <summary>
    /// Simple network communication example.
    /// 
    /// Always send to server as clients can't send to each other directly.
    /// Then decide in the packet if it should be relayed to everyone else (except sender and server of course).
    /// 
    /// Security note:
    ///  SenderId is not reliable and can be altered by sender to claim they're someone else (like an admin).
    ///  If you need senderId to be secure, a more complicated process is required involving sending
    ///   every player a unique random ID and they sending that ID would confirm their identity.
    /// </summary>
    public class NetworkProtobuf
    {
        public readonly ushort ChannelId;

        private List<IMyPlayer> tempPlayers = null;

        /// <summary>
        /// <paramref name="channelId"/> must be unique from all other mods that also use network packets.
        /// </summary>
        public NetworkProtobuf(ushort channelId)
        {
            ChannelId = channelId;
        }

        /// <summary>
        /// Register packet monitoring, not necessary if you don't want the local machine to handle incoming packets.
        /// </summary>
        public void Register()
        {
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(ChannelId, ReceivedPacket);
        }

        /// <summary>
        /// This must be called on world unload if you called <see cref="Register"/>.
        /// </summary>
        public void Unregister()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ChannelId, ReceivedPacket);
        }

        private void ReceivedPacket(ushort handlerId, byte[] rawData, ulong playerSteamID, bool isArrivedFromServer) // executed when a packet is received on this machine
        {
            try
            {
                var packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(rawData);

                HandlePacket(packet, rawData);
            }
            catch (Exception e)
            {
                // Handle packet receive errors however you prefer, this is with logging. Remove try-catch to allow it to crash the game.
                // If another mod uses the same channel as your mod, this will throw errors being unable to deserialize their stuff.
                // In that case, one of you must change the channelId and NOT ignoring the error as it can noticeably impact performance.

                //Yes I know just not showing the Error is a stupid fix, but it will have to do until the next update.
                //As far as I can tell it only happens after the block was merged which is when there should be any messages send anyway so it doesn't really matter that they get lost here.

                Debug.Log(message: $"{e.Message}\n{e.StackTrace}");

                Debug.Notify($"[ERROR: {GetType().FullName}: {e.Message} | Send SpaceEngineers.Log to mod author]", 10000, "Red", true);
            }
        }

        private void HandlePacket(PacketBase packet, byte[] rawData = null)
        {
            var relay = packet.Received();

            if (relay)
                RelayToClients(packet, rawData);
        }

        /// <summary>
        /// Send a packet to the server.
        /// Works from clients and server.
        /// </summary>
        public void SendToServer(PacketBase packet)
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                HandlePacket(packet);
                return;
            }

            var bytes = MyAPIGateway.Utilities.SerializeToBinary(packet);

            MyAPIGateway.Multiplayer.SendMessageToServer(ChannelId, bytes);
        }

        /// <summary>
        /// Send a packet to a specific player.
        /// Only works server side.
        /// </summary>
        public void SendToPlayer(PacketBase packet, ulong steamId)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                return;

            var bytes = MyAPIGateway.Utilities.SerializeToBinary(packet);

            MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, bytes, steamId);
        }

        /// <summary>
        /// Sends packet (or supplied bytes) to all players except server player and supplied packet's sender.
        /// Only works server side.
        /// </summary>
        public void RelayToClients(PacketBase packet, byte[] rawData = null)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                return;

            if (tempPlayers == null)
                tempPlayers = new List<IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers);
            else
                tempPlayers.Clear();

            MyAPIGateway.Players.GetPlayers(tempPlayers);

            foreach (var p in tempPlayers)
            {
                if (p.IsBot)
                    continue;

                if (p.SteamUserId == MyAPIGateway.Multiplayer.ServerId)
                    continue;

                if (p.SteamUserId == packet.SenderId)
                    continue;

                if (rawData == null)
                    rawData = MyAPIGateway.Utilities.SerializeToBinary(packet);

                MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, rawData, p.SteamUserId);
            }

            tempPlayers.Clear();
        }
    }
}