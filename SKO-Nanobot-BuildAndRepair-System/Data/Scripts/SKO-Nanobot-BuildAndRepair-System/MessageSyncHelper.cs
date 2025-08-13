using Sandbox.ModAPI;
using System;

namespace SKONanobotBuildAndRepairSystem
{
    public static class MessageSyncHelper
    {
        private static bool _registered = false;

        public static void RegisterAll()
        {
            if (_registered || MyAPIGateway.Multiplayer == null)
                return;

            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Multiplayer.RegisterMessageHandler(Constants.MSGID_MOD_DATAREQUEST, SyncModDataRequestReceived);
                MyAPIGateway.Multiplayer.RegisterMessageHandler(Constants.MSGID_BLOCK_DATAREQUEST, SyncBlockDataRequestReceived);
                MyAPIGateway.Multiplayer.RegisterMessageHandler(Constants.MSGID_BLOCK_SETTINGS_FROM_CLIENT, SyncBlockSettingsReceived);
            }
            else
            {
                MyAPIGateway.Multiplayer.RegisterMessageHandler(Constants.MSGID_MOD_SETTINGS, SyncModSettingsReceived);
                MyAPIGateway.Multiplayer.RegisterMessageHandler(Constants.MSGID_BLOCK_SETTINGS_FROM_SERVER, SyncBlockSettingsReceived);
                MyAPIGateway.Multiplayer.RegisterMessageHandler(Constants.MSGID_BLOCK_STATE_FROM_SERVER, SyncBlockStateReceived);
            }

            _registered = true;
        }

        public static void UnregisterAll()
        {
            if (!_registered || MyAPIGateway.Multiplayer == null)
                return;

            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(Constants.MSGID_MOD_DATAREQUEST, SyncModDataRequestReceived);
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(Constants.MSGID_BLOCK_DATAREQUEST, SyncBlockDataRequestReceived);
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(Constants.MSGID_BLOCK_SETTINGS_FROM_CLIENT, SyncBlockSettingsReceived);
            }
            else
            {
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(Constants.MSGID_MOD_SETTINGS, SyncModSettingsReceived);
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(Constants.MSGID_BLOCK_SETTINGS_FROM_SERVER, SyncBlockSettingsReceived);
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(Constants.MSGID_BLOCK_STATE_FROM_SERVER, SyncBlockStateReceived);
            }

