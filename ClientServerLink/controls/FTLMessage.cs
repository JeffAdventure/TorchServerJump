using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using ProtoBuf;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Ingame = Sandbox.ModAPI.Ingame;
using VRage;
using VRage.ObjectBuilders;
using VRage.Game;
using VRage.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace Phoenix.FTL
{
    #region MP messaging
    public enum MessageSide
    {
        ServerSide,
        ClientSide
    }

    #endregion

    /// <summary>
    /// This class is a quick workaround to get an abstract class deserialized. It is to be removed when using a byte serializer.
    /// </summary>
    [ProtoContract]
    public class MessageContainer
    {
        [ProtoMember(1)]
        public MessageBase Content;
    }

    public static class MessageUtils
    {
        public static List<byte> Client_MessageCache = new List<byte>();
        public static Dictionary<ulong, List<byte>> Server_MessageCache = new Dictionary<ulong, List<byte>>();

        public static readonly ushort MessageId = 19842;
        static readonly int MAX_MESSAGE_SIZE = 4096;

        public static void SendMessageToServer(MessageBase message)
        {
            message.Side = MessageSide.ServerSide;
            if (MyAPIGateway.Session.Player != null)
                message.SenderSteamId = MyAPIGateway.Session.Player.SteamUserId;
            var xml = MyAPIGateway.Utilities.SerializeToXML<MessageContainer>(new MessageContainer() { Content = message });
            byte[] byteData = System.Text.Encoding.UTF8.GetBytes(xml);
            Logger.Instance.LogDebug(string.Format("SendMessageToServer {0} {1} {2}, {3}b", message.SenderSteamId, message.Side, message.GetType().Name, byteData.Length));
            if (byteData.Length <= MAX_MESSAGE_SIZE)
                MyAPIGateway.Multiplayer.SendMessageToServer(MessageId, byteData);
            else
                SendMessageParts(byteData, MessageSide.ServerSide);
        }

        /// <summary>
        /// Creates and sends an entity with the given information for the server and all players.
        /// </summary>
        /// <param name="content"></param>
        public static void SendMessageToAll(MessageBase message, bool syncAll = true)
        {
            if (MyAPIGateway.Session.Player != null)
                message.SenderSteamId = MyAPIGateway.Session.Player.SteamUserId;

            if (syncAll || !MyAPIGateway.Multiplayer.IsServer)
                SendMessageToServer(message);
            SendMessageToAllPlayers(message);
        }

        public static void SendMessageToAllPlayers(MessageBase messageContainer)
        {
            //MyAPIGateway.Multiplayer.SendMessageToOthers(StandardClientId, System.Text.Encoding.Unicode.GetBytes(ConvertData(content))); <- does not work as expected ... so it doesn't work at all?
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, p => p != null && !p.IsHost());
            foreach (IMyPlayer player in players)
                SendMessageToPlayer(player.SteamUserId, messageContainer);
        }

        public static void SendMessageToPlayer(ulong steamId, MessageBase message)
        {
            message.Side = MessageSide.ClientSide;
            var xml = MyAPIGateway.Utilities.SerializeToXML(new MessageContainer() { Content = message });
            byte[] byteData = System.Text.Encoding.UTF8.GetBytes(xml);

            Logger.Instance.LogDebug(string.Format("SendMessageToPlayer {0} {1} {2}, {3}b", steamId, message.Side, message.GetType().Name, byteData.Length));
            
            if (byteData.Length <= MAX_MESSAGE_SIZE)
                MyAPIGateway.Multiplayer.SendMessageTo(MessageId, byteData, steamId);
            else
                SendMessageParts(byteData, MessageSide.ClientSide, steamId);
        }

        #region Message Splitting
        /// <summary>
        /// Calculates how many bytes can be stored in the given message.
        /// </summary>
        /// <param name="message">The message in which the bytes will be stored.</param>
        /// <returns>The number of bytes that can be stored until the message is too big to be sent.</returns>
        public static int GetFreeByteElementCount(MessageIncomingMessageParts message)
        {
            message.Content = new byte[1];
            var xmlText = MyAPIGateway.Utilities.SerializeToXML<MessageContainer>(new MessageContainer() { Content = message });
            var oneEntry = System.Text.Encoding.UTF8.GetBytes(xmlText).Length;

            message.Content = new byte[4];
            xmlText = MyAPIGateway.Utilities.SerializeToXML<MessageContainer>(new MessageContainer() { Content = message });
            var twoEntries = System.Text.Encoding.UTF8.GetBytes(xmlText).Length;

            // we calculate the difference between one and two entries in the array to get the count of bytes that describe one entry
            // we divide by 3 because 3 entries are stored in one block of the array
            var difference = (double)(twoEntries - oneEntry) / 3d;

            // get the size of the message without any entries
            var freeBytes = MAX_MESSAGE_SIZE - oneEntry - Math.Ceiling(difference);

            int count = (int)Math.Floor((double)freeBytes / difference);

            // finally we test if the calculation was right
            message.Content = new byte[count];
            xmlText = MyAPIGateway.Utilities.SerializeToXML<MessageContainer>(new MessageContainer() { Content = message });
            var finalLength = System.Text.Encoding.UTF8.GetBytes(xmlText).Length;
            Logger.Instance.LogDebug(string.Format("FinalLength: {0}", finalLength));
            if (MAX_MESSAGE_SIZE >= finalLength)
                return count;
            else
                throw new Exception(string.Format("Calculation failed. OneEntry: {0}, TwoEntries: {1}, Difference: {2}, FreeBytes: {3}, Count: {4}, FinalLength: {5}", oneEntry, twoEntries, difference, freeBytes, count, finalLength));
        }

        private static void SendMessageParts(byte[] byteData, MessageSide side, ulong receiver = 0)
        {
            Logger.Instance.LogDebug(string.Format("SendMessageParts {0} {1} {2}", byteData.Length, side, receiver));

            var byteList = byteData.ToList();

            while (byteList.Count > 0)
            {
                // we create an empty message part
                var messagePart = new MessageIncomingMessageParts()
                {
                    Side = side,
                    SenderSteamId = side == MessageSide.ServerSide ? MyAPIGateway.Session.Player.SteamUserId : 0,
                    LastPart = false,
                };

                try
                {
                    // let's check how much we could store in the message
                    int freeBytes = GetFreeByteElementCount(messagePart);

                    int count = freeBytes;

                    // we check if that might be the last message
                    if (freeBytes > byteList.Count)
                    {
                        messagePart.LastPart = true;

                        // since we changed LastPart, we should make sure that we are still able to send all the stuff
                        if (GetFreeByteElementCount(messagePart) > byteList.Count)
                        {
                            count = byteList.Count;
                        }
                        else
                            throw new Exception("Failed to send message parts. The leftover could not be sent!");
                    }

                    // fill the message with content
                    messagePart.Content = byteList.GetRange(0, count).ToArray();
                    var xmlPart = MyAPIGateway.Utilities.SerializeToXML<MessageContainer>(new MessageContainer() { Content = messagePart });
                    var bytes = System.Text.Encoding.UTF8.GetBytes(xmlPart);

                    // and finally send the message
                    switch (side)
                    {
                        case MessageSide.ClientSide:
                            if (MyAPIGateway.Multiplayer.SendMessageTo(MessageId, bytes, receiver))
                                byteList.RemoveRange(0, count);
                            else
                                throw new Exception("Failed to send message parts to client.");
                            break;
                        case MessageSide.ServerSide:
                            if (MyAPIGateway.Multiplayer.SendMessageToServer(MessageId, bytes))
                                byteList.RemoveRange(0, count);
                            else
                                throw new Exception("Failed to send message parts to server.");
                            break;
                    }

                }
                catch (Exception ex)
                {
                    Logger.Instance.LogException(ex);
                    return;
                }
            }
        }
        #endregion

        public static void HandleMessage(byte[] rawData)
        {
            try
            {
                var data = System.Text.Encoding.UTF8.GetString(rawData);
                var message = MyAPIGateway.Utilities.SerializeFromXML<MessageContainer>(data);

                Logger.Instance.LogDebug("HandleMessage()");
                if (message != null && message.Content != null)
                {
                    message.Content.InvokeProcessing();
                }
                return;
            }
            catch (Exception e)
            {
                // Don't warn the user of an exception, this can happen if two mods with the same message id receive an unknown message
                Logger.Instance.LogMessage(string.Format("Processing message exception. Exception: {0}", e.ToString()));
                //Logger.Instance.LogException(e);
            }
        }
    }

    [ProtoContract]
    public class MessageSpooling : MessageBase
    {
        [ProtoMember(1)]
        public long FTLId;
        [ProtoMember(2)]
        public VRageMath.Vector3D Destination = VRageMath.Vector3D.Zero;

        public override void ProcessClient()
        {
            if (MyAPIGateway.Entities.EntityExists(FTLId))
            {
                var ftl = MyAPIGateway.Entities.GetEntityById(FTLId) as IMyFunctionalBlock;
                var ftld = ftl.GetFTLData();
                ftld.jumpDest = Destination;
            }
        }

        public override void ProcessServer()
        {
            // None
        }
    }

    [ProtoContract]
    public class MessageText : MessageBase
    {
        [ProtoMember(1)]
        public string Message;
        [ProtoMember(2)]
        public int Timeout = 2000;
        [ProtoMember(3)]
        public bool Error = false;

        public override void ProcessClient()
        {
            MyAPIGateway.Utilities.ShowNotification(Message, Timeout, (Error ? MyFontEnum.Red : MyFontEnum.White));
        }

        public override void ProcessServer()
        {
            // None
        }
    }

    [ProtoContract]
    public class MessageGPS : MessageBase
    {
        [ProtoMember(1)]
        public long FTLId;
        [ProtoMember(2)]
        public SerializableVector3D? Destination = VRageMath.Vector3D.Zero;
        [ProtoMember(2)]
        public string Name;

        public override void ProcessClient()
        {
            // None
        }

        public override void ProcessServer()
        {
            var ftl = MyAPIGateway.Entities.GetEntityById(FTLId) as IMyFunctionalBlock;

            if (ftl == null)        // Something happened
                return;

            if (FTLAdmin.Configuration.AllowPBControl)
            {
                var ftld = ftl.GetFTLData();
                // TODO Migrate to new way
                //ftld.explicitDest = Destination;
                ftld.flags |= JumpFlags.GPSWaypoint;

                // Save a local gps entry, in case a friendly player edits it later
                if (ftl.GetPlayerRelationToOwner() != MyRelationsBetweenPlayerAndBlock.Enemies)
                {
                    if (Destination.HasValue)
                    {
                        Logger.Instance.LogMessage(string.Format("Received GPS '{0}': {1:F0}, {2:F0}, {3:F0}", Name, Destination.Value.X, Destination.Value.Y, Destination.Value.Z));
                        var gps = MyAPIGateway.Session.GPS.Create(Name, null, Destination.Value, false, true);
                        ftld.jumpDest = Destination.Value;
                        //ftld.jumpTargetGPS = gps;
                        ftl.GameLogic.GetAs<FTLBase>().UpdateFTLStats();
                        //gps.DiscardAt = MyAPIGateway.Session.ElapsedPlayTime.Add(TimeSpan.FromSeconds(1));
                        //MyAPIGateway.Session.GPS.AddLocalGps(gps);
                    }
                }
                else
                {
                    Logger.Instance.LogMessage(string.Format("Received GPS '{0}'", Name));
                }
                MessageUtils.SendMessageToAllPlayers(this);
            }
        }
    }

    [ProtoContract]
    public class MessageSelectGPS : MessageBase
    {
        [ProtoMember(1)]
        public long FTLId;
        [ProtoMember(2)]
        public int GPSHash;
        [ProtoMember(2)]
        public SerializableVector3D? Coords;

        public override void ProcessClient()
        {
            DoWork();
        }

        public override void ProcessServer()
        {
            DoWork();
        }

        private void DoWork()
        {
            var ftl = MyAPIGateway.Entities.GetEntityById(FTLId) as IMyFunctionalBlock;

            if (ftl == null)        // Something happened
                return;

            var ftld = ftl.GetFTLData();
            
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, (x) => x.SteamUserId == SenderSteamId);
            ftld.jumpTargetGPS = FTLExtensions.GetGPSFromHash(GPSHash, players[0].IdentityId);

            if( GPSHash == 0 && Coords.HasValue)
            {
                ftld.jumpTargetGPS = MyAPIGateway.Session.GPS.Create("FTL Generated", string.Empty, Coords.Value, false, true);
            }

            if (ftld.jumpTargetGPS != null)
                Logger.Instance.LogMessage(string.Format("Found GPS: " + ftld.jumpTargetGPS.Name));
            else
                Logger.Instance.LogMessage(string.Format("Cleared GPS"));

            ftl.GameLogic.GetAs<FTLBase>().SaveTerminalValues();
        }
    }

    [ProtoContract]
    public class MessageSetJumpDistance : MessageBase
    {
        [ProtoMember(1)]
        public long FTLId;
        [ProtoMember(2)]
        public float Distance;

        public override void ProcessClient()
        {
            DoWork();
        }

        public override void ProcessServer()
        {
            DoWork();
        }

        private void DoWork()
        {
            var ftl = MyAPIGateway.Entities.GetEntityById(FTLId) as IMyFunctionalBlock;

            if (ftl == null)        // Something happened
                return;

            var ftld = ftl.GetFTLData();

            ftld.jumpDistance = Distance;

            ftl.GameLogic.GetAs<FTLBase>().SaveTerminalValues();
        }
    }

    [ProtoContract]
    public class MessageMove : MessageBase
    {
        [ProtoMember(1)]
        public List<long> Entities;                     // List of entities to operate on
        [ProtoMember(2)]
        public List<VRageMath.Vector3D> Positions;      // List of positions for above entities

        //[ProtoMember(3)]
        //public List<MyTuple2<long, VRageMath.MatrixD, VRageMath.Vector3D>> positions;  // List of positions for entities

        public override void ProcessClient()
        {
            DoMove();
        }

        public override void ProcessServer()
        {
            DoMove();
        }

        private void DoMove()
        {
            VRageMath.BoundingBoxD aggregatebox = new VRageMath.BoundingBoxD();
            for (var x = 0; x < Entities.Count; x++)
            {
                var entity = MyAPIGateway.Entities.GetEntityById(Entities[x]);
                if (entity != null)
                {
                    var box = entity.PositionComp.WorldAABB;
                    box.Translate(entity.PositionComp.GetPosition() - Positions[x]);
                    aggregatebox.Include(ref box);
                }
            }
            MyAPIGateway.Physics.EnsurePhysicsSpace(aggregatebox);
            for (var x = 0; x < Entities.Count; x++)
            {
                var entity = MyAPIGateway.Entities.GetEntityById(Entities[x]);
                if (entity != null)
                {
                    entity.PositionComp.SetPosition(Positions[x]);

                    //if (entity.SyncObject != null)
                    //    entity.SyncObject.UpdatePosition();

                    Logger.Instance.LogMessage(string.Format("updated {0} to: {1:F0}, {2:F0}, {3:F0}", entity.DisplayName, Positions[x].X, Positions[x].Y, Positions[x].Z));
                }
            }
        }
    }

    [ProtoContract]
    public class MessageFont : MessageBase
    {
        [ProtoMember(1)]
        public bool FixedFontSize = true;

        public override void ProcessClient()
        {
            // None
        }

        public override void ProcessServer()
        {
            FTLAdmin.Configuration.FixedFontSize = FixedFontSize;
            MessageUtils.SendMessageToPlayer(SenderSteamId, new MessageChat() { Sender = Globals.ModName, MessageText = "Fixed LCD font size: " + FixedFontSize.ToString() });
        }
    }

    [ProtoContract]
    public class MessageStockJump : MessageBase
    {
        [ProtoMember(1)]
        public bool BlockStockJump = false;

        public override void ProcessClient()
        {
            // None
        }

        public override void ProcessServer()
        {
            FTLAdmin.Configuration.BlockStockJump = BlockStockJump;
            MessageUtils.SendMessageToPlayer(SenderSteamId, new MessageChat() { Sender = Globals.ModName, MessageText = "Inhibit stock jump drives: " + FTLAdmin.Configuration.BlockStockJump.ToString() });
        }
    }

    [ProtoContract]
    public class MessagePBControl : MessageBase
    {
        [ProtoMember(1)]
        public bool AllowPBControl = false;

        public override void ProcessClient()
        {
            // None
        }

        public override void ProcessServer()
        {
            FTLAdmin.Configuration.AllowPBControl = AllowPBControl;
            MessageUtils.SendMessageToPlayer(SenderSteamId, new MessageChat() { Sender = Globals.ModName, MessageText = "Allow PB control of FTL: " + FTLAdmin.Configuration.AllowPBControl.ToString() });
        }
    }

    [ProtoContract]
    public class MessageGravityWell : MessageBase
    {
        [ProtoMember(1)]
        public bool AllowGravityWellJump = true;

        public override void ProcessClient()
        {
            // None
        }

        public override void ProcessServer()
        {
            FTLAdmin.Configuration.AllowGravityWellJump = AllowGravityWellJump;
            MessageUtils.SendMessageToPlayer(SenderSteamId, new MessageChat() { Sender = Globals.ModName, MessageText = "Jump within gravity well: " + FTLAdmin.Configuration.AllowGravityWellJump.ToString() });
        }
    }

    [ProtoContract]
    public class MessageValueChange : MessageBase
    {
        [ProtoMember(1)]
        public ChangeType ValueType;
        [ProtoMember(2)]
        public ModifierType Type;
        [ProtoMember(3)]
        public float Modifier;
        [ProtoMember(4)]
        public bool Reset;

        public override void ProcessClient()
        {
            // None
        }

        public override void ProcessServer()
        {
            string message = string.Format("invalid {1} modifier: {0}", Type, ValueType.ToString().ToLowerInvariant());
            List<MyTuple<ModifierType, float>> list = null;

            if (ValueType == ChangeType.Base)
                list = FTLAdmin.Configuration.BaseValues;
            else if (ValueType == ChangeType.Upgrade)
                list = FTLAdmin.Configuration.Upgrades;

            if (Reset)
            {
                MyTuple<ModifierType, float>? val = null;
                foreach (var entry in list)
                {
                    if (entry.Item1 == Type)
                    {
                        val = entry;
                        break;
                    }
                }
                if (val == null)
                {
                    message = string.Format("{1} modifier {0} already default", Type, ValueType.ToString().ToLowerInvariant());
                }
                else
                {
                    list.Remove(val.Value);
                    message = string.Format("{1} modifier {0} reset to default", Type, ValueType.ToString().ToLowerInvariant());
                }
            }
            else
            {
                bool found = false;
                for (int x = 0; x < list.Count; x++)
                {
                    if (list[x].Item1 == Type)
                    {
                        var item = list[x];
                        item.Item2 = Modifier;
                        list[x] = item;
                        found = true;
                    }
                }

                if (!found)
                    list.Add(new MyTuple<ModifierType, float>(Type, Modifier));

                message = string.Format("{2} modifier {0} set to {1}", Type, Modifier, ValueType.ToString().ToLowerInvariant());
            }

            // Force reload all FTL data
            Globals.Reload();
            FTLData.ReloadAll();
            FTLInhibitor.ReloadAll();

            MessageUtils.SendMessageToPlayer(SenderSteamId, new MessageChat() { Sender = Globals.ModName, MessageText = message });
        }
    }

    [ProtoContract]
    public class MessageWarpEffect : MessageBase
    {
        public long FTLId;
        public bool Remove = false;

        public override void ProcessClient()
        {
            SpawnEffect(Remove);
        }

        public override void ProcessServer()
        {
            if( !MyAPIGateway.Utilities.IsDedicated )
                SpawnEffect(Remove);
        }

        private void SpawnEffect(bool remove)
        {
            var ftl = MyAPIGateway.Entities.GetEntityById(FTLId) as IMyFunctionalBlock;

            if (ftl != null)
            {
                if (Remove)
                    ftl.GameLogic.GetAs<FTLBase>().StopParticleEffects();
                else
                    ftl.GameLogic.GetAs<FTLBase>().PlayParticleEffects();
            }
        }
    }

    [ProtoContract]
    public class MessageSoundEffect : MessageBase
    {
        public long FTLId;
        public string SoundName = null;
        public bool StopPrevious = false;
        public bool ForceStop = false;

        public override void ProcessClient()
        {
            PlaySound();
        }

        public override void ProcessServer()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
                PlaySound();
        }

        private void PlaySound()
        {
            var ftl = MyAPIGateway.Entities.GetEntityById(FTLId) as IMyFunctionalBlock;

            if (ftl != null)
            {
                ftl.GameLogic.GetAs<FTLBase>().PlaySound(SoundName, StopPrevious);
            }
        }
    }

    [ProtoContract]
    public class MessageSave : MessageBase
    {
        public override void ProcessClient()
        {
            // never processed here
        }

        public override void ProcessServer()
        {
            FTLAdmin.SaveConfig();
            MessageUtils.SendMessageToPlayer(SenderSteamId, new MessageChat() { Sender = Globals.ModName, MessageText = "Config saved" });
        }
    }

    [ProtoContract]
    public class MessageDebug : MessageBase
    {
        [ProtoMember(1)]
        public bool DebugMode;

        public override void ProcessClient()
        {
            EnableDebug();
        }

        public override void ProcessServer()
        {
            EnableDebug();
        }

        private void EnableDebug()
        {
            FTLAdmin.Configuration.Debug = DebugMode;
            Logger.Instance.Debug = DebugMode;
            MessageUtils.SendMessageToPlayer(SenderSteamId, new MessageChat() { Sender = Globals.ModName, MessageText = "Debug mode " + FTLAdmin.Configuration.Debug.ToString() });
        }
    }

    [ProtoContract]
    public class MessageChat : MessageBase
    {
        public string Sender;
        public string MessageText;

        public override void ProcessClient()
        {
            MyAPIGateway.Utilities.ShowMessage(Sender, MessageText);
        }

        public override void ProcessServer()
        {
            // None
        }
    }


    #region Message Splitting
    [ProtoContract]
    public class MessageIncomingMessageParts : MessageBase
    {
        [ProtoMember(1)]
        public byte[] Content;

        [ProtoMember(2)]
        public bool LastPart;

        public override void ProcessClient()
        {
            MessageUtils.Client_MessageCache.AddRange(Content.ToList());

            if (LastPart)
            {
                MessageUtils.HandleMessage(MessageUtils.Client_MessageCache.ToArray());
                MessageUtils.Client_MessageCache.Clear();
            }
        }

        public override void ProcessServer()
        {
            if (MessageUtils.Server_MessageCache.ContainsKey(SenderSteamId))
                MessageUtils.Server_MessageCache[SenderSteamId].AddRange(Content.ToList());
            else
                MessageUtils.Server_MessageCache.Add(SenderSteamId, Content.ToList());

            if (LastPart)
            {
                MessageUtils.HandleMessage(MessageUtils.Server_MessageCache[SenderSteamId].ToArray());
                MessageUtils.Server_MessageCache[SenderSteamId].Clear();
            }
        }

    }
    #endregion

    /// <summary>
    /// This is a base class for all messages
    /// </summary>
    // ALL CLASSES DERIVED FROM MessageBase MUST BE ADDED HERE
    [XmlInclude(typeof(MessageIncomingMessageParts))]
    [XmlInclude(typeof(MessageDebug))]
    [XmlInclude(typeof(MessageChat))]
    [XmlInclude(typeof(MessageSave))]
    [XmlInclude(typeof(MessageMove))]
    [XmlInclude(typeof(MessageSpooling))]
    [XmlInclude(typeof(MessageGPS))]
    [XmlInclude(typeof(MessageSelectGPS))]
    [XmlInclude(typeof(MessageSetJumpDistance))]
    [XmlInclude(typeof(MessageText))]
    [XmlInclude(typeof(MessageFont))]
    [XmlInclude(typeof(MessageStockJump))]
    [XmlInclude(typeof(MessagePBControl))]
    [XmlInclude(typeof(MessageValueChange))]
    [XmlInclude(typeof(MessageWarpEffect))]
    [XmlInclude(typeof(MessageSoundEffect))]
    [XmlInclude(typeof(MessageGravityWell))]

    [ProtoContract]
    public abstract class MessageBase
    {
        /// <summary>
        /// The SteamId of the message's sender. Note that this will be set when the message is sent, so there is no need for setting it otherwise.
        /// </summary>
        [ProtoMember(1)]
        public ulong SenderSteamId;

        /// <summary>
        /// Defines on which side the message should be processed. Note that this will be set when the message is sent, so there is no need for setting it otherwise.
        /// </summary>
        [ProtoMember(2)]
        public MessageSide Side = MessageSide.ClientSide;

        /*
        [ProtoAfterDeserialization]
        void InvokeProcessing() // is not invoked after deserialization from xml
        {
            Logger.Debug("START - Processing");
            switch (Side)
            {
                case MessageSide.ClientSide:
                    ProcessClient();
                    break;
                case MessageSide.ServerSide:
                    ProcessServer();
                    break;
            }
            Logger.Debug("END - Processing");
        }
        */

        public void InvokeProcessing()
        {
            switch (Side)
            {
                case MessageSide.ClientSide:
                    InvokeClientProcessing();
                    break;
                case MessageSide.ServerSide:
                    InvokeServerProcessing();
                    break;
            }
        }

        private void InvokeClientProcessing()
        {
            Logger.Instance.LogDebug(string.Format("START - Processing [Client] {0}", this.GetType().Name));
            try
            {
                ProcessClient();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
            Logger.Instance.LogDebug(string.Format("END - Processing [Client] {0}", this.GetType().Name));
        }

        private void InvokeServerProcessing()
        {
            Logger.Instance.LogDebug(string.Format("START - Processing [Server] {0}", this.GetType().Name));

            try
            {
                ProcessServer();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }

            Logger.Instance.LogDebug(string.Format("END - Processing [Server] {0}", this.GetType().Name));
        }

        public abstract void ProcessClient();
        public abstract void ProcessServer();
    }
}
// vim: tabstop=4 expandtab shiftwidth=4 nobackup
