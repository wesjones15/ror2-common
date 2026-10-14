using System;
using System.Collections.Generic;
using RoR2;
using RoR2.Networking;
using UnityEngine.Networking;

namespace Ror2Common
{
    /// Custom messages over RoR2's UNet, with no R2API: a mod registers a message type with a
    /// server handler and a client handler, and sends byte payloads (ByteWriter) between them.
    /// The UNet weaver doesn't run for our DLLs, so there are no SyncVars or Commands; this is
    /// the plain `NetworkServer.RegisterHandler` / `SendByChannel` route RoR2 itself uses.
    ///
    /// Topology: a client sends to the server (`SendToServer`), the server answers or relays to
    /// everyone else (`SendToAll`, `SendToAllExcept`). A host plays on both ends in one process:
    /// its own `SendToServer` runs the server handler directly (with `from == null`), and the
    /// server's sends reach its own client side through the local connection, so a handler
    /// that relays must ignore what came from its own side.
    ///
    /// Message types should be unique across mods (UNet user types are above 47; RoR2 uses a
    /// few more): pick a block per mod, e.g. 0x4D01.. for Mario, 0x4F01.. for Olimar.
    public static class Mp
    {
        /// A payload from a client (server side: `from` is its connection, null for the host's
        /// own) or from the server (client side: `from` is null).
        public delegate void Handler(NetworkConnection from, ByteReader reader);

        sealed class Message : MessageBase
        {
            public byte[] data = new byte[0];

            public override void Serialize(NetworkWriter writer) => writer.WriteBytesFull(data);

            public override void Deserialize(NetworkReader reader) => data = reader.ReadBytesAndSize() ?? new byte[0];
        }

        struct Entry
        {
            public Handler server, client;
        }

        static readonly Dictionary<short, Entry> entries = new Dictionary<short, Entry>();
        static bool hooked;

        /// Server handlers fire only on the server (host); client handlers on every client,
        /// the host's included. Either may be null. Call once at plugin start.
        public static void Register(short type, Handler onServer, Handler onClient)
        {
            entries[type] = new Entry { server = onServer, client = onClient };
            if (hooked) return;
            hooked = true;
            NetworkManagerSystem.onStartServerGlobal += RegisterServer;
            NetworkManagerSystem.onStartClientGlobal += RegisterClient;
        }

        static void RegisterServer()
        {
            foreach (var kv in entries)
            {
                short type = kv.Key;
                if (kv.Value.server == null) continue;
                NetworkServer.RegisterHandler(type, msg => Dispatch(type, msg, true));
            }
        }

        static void RegisterClient(NetworkClient client)
        {
            foreach (var kv in entries)
            {
                short type = kv.Key;
                if (kv.Value.client == null) continue;
                client.RegisterHandler(type, msg => Dispatch(type, msg, false));
            }
        }

        static void Dispatch(short type, NetworkMessage msg, bool server)
        {
            try
            {
                if (!entries.TryGetValue(type, out var entry)) return;
                var data = msg.ReadMessage<Message>().data;
                var reader = new ByteReader(data);
                if (server) entry.server?.Invoke(msg.conn, reader);
                else entry.client?.Invoke(null, reader);
            }
            catch (Exception e)
            {
                CommonLog.Warn($"Mp message {type:X}: {e.GetType().Name}: {e.Message}");
            }
        }

        /// More than one player is in this game (or this is a client): worth sending state.
        public static bool Networked
        {
            get
            {
                if (!NetworkServer.active) return NetworkClient.active;
                return PlayerCharacterMasterController.instances.Count > 1;
            }
        }

        /// This process is the server (single player, or a host).
        public static bool IsServer => NetworkServer.active;

        static int Channel(bool reliable) =>
            (reliable ? QosChannelIndex.defaultReliable : QosChannelIndex.defaultUnreliable).intVal;

        /// Client to server. On the host it runs the server handler directly.
        public static void SendToServer(short type, byte[] payload, bool reliable = true)
        {
            if (NetworkServer.active)
            {
                if (entries.TryGetValue(type, out var entry) && entry.server != null)
                    entry.server(null, new ByteReader(payload));
                return;
            }
            var client = NetworkClient.allClients.Count > 0 ? NetworkClient.allClients[0] : null;
            if (client == null || !client.isConnected) return;
            client.SendByChannel(type, new Message { data = payload }, Channel(reliable));
        }

        /// Server to every client (the host's own included).
        public static void SendToAll(short type, byte[] payload, bool reliable = true)
        {
            if (!NetworkServer.active) return;
            NetworkServer.SendByChannelToAll(type, new Message { data = payload }, Channel(reliable));
        }

        /// Server to every client but `except` (null excepts none), for relaying one client's
        /// state to the rest.
        public static void SendToAllExcept(short type, byte[] payload, NetworkConnection except, bool reliable = true)
        {
            if (!NetworkServer.active) return;
            var msg = new Message { data = payload };
            int channel = Channel(reliable);
            foreach (var conn in NetworkServer.connections)
            {
                if (conn == null || conn == except || !conn.isReady) continue;
                conn.SendByChannel(type, msg, channel);
            }
        }

        /// Server to one client.
        public static void SendTo(NetworkConnection conn, short type, byte[] payload, bool reliable = true)
        {
            if (conn == null || !conn.isReady) return;
            conn.SendByChannel(type, new Message { data = payload }, Channel(reliable));
        }

        /// The networked object with this id on this process, or null.
        public static UnityEngine.GameObject Find(uint netId)
        {
            if (netId == 0) return null;
            NetworkIdentity identity;
            if (NetworkServer.active)
                return NetworkServer.objects.TryGetValue(new NetworkInstanceId(netId), out identity) && identity ? identity.gameObject : null;
            return ClientScene.objects.TryGetValue(new NetworkInstanceId(netId), out identity) && identity ? identity.gameObject : null;
        }
    }
}
