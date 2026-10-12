using System;
using HighCommand.Army;
using HighCommand.Mode;
using HighCommand.Orders;
using HighCommand.UI;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Net
{
    /// <summary>
    /// Client -> server commands (orders, recruiting, item moves) and the server's short answers. The commander's
    /// objects are not owned by any network connection, so UNET [Command]s cannot be used; this is a plain pair of
    /// UNET messages (one per direction) with a command kind and a few integers.
    ///
    /// A host applies commands directly; a client sends them. <see cref="ForceRelay"/> makes the host send them too
    /// (through its own local connection), which is how the transport is tested without a second machine.
    /// </summary>
    internal static class CommandChannel
    {
        // Above the game's own range (MsgType.Highest + 1 .. + 41 = 48..88).
        internal const short MsgCommand = 0x4843;
        internal const short MsgReply = 0x4844;

        /// <summary>Returned by the request functions when the command was sent and the server will answer.</summary>
        internal const string Relayed = "\u0001relayed";

        internal enum Kind : byte
        {
            Order = 1,
            MoveItem = 2,
            MoveEquipment = 3,
            EquipFromPool = 4,
            Redistribute = 5,
            Recruit = 6
        }

        /// <summary>Dev: send commands through the network path even when this machine is the server.</summary>
        internal static bool ForceRelay;

        internal static bool ShouldRelay => ForceRelay || !NetworkServer.active;

        private sealed class CommandMessage : MessageBase
        {
            internal byte kind;
            internal int a, b, c, d;
            internal byte[] blob = new byte[0];

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(kind);
                writer.Write(a);
                writer.Write(b);
                writer.Write(c);
                writer.Write(d);
                writer.WriteBytesAndSize(blob, blob.Length);
            }

            public override void Deserialize(NetworkReader reader)
            {
                kind = reader.ReadByte();
                a = reader.ReadInt32();
                b = reader.ReadInt32();
                c = reader.ReadInt32();
                d = reader.ReadInt32();
                blob = reader.ReadBytesAndSize() ?? new byte[0];
            }
        }

        private sealed class ReplyMessage : MessageBase
        {
            internal string text = "";

            public override void Serialize(NetworkWriter writer) => writer.Write(text ?? "");

            public override void Deserialize(NetworkReader reader) => text = reader.ReadString();
        }

        // ---- registration --------------------------------------------------------------------------------

        internal static void RegisterServer()
        {
            if (!NetworkServer.active) return;
            try
            {
                if (!NetworkServer.handlers.ContainsKey(MsgCommand))
                    NetworkServer.RegisterHandler(MsgCommand, OnServerCommand);
            }
            catch (Exception e) { Plugin.Log.LogWarning("CommandChannel: server registration failed: " + e.Message); }
        }

        internal static void RegisterClient()
        {
            try
            {
                foreach (NetworkClient client in NetworkClient.allClients)
                {
                    if (client == null) continue;
                    if (!client.handlers.ContainsKey(MsgReply)) client.RegisterHandler(MsgReply, OnClientReply);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("CommandChannel: client registration failed: " + e.Message); }
        }

        // ---- client side ---------------------------------------------------------------------------------

        private static NetworkConnection ClientConnection()
        {
            NetworkConnection conn = ClientScene.readyConnection;
            if (conn != null) return conn;
            foreach (NetworkClient client in NetworkClient.allClients)
                if (client != null && client.connection != null) return client.connection;
            return null;
        }

        private static string Send(CommandMessage message)
        {
            NetworkConnection conn = ClientConnection();
            if (conn == null || !conn.isReady) return Loc.Get("HIGHCOMMAND_ERR_NOLINK");
            conn.Send(MsgCommand, message);
            return Relayed;
        }

        internal static string SendOrder(OrderCommand command)
        {
            var writer = new NetworkWriter();
            command.Write(writer);
            return Send(new CommandMessage { kind = (byte)Kind.Order, blob = writer.ToArray() });
        }

        internal static string SendMoveItem(int fromId, int toId, ItemIndex item, int count)
            => Send(new CommandMessage { kind = (byte)Kind.MoveItem, a = fromId, b = toId, c = (int)item, d = count });

        internal static string SendMoveEquipment(int fromId, int fromSlot, int toId)
            => Send(new CommandMessage { kind = (byte)Kind.MoveEquipment, a = fromId, b = toId, c = fromSlot });

        internal static string SendEquipFromPool(int poolIndex, int toId)
            => Send(new CommandMessage { kind = (byte)Kind.EquipFromPool, a = poolIndex, b = toId });

        internal static string SendRedistribute() => Send(new CommandMessage { kind = (byte)Kind.Redistribute });

        internal static string SendRecruit(SurvivorDef def)
            => Send(new CommandMessage { kind = (byte)Kind.Recruit, a = def ? (int)def.survivorIndex : -1 });

        private static void OnClientReply(NetworkMessage netMsg)
        {
            try
            {
                string text = netMsg.ReadMessage<ReplyMessage>().text;
                if (!string.IsNullOrEmpty(text)) Toast.Show(text);
            }
            catch (Exception e) { Plugin.Log.LogWarning("CommandChannel: bad reply: " + e.Message); }
        }

        // ---- server side ---------------------------------------------------------------------------------

        private static NetworkUser UserOf(NetworkConnection conn)
        {
            foreach (NetworkUser user in NetworkUser.readOnlyInstancesList)
                if (user && user.connectionToClient == conn) return user;
            return null;
        }

        private static void Reply(NetworkConnection conn, string text)
        {
            if (conn == null || string.IsNullOrEmpty(text)) return;
            conn.Send(MsgReply, new ReplyMessage { text = text });
        }

        private static Hero FindHero(HighCommandRun run, int id)
        {
            foreach (Hero h in run.Army.Heroes) if (h.Id == id) return h;
            return null;
        }

        private static bool Finite(Vector3 v)
            => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        private static void OnServerCommand(NetworkMessage netMsg)
        {
            try
            {
                CommandMessage msg = netMsg.ReadMessage<CommandMessage>();
                HighCommandRun run = HighCommandRun.Instance;
                if (run == null || !NetworkServer.active) return;

                NetworkUser user = UserOf(netMsg.conn);
                if (user == null)
                {
                    Plugin.Log.LogWarning("CommandChannel: command from a connection that is not a player was ignored.");
                    return;
                }

                string answer = Execute(run, user, msg);
                if (ForceRelay || Settings.VerboseLog.Value)
                    Plugin.Log.LogInfo($"CommandChannel: {(Kind)msg.kind} from {user.userName} -> {(string.IsNullOrEmpty(answer) ? "ok" : answer)}");
                if (!string.IsNullOrEmpty(answer)) Reply(netMsg.conn, answer);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("CommandChannel: command failed: " + e.Message);
            }
        }

        /// <summary>Applies one command on the server. Returns the text to show the sender (success or error), or null.</summary>
        private static string Execute(HighCommandRun run, NetworkUser user, CommandMessage m)
        {
            switch ((Kind)m.kind)
            {
                case Kind.Order:
                {
                    OrderCommand cmd = OrderCommand.Read(new NetworkReader(m.blob));
                    if (cmd == null || !Finite(cmd.Point) || !Finite(cmd.Point2)) return null;
                    OrderService.Execute(cmd);
                    return null;
                }

                case Kind.MoveItem:
                {
                    Hero from = FindHero(run, m.a), to = FindHero(run, m.b);
                    var item = (ItemIndex)m.c;
                    if (from == null || to == null || ItemCatalog.GetItemDef(item) == null || m.d < 1) return Loc.Get("HIGHCOMMAND_ERR_FAILED");
                    string error = ArmyService.ExecMoveItem(from, to, item, Mathf.Min(m.d, 9999));
                    if (error != null) return error;
                    PickupIndex pickup = PickupCatalog.FindPickupIndex(item);
                    PickupDef def = pickup.isValid ? PickupCatalog.GetPickupDef(pickup) : null;
                    string what = (def != null ? Language.GetString(def.nameToken) : "?") + (m.d > 1 ? " x" + m.d : "");
                    return Loc.Format("HIGHCOMMAND_INV_MOVED", from.DisplayName, to.DisplayName, what);
                }

                case Kind.MoveEquipment:
                {
                    Hero from = FindHero(run, m.a), to = FindHero(run, m.b);
                    if (from == null || to == null) return Loc.Get("HIGHCOMMAND_ERR_FAILED");
                    return ArmyService.ExecMoveEquipment(from, Mathf.Clamp(m.c, 0, 7), to);
                }

                case Kind.EquipFromPool:
                {
                    Hero to = FindHero(run, m.b);
                    if (to == null) return Loc.Get("HIGHCOMMAND_ERR_FAILED");
                    return ArmyService.ExecEquipFromPool(m.a, to);
                }

                case Kind.Redistribute:
                {
                    string error = ArmyService.ExecRedistribute();
                    return error ?? Loc.Get("HIGHCOMMAND_INV_AUTO_DONE");
                }

                case Kind.Recruit:
                {
                    SurvivorDef def = SurvivorCatalog.GetSurvivorDef((SurvivorIndex)m.a);
                    if (!def) return Loc.Get("HIGHCOMMAND_ERR_FAILED");
                    string error = ArmyService.ExecRecruit(def, ArmyService.LoadoutFor(def, user));
                    if (error != null) return error;
                    string name = !string.IsNullOrEmpty(def.displayNameToken) ? Language.GetString(def.displayNameToken) : def.cachedName;
                    return Loc.Format("HIGHCOMMAND_RECRUITED", name);
                }

                default:
                    return null;
            }
        }
    }
}
