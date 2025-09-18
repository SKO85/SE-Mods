using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Utils;
using System;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public static class NetworkMessagingHandler
    {
        private static ushort MSGID_MOD_DATAREQUEST = 40000;
        private static ushort MSGID_MOD_SETTINGS = 40001;
        private static ushort MSGID_BLOCK_DATAREQUEST = 40100;
        private static ushort MSGID_BLOCK_SETTINGS_FROM_SERVER = 40102;
        private static ushort MSGID_BLOCK_SETTINGS_FROM_CLIENT = 40103;
        private static ushort MSGID_BLOCK_STATE_FROM_SERVER = 40104;

        #region Registration
        private static bool _registered = false;

        public static void Register()
        {
            if (_registered || MyAPIGateway.Multiplayer == null || MyAPIGateway.Utilities == null)
                return;

            if(MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_MOD_DATAREQUEST, ServerMsgDataRequestReceived);
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_DATAREQUEST, ServerMsgBlockDataRequestReceived);
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_SETTINGS_FROM_CLIENT, MsgBlockSettingsReceived);
            }
            else
            {
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_MOD_SETTINGS, ClientMsgModSettingsReceived);
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_SETTINGS_FROM_SERVER, MsgBlockSettingsReceived);
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_STATE_FROM_SERVER, ClientMsgBlockStateReceived);

                // Send first data request message on clients.
                MsgDataRequestSend();
            }

            _registered = true;
        }

        public static void Unregister() 
        {
            if (!_registered || MyAPIGateway.Multiplayer == null || MyAPIGateway.Utilities == null)
                return;

            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_MOD_DATAREQUEST, ServerMsgDataRequestReceived);
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_BLOCK_DATAREQUEST, ServerMsgBlockDataRequestReceived);
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_BLOCK_SETTINGS_FROM_CLIENT, MsgBlockSettingsReceived);
            }
            else
            {
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_MOD_SETTINGS, ClientMsgModSettingsReceived);
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_BLOCK_SETTINGS_FROM_SERVER, MsgBlockSettingsReceived);
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_BLOCK_STATE_FROM_SERVER, ClientMsgBlockStateReceived);
            }

            _registered = false;
        }

       
        #endregion

        #region Server Message Received Handlers       

        private static void ServerMsgDataRequestReceived(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgModDataRequest>(data);
            MsgModSettingsSend(msgRcv.SteamId);
        }

        private static void ServerMsgBlockDataRequestReceived(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgBlockDataRequest>(data);

            NanobotSystem system;
            if (Mod.NanobotSystems.TryGetValue(msgRcv.EntityId, out system))
            {
                if (Logging.Instance.ShouldLog(Logging.Level.Communication)) Logging.Instance.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncBlockDataRequestReceived SteamId={0} EntityId={1}/{2}", msgRcv.SteamId, msgRcv.EntityId, Logging.BlockName(system.Entity, Logging.BlockNameOptions.None));
                MsgBlockSettingsSend(msgRcv.SteamId, system);
                MsgBlockStateSend(msgRcv.SteamId, system);
            }
            else
            {
                if (Logging.Instance.ShouldLog(Logging.Level.Error)) Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockDataRequestReceived for unknown system SteamId{0} EntityId={1}", msgRcv.SteamId, msgRcv.EntityId);
            }
        }

        #endregion

        #region Client Message Received Handlers

        private static void ClientMsgModSettingsReceived(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgModSettings>(data);
                SyncModSettings.AdjustSettings(msgRcv.Settings);

                Mod.Settings = msgRcv.Settings;
                Mod.SettingsValid = true;
                Mod.SettingsChanged();
            }
            catch (Exception ex)
            {
                Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncModSettingsReceived Exception:{0}", ex);
            }
        }

        private static void ClientMsgBlockStateReceived(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgBlockState>(data);

                NanobotSystem system;
                if (Mod.NanobotSystems.TryGetValue(msgRcv.EntityId, out system))
                {
                    system.State.AssignReceived(msgRcv.State);
                }
                else
                {
                    if (Logging.Instance.ShouldLog(Logging.Level.Error))
                    {
                        Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockStateReceived for unknown system EntityId={0}", msgRcv.EntityId);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockStateReceived Exception:{0}", ex);
            }
        }

        #endregion

        #region General Received

        private static void MsgBlockSettingsReceived(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgBlockSettings>(data);

                NanobotSystem system;
                if (Mod.NanobotSystems.TryGetValue(msgRcv.EntityId, out system))
                {
                    system.Settings.AssignReceived(msgRcv.Settings, system.BlockWeldPriority, system.BlockGrindPriority, system.ComponentCollectPriority);
                    system.SettingsChanged();

                    if (MyAPIGateway.Session.IsServer)
                    {
                        MsgBlockSettingsSend(0, system);
                    }
                }
                else
                {
                    if (Logging.Instance.ShouldLog(Logging.Level.Error))
                    {
                        Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockSettingsReceived for unknown system EntityId={0}", msgRcv.EntityId);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockSettingsReceived Exception:{0}", ex);
            }
        }
        #endregion

        #region Send Handlers
        private static void MsgModSettingsSend(ulong steamId)
        {
            if (!MyAPIGateway.Session.IsServer)
                return;

            var msgSnd = new MsgModSettings()
            {
                Settings = Mod.Settings
            };

            if (!MyAPIGateway.Multiplayer.SendMessageTo(MSGID_MOD_SETTINGS, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), steamId, true))
            {
                Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncModSettingsSend failed");
            }
        }

        public static void MsgDataRequestSend()
        {
            if (MyAPIGateway.Session.IsServer)
                return;

            var msgSnd = new MsgModDataRequest();

            if (MyAPIGateway.Session.Player != null)
                msgSnd.SteamId = MyAPIGateway.Session.Player.SteamUserId;
            else
                msgSnd.SteamId = 0;

            MyAPIGateway.Multiplayer.SendMessageToServer(MSGID_MOD_DATAREQUEST, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
        }

        public static void MsgBlockSettingsSend(ulong steamId, NanobotSystem block)
        {
            var msgSnd = new MsgBlockSettings();
            msgSnd.EntityId = block.Entity.EntityId;
            msgSnd.Settings = block.Settings.GetTransmit();

            var res = false;

            if (MyAPIGateway.Session.IsServer)
            {
                if (steamId == 0)
                {
                    res = MyAPIGateway.Multiplayer.SendMessageToOthers(MSGID_BLOCK_SETTINGS_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
                }
                else
                {
                    res = MyAPIGateway.Multiplayer.SendMessageTo(MSGID_BLOCK_SETTINGS_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), steamId, true);
                }
            }
            else
            {
                res = MyAPIGateway.Multiplayer.SendMessageToServer(MSGID_BLOCK_SETTINGS_FROM_CLIENT, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
            }

            if (!res && Logging.Instance.ShouldLog(Logging.Level.Error))
            {
                Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockSettingsSend failed", Logging.BlockName(block.Entity, Logging.BlockNameOptions.None));
            }
        }

        public static void MsgBlockStateSend(ulong steamId, NanobotSystem system)
        {
            if (!MyAPIGateway.Session.IsServer)
                return;

            if (!MyAPIGateway.Multiplayer.MultiplayerActive)
                return;

            var msgSnd = new MsgBlockState();
            msgSnd.EntityId = system.Entity.EntityId;
            msgSnd.State = system.State.GetTransmit();

            var res = false;
            if (steamId == 0)
            {
                res = MyAPIGateway.Multiplayer.SendMessageToOthers(MSGID_BLOCK_STATE_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
            }
            else
            {
                res = MyAPIGateway.Multiplayer.SendMessageTo(MSGID_BLOCK_STATE_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), steamId, true);
            }

            if (!res && Logging.Instance.ShouldLog(Logging.Level.Error))
            {
                Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockStateSend Failed");
            }
        }

        public static void MsgBlockDataRequestSend(NanobotSystem block)
        {
            if (MyAPIGateway.Session.IsServer)
                return;

            var msgSnd = new MsgBlockDataRequest();

            if (MyAPIGateway.Session.Player != null)
                msgSnd.SteamId = MyAPIGateway.Session.Player.SteamUserId;
            else
                msgSnd.SteamId = 0;

            msgSnd.EntityId = block.Entity.EntityId;

            MyAPIGateway.Multiplayer.SendMessageToServer(MSGID_BLOCK_DATAREQUEST, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
        }
        #endregion
    }
}
