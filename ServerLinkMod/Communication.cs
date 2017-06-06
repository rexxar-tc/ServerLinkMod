using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Timers;
using Sandbox.ModAPI;
using VRage.Game;

namespace ServerLinkMod
{
    public static class Communication
    {
        public enum MessageType : byte
        {
            ServerGridPart,
            ClientGridPart,
            ServerChat,
            ClientChat,
            Redirect,
            Notificaion,
            MatchTimes,
            ShipDelete,
        }

        public const ushort NETWORK_ID = 7815;
        private static readonly Dictionary<ulong, SegmentedReceiver> _recievers = new Dictionary<ulong, SegmentedReceiver>();

        public static void RegisterHandlers()
        {
            MyAPIGateway.Multiplayer.RegisterMessageHandler(NETWORK_ID, MessageHandler);
            Logging.Instance.WriteLine("Register handlers");
        }

        public static void UnregisterHandlers()
        {
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(NETWORK_ID, MessageHandler);
        }

        private static void MessageHandler(byte[] bytes)
        {
            try
            {
                var type = (MessageType)bytes[0];

                Logging.Instance.WriteLine($"Recieved message: {bytes[0]}: {type}");

                var data = new byte[bytes.Length - 1];
                Array.Copy(bytes, 1, data, 0, data.Length);

                switch (type)
                {
                    case MessageType.ServerGridPart:
                        ReceiveServerGridPart(data);
                        break;
                    case MessageType.ClientGridPart:
                        ReceiveClientGridPart(data);
                        break;
                    case MessageType.ServerChat:
                        OnServerChat(data);
                        break;
                    case MessageType.ClientChat:
                        OnClientChat(data);
                        break;
                    case MessageType.Redirect:
                        OnRedirect(data);
                        break;
                    case MessageType.Notificaion:
                        OnNotificaion(data);
                        break;
                    case MessageType.MatchTimes:
                        OnMatchTimes(data);
                        break;
                    case MessageType.ShipDelete:
                        OnShipDelete(data);
                        break;
                    default:
                        return;
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine($"Error during message handle! {ex}");
            }
        }

        [Serializable]
        public struct Notification
        {
            public int TimeoutMs;
            public string Message;
            public string Font;
        }

        #region Recieve

        private static void ReceiveClientGridPart(byte[] data)
        {
            ulong steamId = BitConverter.ToUInt64(data, 0);

            SegmentedReceiver receiver;
            if (!_recievers.TryGetValue(steamId, out receiver))
            {
                receiver = new SegmentedReceiver(steamId);
                _recievers.Add(steamId, receiver);
            }

            byte[] message = receiver.Desegment(data);
            if (message == null)
                return; //don't have all the parts yet

            BinaryWriter writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage("Ship.bin", typeof(LinkModCore));
            writer.Write(message.Length);
            writer.Write(message);
            writer.Flush();
            writer.Close();
        }

        private static void ReceiveServerGridPart(byte[] data)
        {
            ulong steamId = BitConverter.ToUInt64(data, 0);

            SegmentedReceiver receiver;
            if (!_recievers.TryGetValue(steamId, out receiver))
            {
                receiver = new SegmentedReceiver(steamId);
                _recievers.Add(steamId, receiver);
            }

            byte[] message = receiver.Desegment(data);
            if (message == null)
                return; //don't have all the parts yet

            ClientData clientData;
            Utilities.VerifyResult res = Utilities.DeserializeAndVerify(message, out clientData, Settings.Instance.IsHub);

            switch (res)
            {
                case Utilities.VerifyResult.Timeout:
                    if (Settings.Instance.IsHub)
                        goto case Utilities.VerifyResult.Ok;
                    goto case Utilities.VerifyResult.ContentModified;
                case Utilities.VerifyResult.Ok:
                    long id = MyAPIGateway.Players.TryGetIdentityId(steamId);

                    Utilities.RecreateFaction(clientData.Faction, id);
                    Utilities.FindPositionAndSpawn(clientData.Grids, id, clientData.ControlledBlock, grids => LinkModCore.Instance.PlayerGrids[steamId] = grids[0]);
                    LinkModCore.Instance.TryStartLobby();
                    SendMatchTimes(steamId);
                    if (Settings.Instance.IsHub)
                        SendShipDelete(steamId);
                    break;
                case Utilities.VerifyResult.Error:
                    MyAPIGateway.Utilities.ShowMessage("Server", "Error loading a grid. Notify an admin!");
                    MyAPIGateway.Utilities.SendMessage("Error loading a grid. Notify an admin!");
                    Logging.Instance.WriteLine($"User {steamId} failed. Validation response: {res}. Client data to follow:");
                    Logging.Instance.WriteLine(Encoding.UTF8.GetString(message));
                    break;
                case Utilities.VerifyResult.ContentModified:
                    MyAPIGateway.Utilities.SendMessage("A user was detected cheating! Event was recorded and the user will be remnoved from the game.");
                    SendShipDelete(steamId);
                    RedirectClient(steamId, Utilities.ZERO_IP);
                    Logging.Instance.WriteLine($"User {steamId} was detected cheating. Validation response: {res}. Client data to follow:");
                    Logging.Instance.WriteLine(clientData == null ? "NULL" : MyAPIGateway.Utilities.SerializeToXML(clientData));
                    break;
                case Utilities.VerifyResult.WrongIP:
                    SendShipDelete(steamId);
                    break;
                default:
                    return;
            }
        }

        private static void OnRedirect(byte[] data)
        {
            string ip = Encoding.ASCII.GetString(data);
            MyAPIGateway.Utilities.ShowNotification("You will be sent to a new server in 10 seconds", 10000, MyFontEnum.Blue);
            var timer = new Timer();
            timer.Interval = 10000;
            timer.Elapsed += (a, b) => Utilities.JoinServer(ip);
            timer.AutoReset = false;
            timer.Start();
        }

        private static void OnClientChat(byte[] data)
        {
            ulong id = BitConverter.ToUInt64(data, 0);
            string message = Encoding.UTF8.GetString(data, sizeof(ulong), data.Length - sizeof(ulong));
            LinkModCore.Instance.HandleChatCommand(id, message);
        }

        private static void OnServerChat(byte[] data)
        {
            MyAPIGateway.Utilities.ShowMessage("Server", Encoding.UTF8.GetString(data));
        }

        private static void OnNotificaion(byte[] data)
        {
            var notificaion = MyAPIGateway.Utilities.SerializeFromXML<Notification>(Encoding.UTF8.GetString(data));

            MyAPIGateway.Utilities.ShowNotification(notificaion.Message, notificaion.TimeoutMs, notificaion.Font);
        }

        private static void OnMatchTimes(byte[] data)
        {
            long lobbyTime = BitConverter.ToInt64(data, 0);
            long matchTime = BitConverter.ToInt64(data, sizeof(long));

            LinkModCore.Instance.LobbyTime = new DateTime(lobbyTime);
            LinkModCore.Instance.MatchTime = new DateTime(matchTime);
        }

        private static void OnShipDelete(byte[] data)
        {
            MyAPIGateway.Utilities.DeleteFileInLocalStorage("Ship.bin", typeof(LinkModCore));
        }

        #endregion

        #region Send

        public static void SegmentAndSend(MessageType type, byte[] payload, ulong from, ulong to = 0)
        {
            SegmentedReceiver receiver;
            if (!_recievers.TryGetValue(from, out receiver))
            {
                receiver = new SegmentedReceiver(from);
                _recievers.Add(from, receiver);
            }

            List<byte[]> packets = receiver.Segment(payload);
            foreach (byte[] packet in packets)
            {
                if (to == 0)
                    SendToServer(type, packet);
                else
                    SendToClient(type, packet, to);
            }
        }

        public static void RedirectClient(ulong steamId, string ip)
        {
            Logging.Instance.WriteLine($"Sending client {steamId} to {ip}");
            SendToClient(MessageType.Redirect, Encoding.ASCII.GetBytes(ip), steamId);
        }

        public static void SendClientChat(string message)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            byte[] idBytes = BitConverter.GetBytes(MyAPIGateway.Session.Player.SteamUserId);
            var data = new byte[messageBytes.Length + idBytes.Length];
            idBytes.CopyTo(data, 0);
            messageBytes.CopyTo(data, idBytes.Length);

            SendToServer(MessageType.ClientChat, data);
        }

