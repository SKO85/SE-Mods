using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Chat;
using SKONanobotBuildAndRepairSystem.Extensions;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;

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
        private static ushort MSGID_MOD_COMMAND_FROM_CLIENT = 40002;
        private static ushort MSGID_MOD_COMMAND_RESPONSE_FROM_SERVER = 40003;
        private static ushort MSGID_DEBUG_STATS_FROM_SERVER = 40004;
        private static ushort MSGID_PROFILE_SUMMARY_FROM_SERVER = 40005;

        #region Registration

        private static bool _registered = false;

        public static void Register()
        {
            if (_registered || MyAPIGateway.Multiplayer == null || MyAPIGateway.Utilities == null)
                return;

            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_MOD_DATAREQUEST, ServerMsgDataRequestReceived);
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_DATAREQUEST, ServerMsgBlockDataRequestReceived);
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_SETTINGS_FROM_CLIENT, MsgBlockSettingsReceived);
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_MOD_COMMAND_FROM_CLIENT, ServerMsgModCommandReceived);
            }
            else
            {
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_MOD_SETTINGS, ClientMsgModSettingsReceived);
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_SETTINGS_FROM_SERVER, MsgBlockSettingsReceived);
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_STATE_FROM_SERVER, ClientMsgBlockStateReceived);
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_MOD_COMMAND_RESPONSE_FROM_SERVER, ClientMsgModCommandResponseReceived);
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_DEBUG_STATS_FROM_SERVER, ClientMsgDebugStatsReceived);
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_PROFILE_SUMMARY_FROM_SERVER, ClientMsgProfileSummaryReceived);

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
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_MOD_COMMAND_FROM_CLIENT, ServerMsgModCommandReceived);
            }
            else
            {
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_MOD_SETTINGS, ClientMsgModSettingsReceived);
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_BLOCK_SETTINGS_FROM_SERVER, MsgBlockSettingsReceived);
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_BLOCK_STATE_FROM_SERVER, ClientMsgBlockStateReceived);
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_MOD_COMMAND_RESPONSE_FROM_SERVER, ClientMsgModCommandResponseReceived);
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_DEBUG_STATS_FROM_SERVER, ClientMsgDebugStatsReceived);
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_PROFILE_SUMMARY_FROM_SERVER, ClientMsgProfileSummaryReceived);
            }

            _registered = false;
        }

        #endregion Registration

        #region Server Message Received Handlers

        private static void ServerMsgDataRequestReceived(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgModDataRequest>(data);
                MsgModSettingsSend(msgRcv.SteamId);
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "ServerMsgDataRequestReceived: {0}", ex.Message); }
        }

        private static void ServerMsgBlockDataRequestReceived(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgBlockDataRequest>(data);

                NanobotSystem system;
                if (Mod.NanobotSystems.TryGetValue(msgRcv.EntityId, out system))
                {
                    MsgBlockSettingsSend(msgRcv.SteamId, system);
                    system.State.ForceFullTransmit();
                    MsgBlockStateSend(msgRcv.SteamId, system);
                }
                else
                {
                    if (Logging.Instance.ShouldLog(Logging.Level.Error)) Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockDataRequestReceived for unknown system SteamId{0} EntityId={1}", msgRcv.SteamId, msgRcv.EntityId);
                }
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "ServerMsgBlockDataRequestReceived: {0}", ex.Message); }
        }

        private static void ServerMsgModCommandReceived(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgModCommand>(data);

                // "version" is informational and available to any player so version
                // mismatches between client and server can be diagnosed from the client.
                var cmdFirstWord = (msgRcv.Command ?? string.Empty).Trim();
                var spaceIdx = cmdFirstWord.IndexOf(' ');
                if (spaceIdx >= 0) cmdFirstWord = cmdFirstWord.Substring(0, spaceIdx);
                var isPublicCommand = cmdFirstWord == "version";

                // Validate admin using the sender parameter from SE API (tamper-proof)
                if (!isPublicCommand && !IsRemoteAdmin(sender))
                {
                    SendCommandResponse(sender, "Command requires admin permissions.", true, false, null, null);
                    return;
                }

                string responseMessage;
                bool isError;
                bool useMissionScreen;
                string screenTitle;
                string screenSubtitle;
                ChatHandler.ExecuteServerCommand(msgRcv.Command, sender, out responseMessage, out isError, out useMissionScreen, out screenTitle, out screenSubtitle);

                SendCommandResponse(sender, responseMessage, isError, useMissionScreen, screenTitle, screenSubtitle);
            }
            catch (Exception ex)
            {
                Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: ServerMsgModCommandReceived Exception:{0}", ex);
            }
        }

        // PERF-8: pooled scratch for IsRemoteAdmin. Server-only, main-thread call site,
        // never recurses into SendToAdmins, so a dedicated pool is safest (avoids any
        // risk of clobbering _adminBroadcastPlayers mid-iteration).
        private static readonly List<IMyPlayer> _adminCheckPlayers = new List<IMyPlayer>();

        private static bool IsRemoteAdmin(ulong steamId)
        {
            _adminCheckPlayers.Clear();
            MyAPIGateway.Players.GetPlayers(_adminCheckPlayers);
            try
            {
                for (var i = 0; i < _adminCheckPlayers.Count; i++)
                {
                    var player = _adminCheckPlayers[i];
                    if (player.SteamUserId == steamId)
                    {
                        // REF-4: compare MyPromoteLevel enum directly. Avoids the .ToString()
                        // allocation and is fragile-rename-proof.
                        var level = player.PromoteLevel;
                        return level == MyPromoteLevel.Admin
                            || level == MyPromoteLevel.SpaceMaster
                            || level == MyPromoteLevel.Owner;
                    }
                }
                return false;
            }
            finally
            {
                _adminCheckPlayers.Clear();
            }
        }

        #endregion Server Message Received Handlers

        #region Client Message Received Handlers

        private static void ClientMsgModSettingsReceived(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgModSettings>(data);
                SyncModSettings.AdjustSettings(msgRcv.Settings);
                // BUG-093: clamp on the broadcast path too (server-mutated settings may bypass Load).
                SyncModSettings.ValidateAndClamp(msgRcv.Settings);

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

        private static void ClientMsgModCommandResponseReceived(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgModCommandResponse>(data);

                if (msgRcv.UseMissionScreen)
                {
                    MyAPIGateway.Utilities.ShowMissionScreen(
                        msgRcv.ScreenTitle ?? "Nanobot Build and Repair System",
                        msgRcv.ScreenSubtitle ?? "",
                        "",
                        msgRcv.Message);
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Nanobars", msgRcv.Message);
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: ClientMsgModCommandResponseReceived Exception:{0}", ex);
            }
        }

        #endregion Client Message Received Handlers

        #region General Received

        private static void MsgBlockSettingsReceived(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgBlockSettings>(data);

                NanobotSystem system;
                if (Mod.NanobotSystems.TryGetValue(msgRcv.EntityId, out system))
                {
                    if (MyAPIGateway.Session.IsServer)
                    {
                        system.Settings.AssignReceived(msgRcv.Settings, system.BlockWeldPriority, system.BlockGrindPriority, system.ComponentCollectPriority);
                        system.SettingsChanged();
                        system.Settings.Save(system.Entity, Mod.ModGuid);
                        MsgBlockSettingsSend(0, system);
                    }
                    else if (!system._firstSettingsReceived)
                    {
                        system._firstSettingsReceived = true;
                        system.Settings.AssignReceived(msgRcv.Settings, system.BlockWeldPriority, system.BlockGrindPriority, system.ComponentCollectPriority);
                        system.OnFirstSettingsReceived();
                    }
                    else
                    {
                        system.Settings.AssignReceived(msgRcv.Settings, system.BlockWeldPriority, system.BlockGrindPriority, system.ComponentCollectPriority);
                        system.SettingsChanged();
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

        #endregion General Received

        #region Send Handlers

        private static void MsgModSettingsSend(ulong steamId)
        {
            try
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
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "MsgModSettingsSend: {0}", ex.Message); }
        }

        /// <summary>
        /// Broadcast current mod settings to all connected clients.
        /// Called after config set/reload/reset to push changes immediately.
        /// </summary>
        public static void BroadcastModSettings()
        {
            try
            {
                if (!MyAPIGateway.Session.IsServer || MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.MultiplayerActive)
                    return;

                var msgSnd = new MsgModSettings()
                {
                    Settings = Mod.Settings
                };

                MyAPIGateway.Multiplayer.SendMessageToOthers(MSGID_MOD_SETTINGS, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "BroadcastModSettings: {0}", ex.Message); }
        }

        /// <summary>
        /// Send a message to all admin-level players (Admin, SpaceMaster, Owner).
        /// Server-only. Reuses a single player list to avoid per-call allocation.
        /// </summary>
        private static readonly List<IMyPlayer> _adminBroadcastPlayers = new List<IMyPlayer>();
        private static void SendToAdmins(ushort msgId, byte[] bytes)
        {
            if (!MyAPIGateway.Session.IsServer || MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.MultiplayerActive)
                return;

            _adminBroadcastPlayers.Clear();
            MyAPIGateway.Players.GetPlayers(_adminBroadcastPlayers);
            foreach (var player in _adminBroadcastPlayers)
            {
                if (player.SteamUserId == 0) continue;
                var level = player.PromoteLevel.ToString();
                if (level == "Admin" || level == "SpaceMaster" || level == "Owner")
                {
                    MyAPIGateway.Multiplayer.SendMessageTo(msgId, bytes, player.SteamUserId, true);
                }
            }
        }

        /// <summary>
        /// Broadcast debug stats to admin clients only. Server-only, called periodically when DebugMode or profiling is active.
        /// </summary>
        public static void BroadcastDebugStatsToAdmins(MsgDebugStats stats)
        {
            try
            {
                SendToAdmins(MSGID_DEBUG_STATS_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(stats));
            }
            catch { }
        }

        private static void ClientMsgDebugStatsReceived(ushort channelId, byte[] bytes, ulong senderSteamId, bool isSenderServer)
        {
            try
            {
                if (!isSenderServer) return;
                var stats = MyAPIGateway.Utilities.SerializeFromBinary<MsgDebugStats>(bytes);
                if (stats != null) HudHandler.ReceivedStats = stats;
            }
            catch { }
        }

        /// <summary>
        /// Broadcast profile summary to admin clients. Server-only.
        /// </summary>
        public static void BroadcastProfileSummaryToAdmins(MsgProfileSummary summary)
        {
            try
            {
                SendToAdmins(MSGID_PROFILE_SUMMARY_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(summary));
            }
            catch { }
        }

        private static void ClientMsgProfileSummaryReceived(ushort channelId, byte[] bytes, ulong senderSteamId, bool isSenderServer)
        {
            try
            {
                if (!isSenderServer) return;
                var summary = MyAPIGateway.Utilities.SerializeFromBinary<MsgProfileSummary>(bytes);
                if (summary != null) HudHandler.ReceivedProfileSummary = summary;
            }
            catch { }
        }

        public static void MsgDataRequestSend()
        {
            try
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
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "MsgDataRequestSend: {0}", ex.Message); }
        }

        public static void MsgBlockSettingsSend(ulong steamId, NanobotSystem block)
        {
            try
            {
                if (block == null || block.Closed || block.MarkedForClose || block.Welder == null || block.Welder.Closed || block.Welder.MarkedForClose)
                    return;

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
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "MsgBlockSettingsSend: {0}", ex.Message); }
        }

        public static void MsgBlockStateSend(ulong steamId, NanobotSystem system)
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                if (!MyAPIGateway.Session.IsServer)
                    return;

                if (!MyAPIGateway.Multiplayer.MultiplayerActive)
                    return;

                var msgSnd = new MsgBlockState();
                msgSnd.EntityId = system.Entity.EntityId;
                msgSnd.State = system.State.GetTransmit();

                var bytes = MyAPIGateway.Utilities.SerializeToBinary(msgSnd);

                var res = false;
                if (steamId == 0)
                {
                    res = MyAPIGateway.Multiplayer.SendMessageToOthers(MSGID_BLOCK_STATE_FROM_SERVER, bytes, true);
                }
                else
                {
                    res = MyAPIGateway.Multiplayer.SendMessageTo(MSGID_BLOCK_STATE_FROM_SERVER, bytes, steamId, true);
                }

                if (!res && Logging.Instance.ShouldLog(Logging.Level.Error))
                {
                    Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemMod: SyncBlockStateSend Failed");
                }

                var payloadBytes = bytes.Length;
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("MsgBlockStateSend", profilerTs, () =>
                        string.Format("entityId={0};steamId={1};bytes={2}",
                            system.Entity.EntityId, steamId, payloadBytes));
                }
            }
            catch
            {
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("MsgBlockStateSend", profilerTs);
                }
            }
        }

        public static void MsgBlockDataRequestSend(NanobotSystem block)
        {
            try
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
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "MsgBlockDataRequestSend: {0}", ex.Message); }
        }

        public static void MsgModCommandSend(string command)
        {
            try
            {
                if (MyAPIGateway.Session.IsServer)
                    return;

                var msgSnd = new MsgModCommand();
                if (MyAPIGateway.Session.Player != null)
                    msgSnd.SteamId = MyAPIGateway.Session.Player.SteamUserId;
                else
                    msgSnd.SteamId = 0;

                msgSnd.Command = command;

                MyAPIGateway.Multiplayer.SendMessageToServer(MSGID_MOD_COMMAND_FROM_CLIENT, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "MsgModCommandSend: {0}", ex.Message); }
        }

        internal static void SendCommandResponse(ulong steamId, string message, bool isError, bool useMissionScreen, string screenTitle, string screenSubtitle)
        {
            try
            {
                var response = new MsgModCommandResponse
                {
                    Message = message,
                    IsError = isError,
                    UseMissionScreen = useMissionScreen,
                    ScreenTitle = screenTitle,
                    ScreenSubtitle = screenSubtitle
                };

                MyAPIGateway.Multiplayer.SendMessageTo(MSGID_MOD_COMMAND_RESPONSE_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(response), steamId, true);
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "SendCommandResponse: {0}", ex.Message); }
        }

        #endregion Send Handlers
    }
}