            _registered = false;
        }

        private static void SyncModDataRequestReceived(byte[] dataRcv)
        {
            var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgModDataRequest>(dataRcv);
            Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncModDataRequestReceived SteamId={0}", msgRcv.SteamId);
            SyncModSettingsSend(msgRcv.SteamId);
        }

        private static void SyncBlockDataRequestReceived(byte[] dataRcv)
        {
            var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgBlockDataRequest>(dataRcv);

            NanobotBuildAndRepairSystemBlock system;
            if (NanobotBuildAndRepairSystemMod.BuildAndRepairSystems.TryGetValue(msgRcv.EntityId, out system))
            {
                Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncBlockDataRequestReceived SteamId={0} EntityId={1}/{2}", msgRcv.SteamId, msgRcv.EntityId, Logging.BlockName(system.Entity, Logging.BlockNameOptions.None));
                SyncBlockSettingsSend(msgRcv.SteamId, system);
                SyncBlockStateSend(msgRcv.SteamId, system);
            }
            else
            {
                Logging.Instance?.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockDataRequestReceived for unknown system SteamId{0} EntityId={1}", msgRcv.SteamId, msgRcv.EntityId);
            }
        }

        public static void SyncBlockSettingsSend(ulong steamId, NanobotBuildAndRepairSystemBlock block)
        {
            var msgSnd = new MsgBlockSettings();
            msgSnd.EntityId = block.Entity.EntityId;
            msgSnd.Settings = block.Settings.GetTransmit();

            var res = false;
            if (MyAPIGateway.Session.IsServer)
            {
                if (steamId == 0)
                {
                    Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncBlockSettingsSend To Others EntityId={0}/{1}", block.Entity.EntityId, Logging.BlockName(block.Entity, Logging.BlockNameOptions.None));
                    res = MyAPIGateway.Multiplayer.SendMessageToOthers(Constants.MSGID_BLOCK_SETTINGS_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
                }
                else
                {
                    Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncBlockSettingsSend To SteamId={2} EntityId={0}/{1}", block.Entity.EntityId, Logging.BlockName(block.Entity, Logging.BlockNameOptions.None), steamId);
                    res = MyAPIGateway.Multiplayer.SendMessageTo(Constants.MSGID_BLOCK_SETTINGS_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), steamId, true);
                }
            }
            else
            {
                Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncBlockSettingsSend To Server EntityId={0}/{1} to Server", block.Entity.EntityId, Logging.BlockName(block.Entity, Logging.BlockNameOptions.None));
                res = MyAPIGateway.Multiplayer.SendMessageToServer(Constants.MSGID_BLOCK_SETTINGS_FROM_CLIENT, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
            }

            if (!res) Logging.Instance?.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockSettingsSend failed", Logging.BlockName(block.Entity, Logging.BlockNameOptions.None));
        }

        public static void SyncBlockStateSend(ulong steamId, NanobotBuildAndRepairSystemBlock system)
        {
            if (!MyAPIGateway.Session.IsServer) return;
            if (!MyAPIGateway.Multiplayer.MultiplayerActive) return;

            var msgSnd = new MsgBlockState();
            msgSnd.EntityId = system.Entity.EntityId;
            msgSnd.State = system.State.GetTransmit();

            var res = false;
            if (steamId == 0)
            {
                Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncBlockStateSend to others EntityId={0}/{1}, State={2}", system.Entity.EntityId, Logging.BlockName(system.Entity, Logging.BlockNameOptions.None), msgSnd.State.ToString());
                res = MyAPIGateway.Multiplayer.SendMessageToOthers(Constants.MSGID_BLOCK_STATE_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
            }
            else
            {
                Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncBlockStateSend to SteamId={0} EntityId={1}/{2}, State={3}", steamId, system.Entity.EntityId, Logging.BlockName(system.Entity, Logging.BlockNameOptions.None), msgSnd.State.ToString());
                res = MyAPIGateway.Multiplayer.SendMessageTo(Constants.MSGID_BLOCK_STATE_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), steamId, true);
            }

            if (!res) Logging.Instance?.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockStateSend Failed");
        }

        private static void SyncBlockSettingsReceived(byte[] dataRcv)
        {
            try
            {
                var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgBlockSettings>(dataRcv);

                NanobotBuildAndRepairSystemBlock system;
                if (NanobotBuildAndRepairSystemMod.BuildAndRepairSystems.TryGetValue(msgRcv.EntityId, out system))
                {
                    Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncBlockSettingsReceived EntityId={0}/{1}", msgRcv.EntityId, Logging.BlockName(system.Entity, Logging.BlockNameOptions.None));
                    system.Settings.AssignReceived(msgRcv.Settings, system.BlockWeldPriority, system.BlockGrindPriority, system.ComponentCollectPriority);
                    system.SettingsChanged();
                    if (MyAPIGateway.Session.IsServer)
                    {
                        SyncBlockSettingsSend(0, system);
                    }
                }
                else
                {
                    Logging.Instance?.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockSettingsReceived for unknown system EntityId={0}", msgRcv.EntityId);
                }
            }
            catch (Exception ex)
            {
                Logging.Instance?.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockSettingsReceived Exception:{0}", ex);
            }
        }

        private static void SyncModSettingsReceived(byte[] dataRcv)
        {
            try
            {
                var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgModSettings>(dataRcv);
                SyncModSettings.AdjustSettings(msgRcv.Settings);
                NanobotBuildAndRepairSystemMod.Settings = msgRcv.Settings;
                NanobotBuildAndRepairSystemMod.SettingsValid = true;
                Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncModSettingsReceived");
                NanobotBuildAndRepairSystemMod.ApplySettingsToSystems();
            }
            catch (Exception ex)
            {
                Logging.Instance?.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncModSettingsReceived Exception:{0}", ex);
            }
        }

        private static void SyncModSettingsSend(ulong steamId)
        {
            if (!MyAPIGateway.Session.IsServer) return;
            var msgSnd = new MsgModSettings();
            msgSnd.Settings = NanobotBuildAndRepairSystemMod.Settings;

            Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncModSettingsSend SteamId={0}", steamId);

            if (!MyAPIGateway.Multiplayer.SendMessageTo(Constants.MSGID_MOD_SETTINGS, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), steamId, true))
            {
                Logging.Instance?.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncModSettingsSend failed");
            }
        }

        private static void SyncBlockStateReceived(byte[] dataRcv)
        {
            try
            {
                var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgBlockState>(dataRcv);

                NanobotBuildAndRepairSystemBlock system;
                if (NanobotBuildAndRepairSystemMod.BuildAndRepairSystems.TryGetValue(msgRcv.EntityId, out system))
                {
                    Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncBlockStateReceived EntityId={0}/{1}, State={2}", system.Entity.EntityId, Logging.BlockName(system.Entity, Logging.BlockNameOptions.None), msgRcv.State.ToString());
                    system.State.AssignReceived(msgRcv.State);
                }
                else
                {
                    Logging.Instance?.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockStateReceived for unknown system EntityId={0}", msgRcv.EntityId);
                }
            }
            catch (Exception ex)
            {
                Logging.Instance?.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockStateReceived Exception:{0}", ex);
            }
        }

        public static void SyncModDataRequestSend()
        {
            if (MyAPIGateway.Session.IsServer) return;

            var msgSnd = new MsgModDataRequest();
            msgSnd.SteamId = MyAPIGateway.Session.Player != null ? MyAPIGateway.Session.Player.SteamUserId : (ulong)0;

            Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncModDataRequestSend SteamId={0}", msgSnd.SteamId);
            MyAPIGateway.Multiplayer.SendMessageToServer(Constants.MSGID_MOD_DATAREQUEST, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
        }

        public static void SyncBlockDataRequestSend(NanobotBuildAndRepairSystemBlock block)
        {
            if (MyAPIGateway.Session.IsServer) return;

            var msgSnd = new MsgBlockDataRequest();
            if (MyAPIGateway.Session.Player != null)
                msgSnd.SteamId = MyAPIGateway.Session.Player.SteamUserId;
            else
                msgSnd.SteamId = 0;
            msgSnd.EntityId = block.Entity.EntityId;

            Logging.Instance?.Write(Logging.Level.Communication, "BuildAndRepairSystemMod: SyncBlockDataRequestSend SteamId={0} EntityId={1}/{2}", msgSnd.SteamId, msgSnd.EntityId, Logging.BlockName(block.Entity, Logging.BlockNameOptions.None));
            MyAPIGateway.Multiplayer.SendMessageToServer(Constants.MSGID_BLOCK_DATAREQUEST, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
        }

    }
}