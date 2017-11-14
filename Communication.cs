using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Timers;
using Sandbox.ModAPI;
using VRage.Game;
using VRageMath;

namespace ServerJump
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
            ClientRequestJump,
            ResetClient,
            SoundMessage
        }

        public const ushort NETWORK_ID = 7815;
        private static readonly Dictionary<ulong, SegmentedReceiver> _recievers = new Dictionary<ulong, SegmentedReceiver>();

        public static void RegisterHandlers()
        {
            MyAPIGateway.Multiplayer.RegisterMessageHandler(NETWORK_ID, MessageHandler);
            ServerJumpClass.Instance.SomeLog("Register handlers");
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

                ServerJumpClass.Instance.SomeLog($"Recieved message: {bytes[0]}: {type}");

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
                    case MessageType.ClientRequestJump:
                        OnClientRequesJump(data);
                        break;
                    case MessageType.Notificaion:
                        OnNotificaion(data);
                        break;
                    case MessageType.ResetClient:
                        OnResetClient(data);
                        break;
                    case MessageType.SoundMessage:
                        OnSoundMessage(data);
                        break;
                    default:
                        return;
                }
            }
            catch (Exception ex)
            {
                ServerJumpClass.Instance.SomeLog($"Error during message handle! {ex}");
            }
        }

        [Serializable]
        public struct Notification
        {
            public int TimeoutMs;
            public string Message;
            public string Font;
        }
        [Serializable]
        public struct JumpInfo
        {
            public ulong steamId;
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

            BinaryWriter writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage("Ship.bin", typeof(ServerJumpClass));
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
            Utilities.VerifyResult res = Utilities.DeserializeAndVerify(message, out clientData); //ignore timestamp none!

            switch (res)
            {
                case Utilities.VerifyResult.Ok:
                    long id = MyAPIGateway.Players.TryGetIdentityId(steamId);
                    
                    Utilities.RecreateFaction(clientData.Faction, id);//create thread for me pls
                    Utilities.FindPositionAndSpawn(clientData.Grid, id, clientData.ControlledBlock); //create thread for me pls
                    SendToClient(MessageType.ResetClient, Encoding.ASCII.GetBytes("pidor"), steamId);
                    //ServerJumpClass.Instance.TryStartLobby();
                    SendMatchTimes(steamId);
                    break;
                case Utilities.VerifyResult.Error:
                    MyAPIGateway.Utilities.ShowMessage("Server", "Error loading a grid. Notify an admin!");
                    MyAPIGateway.Utilities.SendMessage("Error loading a grid. Notify an admin!");
                    ServerJumpClass.Instance.SomeLog($"User {steamId} failed. Validation response: {res}. Client data to follow:");
                    ServerJumpClass.Instance.SomeLog(clientData == null ? "NULL" : MyAPIGateway.Utilities.SerializeToXML(clientData));
                    break;
                case Utilities.VerifyResult.Timeout:
                  //  if (ServerJump.ServerJumpClass.Instance.Settings.Hub)
                   // goto case Utilities.VerifyResult.Ok;
                    goto case Utilities.VerifyResult.ContentModified;
                case Utilities.VerifyResult.ContentModified:
                    MyAPIGateway.Utilities.SendMessage("A user was detected cheating! Event was recorded and the user will be remnoved from the game.");
                    RedirectClient(steamId, Utilities.ZERO_IP);
                    SendToClient(MessageType.ResetClient, Encoding.ASCII.GetBytes("pidor"), steamId);
                    ServerJumpClass.Instance.SomeLog($"User {steamId} was detected cheating. Validation response: {res}. Client data to follow:");
                    ServerJumpClass.Instance.SomeLog(clientData == null ? "NULL" : MyAPIGateway.Utilities.SerializeToXML(clientData));
                    break;
                default:
                    return;
            }
        }

        private static void OnClientChat(byte[] data)
        {
            ulong id = BitConverter.ToUInt64(data, 0);
            string message = Encoding.UTF8.GetString(data, sizeof(ulong), data.Length - sizeof(ulong));
            ServerJumpClass.Instance.HandleChatCommand(id, message);
        }
        private static void OnClientRequesJump(byte[] data)
        {
            var LinkInfo1 = MyAPIGateway.Utilities.SerializeFromXML<JumpInfo>(Encoding.UTF8.GetString(data));
            ServerJumpClass.Instance.SomeLog($" OnClientRequesJump");
            ServerJumpClass.Instance.SomeLog($"OnClientRequesJump SteamID: " + LinkInfo1.steamId.ToString());
            //  MyAPIGateway.Parallel.Start(() => ServerJumpClass.Instance.InitJump(LinkInfo1.steamId));
            ServerJumpClass.Instance.InitJump(LinkInfo1.steamId);
        }
        private static void OnServerChat(byte[] data)
        {
            MyAPIGateway.Utilities.ShowMessage("Server", Encoding.UTF8.GetString(data));
        }
        private static void OnSoundMessage(byte[] data)
        {

        }
        private static void OnResetClient(byte[] data)
        {
           // MyAPIGateway.Utilities.DeleteFileInLocalStorage("Ship.bin", typeof(LinkModCore));
        }
        private static void OnNotificaion(byte[] data)
        {
            var notificaion = MyAPIGateway.Utilities.SerializeFromXML<Notification>(Encoding.UTF8.GetString(data));

            MyAPIGateway.Utilities.ShowNotification(notificaion.Message, notificaion.TimeoutMs, notificaion.Font);
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
            ServerJumpClass.Instance.SomeLog($"Sending client {steamId} to {ip}");
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
            DateTime lobbyTime = ServerJumpClass.Instance.MatchStart + TimeSpan.FromMinutes(5);
            DateTime matchTime = lobbyTime + TimeSpan.FromMinutes(5);
            BitConverter.GetBytes(lobbyTime.Ticks).CopyTo(data, 0);
            BitConverter.GetBytes(matchTime.Ticks).CopyTo(data, sizeof(long));

            SendToClient(MessageType.MatchTimes, data, steamId);
        }

        #endregion

        #region Helpers

        public static void BroadcastToClients(MessageType type, byte[] data)
        {
            var newData = new byte[data.Length + 1];
            newData[0] = (byte)type;
            data.CopyTo(newData, 1);
            ServerJumpClass.Instance.SomeLog($"Sending message to others: {type}");
            MyAPIGateway.Utilities.InvokeOnGameThread(() => { MyAPIGateway.Multiplayer.SendMessageToOthers(NETWORK_ID, newData); });
        }

        public static void SendToClient(MessageType type, byte[] data, ulong recipient)
        {
            var newData = new byte[data.Length + 1];
            newData[0] = (byte)type;
            data.CopyTo(newData, 1);
            ServerJumpClass.Instance.SomeLog($"Sending message to {recipient}: {type}");
            MyAPIGateway.Utilities.InvokeOnGameThread(() => { MyAPIGateway.Multiplayer.SendMessageTo(NETWORK_ID, newData, recipient); });
        }

        public static void SendToServer(MessageType type, byte[] data)
        {
            var newData = new byte[data.Length + 1];
            newData[0] = (byte)type;
            data.CopyTo(newData, 1);
            ServerJumpClass.Instance.SomeLog($"Sending message to server: {type}");
            MyAPIGateway.Utilities.InvokeOnGameThread(() => { MyAPIGateway.Multiplayer.SendMessageToServer(NETWORK_ID, newData); });
        }

        #endregion
    }
}