        public static void SendServerChat(ulong steamID, string message)
        {
            if (steamID != 0)
                SendToClient(MessageType.ServerChat, Encoding.UTF8.GetBytes(message), steamID);
            else
                BroadcastToClients(MessageType.ServerChat, Encoding.UTF8.GetBytes(message));
        }

        public static void SendNotification(ulong steamId, string message, string font = MyFontEnum.White, int timeoutMs = 2000)
        {
            var notification = new Notification
                               {
                                   Message = message,
                                   Font = font,
                                   TimeoutMs = timeoutMs
                               };

            byte[] data = Encoding.UTF8.GetBytes(MyAPIGateway.Utilities.SerializeToXML(notification));

            if (steamId != 0)
                SendToClient(MessageType.Notificaion, data, steamId);
            else
                BroadcastToClients(MessageType.Notificaion, data);
        }

        public static void SendMatchTimes(ulong steamId)
        {
            var data = new byte[sizeof(long) * 2];
            DateTime lobbyTime = LinkModCore.Instance.MatchStart + TimeSpan.FromMinutes(Settings.Instance.CurrentData.JoinTime);
            DateTime matchTime = lobbyTime + TimeSpan.FromMinutes(Settings.Instance.CurrentData.BattleTime);
            BitConverter.GetBytes(lobbyTime.Ticks).CopyTo(data, 0);
            BitConverter.GetBytes(matchTime.Ticks).CopyTo(data, sizeof(long));

            SendToClient(MessageType.MatchTimes, data, steamId);
        }

        public static void SendShipDelete(ulong steamId)
        {
            SendToClient(MessageType.ShipDelete, new byte[0], steamId );
        }

        #endregion

        #region Helpers

        public static void BroadcastToClients(MessageType type, byte[] data)
        {
            var newData = new byte[data.Length + 1];
            newData[0] = (byte)type;
            data.CopyTo(newData, 1);
            Logging.Instance.WriteLine($"Sending message to others: {type}");
            MyAPIGateway.Utilities.InvokeOnGameThread(() => { MyAPIGateway.Multiplayer.SendMessageToOthers(NETWORK_ID, newData); });
        }

        public static void SendToClient(MessageType type, byte[] data, ulong recipient)
        {
            var newData = new byte[data.Length + 1];
            newData[0] = (byte)type;
            data.CopyTo(newData, 1);
            Logging.Instance.WriteLine($"Sending message to {recipient}: {type}");
            MyAPIGateway.Utilities.InvokeOnGameThread(() => { MyAPIGateway.Multiplayer.SendMessageTo(NETWORK_ID, newData, recipient); });
        }

        public static void SendToServer(MessageType type, byte[] data)
        {
            var newData = new byte[data.Length + 1];
            newData[0] = (byte)type;
            data.CopyTo(newData, 1);
            Logging.Instance.WriteLine($"Sending message to server: {type}");
            MyAPIGateway.Utilities.InvokeOnGameThread(() => { MyAPIGateway.Multiplayer.SendMessageToServer(NETWORK_ID, newData); });
        }

        #endregion
    }
}
