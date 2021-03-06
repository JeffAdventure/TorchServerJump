﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Timers;
using Sandbox.ModAPI;
using VRage.Game;
using VRageMath;

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
            ClientRequestJump,
            ResetClient,
            SoundMessage
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
            Logging.Instance.WriteLine($"UnRegister handlers");
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
                    case MessageType.ClientRequestJump:
                        OnClientRequesJump(data);
                        break;
                    case MessageType.MatchTimes:
                        OnMatchTimes(data);
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

            BinaryWriter writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage("Ship.bin", typeof(LinkModCore));
            writer.Write(message.Length);
            writer.Write(message);
            writer.Flush();
            writer.Close();
        }
        

        private static void OnRedirect(byte[] data)
        {
            Logging.Instance.WriteLine($"OnRedirect started");
            string ip = Encoding.ASCII.GetString(data);
            MyAPIGateway.Utilities.ShowNotification("You will jupm to a new server in 10 seconds", 10000, MyFontEnum.Blue);
            var timer = new Timer();
            timer.Interval = 10000;
            timer.Elapsed += (a, b) => Utilities.JoinServer(ip);
            timer.AutoReset = false;
            timer.Start();
            Logging.Instance.WriteLine($"OnRedirect fin");
        }

        private static void OnClientChat(byte[] data)
        {
            ulong id = BitConverter.ToUInt64(data, 0);
            string message = Encoding.UTF8.GetString(data, sizeof(ulong), data.Length - sizeof(ulong));
           // LinkModCore.Instance.HandleChatCommand(id, message);
        }
        private static void OnResetClient(byte[] data)
        {
            MyAPIGateway.Utilities.DeleteFileInLocalStorage("Ship.bin", typeof(LinkModCore));
            MyAPIGateway.Utilities.ShowMessage("Server", "delete Ship.bin");


            var timer1 = new Timer();
            timer1.Interval = 10000;
            timer1.Elapsed += (a, b) => { Communication.SendClientChat("!entities refresh");
                MyAPIGateway.Utilities.SendMessage("");
                
            };
            timer1.AutoReset = false;
            timer1.Start();

        }
        private static void OnClientRequesJump(byte[] data) {
          //  var notificaion = MyAPIGateway.Utilities.SerializeFromXML<Notification>(Encoding.UTF8.GetString(data));
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
        private static void OnSoundMessage(byte[] data)
        {

        }
        private static void OnMatchTimes(byte[] data)
        {
            long lobbyTime = BitConverter.ToInt64(data, 0);
            long matchTime = BitConverter.ToInt64(data, sizeof(long));

         //   LinkModCore.Instance.LobbyTime = new DateTime(lobbyTime);
      //      LinkModCore.Instance.MatchTime = new DateTime(matchTime);
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
          //  DateTime lobbyTime = LinkModCore.Instance.MatchStart + TimeSpan.FromMinutes(Settings.Instance.JoinTime);
         //   DateTime matchTime = lobbyTime + TimeSpan.FromMinutes(Settings.Instance.BattleTime);
         //   BitConverter.GetBytes(lobbyTime.Ticks).CopyTo(data, 0);
           // BitConverter.GetBytes(matchTime.Ticks).CopyTo(data, sizeof(long));

           // SendToClient(MessageType.MatchTimes, data, steamId);
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
