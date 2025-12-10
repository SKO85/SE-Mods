namespace SpaceEquipmentLtd.NanobotDrillSystem
{
   using System;
   using System.Collections.Generic;

   using VRage.Game;
   using VRage.Game.Components;
   using VRage.Game.ModAPI;
   using VRage.Voxels;
   using VRageMath;
   using Sandbox.ModAPI;
   using Sandbox.Game.Entities;
   using SpaceEquipmentLtd.Utils;


   static class Mod
   {
      public static Logging Log = new Logging("NanobotDrillSystem", 0, "NanobotDrillSystem.log", typeof(NanobotDrillSystemMod)) {
         LogLevel = Logging.Level.Error, //Default
         EnableHudNotification = false,
      };
      public static bool DisableLocalization = false;
   }

   /*Change log:
   * V 1.0.11:
   *     -Fix: Fixed issue with ResourceSink/Power after Heavy Industrie Update
   * V 1.0.10:
   *     -Fix: Edit priority list while multiple block are selected
   * V 1.0.9:
   *     -Fix: Reverted 1.0.8
   * V 1.0.8:
   *     -Fix: Added internal TransportInventory to Block componentes list (ownership)
   * V 1.0.7:
   *     -New: Works now with API changes from public test
   * V 1.0.6:
   *     -Fix: Now mining also planetary surface boulders
   *     -Fix: Possible reason for drill stop working unexpected
   *     -Fix: Wrong placed side mointpoints
   * V 1.0.5:
   *     -Imp: New russian tranlation thanks to KesorAv
   *     -Fix: Remaining fragments inside drill area on client
   *     -Fix: Transport animation on client
   *     -Imp: Controlled by Player: if player dies or leaves the game, it behaves now like player has no drill equipped, instead switching to not controled.
   * V 1.0.4:
   *     -New: Localization
   * V 1.0.2:
   *     -Fix: Sometimes while drilling ore appearing somewhere inside drill area
   *           Fixed Debug Trace message removed
   * V 1.0.0:
   *     -New: Initial release.
   */
   [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
   public class NanobotDrillSystemMod : MySessionComponentBase
   {
      private const string Version = "V1.0.11 2021-08-31"
      ;

      private const string CmdKey = "/nanodrills";
      private const string CmdHelp1 = "-?";
      private const string CmdHelp2 = "-help";
      private const string CmdCwsf = "-cwsf";
      private const string CmdCpsf = "-cpsf";
      private const string CmdLogLevel = "-loglevel";
      private const string CmdWritePerfCounter = "-writeperf";
      private const string CmdLogLevel_All = "all";
      private const string CmdLogLevel_Default = "default";

      private static ushort MSGID_MOD_DATAREQUEST = 40200;
      private static ushort MSGID_MOD_SETTINGS = 40201;
      private static ushort MSGID_MOD_COMMAND = 40210;
      private static ushort MSGID_BLOCK_DATAREQUEST = 40300;
      private static ushort MSGID_BLOCK_SETTINGS_FROM_SERVER = 40302;
      private static ushort MSGID_BLOCK_SETTINGS_FROM_CLIENT = 40303;
      private static ushort MSGID_BLOCK_STATE_FROM_SERVER = 40304;
      private static ushort MSGID_BLOCK_VOXELBOXES_FILL_REMOVE_FROM_SERVER = 40310;

      private bool _Init = false;
      public static bool SettingsValid = false;
      public static SyncModSettings Settings = new SyncModSettings();
      private static TimeSpan _LastSourcesAndTargetsUpdateTimer;
      private static TimeSpan _LastSyncModDataRequestSend;
      private static TimeSpan _LastSyncModvoxelBoxesSend;
      private static TimeSpan SourcesAndTargetsUpdateTimerInterval = new TimeSpan(0, 0, 2);

      public static Guid ModGuid = new Guid("CD2D351C-44BF-4BC1-93C1-D986BFAC58D3");
      public const int MaxBackgroundTasks_Default = 4;
      public const int MaxBackgroundTasks_Max = 10;
      public const int MaxBackgroundTasks_Min = 1;

      public static List<Action> AsynActions = new List<Action>();
      private static int ActualBackgroundTaskCount = 0;

      private static MsgVoxelBoxesFillRemove _MsgVoxelBoxesFillRemove = new MsgVoxelBoxesFillRemove() { VoxelBoxesData = new List<SyncTargetVoxelBoxesRemoveFillData>()  };
      private static MyStorageData _VoxelDataCacheFillRemove;

      /// <summary>
      /// Current known Build and Repair Systems in world
      /// </summary>
      private static Dictionary<long, NanobotDrillSystemBlock> _DrillSystems;
      public static Dictionary<long, NanobotDrillSystemBlock> DrillSystems
      {
         get
         {
            return _DrillSystems != null ? _DrillSystems : _DrillSystems = new Dictionary<long, NanobotDrillSystemBlock>();
         }
      }


      /// <summary>
      /// 
      /// </summary>
      public void Init()
      {
         Mod.Log.Write("DrillSystemMod: Initializing IsServer={0}, IsDedicated={1}", MyAPIGateway.Session.IsServer, MyAPIGateway.Utilities.IsDedicated);
         _Init = true;

         Settings = SyncModSettings.Load();
         Mod.DisableLocalization = Settings.DisableLocalization;
         SettingsValid = MyAPIGateway.Session.IsServer;
         Mod.Log.LogLevel = Settings.LogLevel;
         SettingsChanged();

         MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, BeforeDamageHandlerNoDamageByDrillSystem);
         if (MyAPIGateway.Session.IsServer)
         {
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_MOD_COMMAND, SyncModCommandReceived);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_MOD_DATAREQUEST, SyncModDataRequestReceived);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_DATAREQUEST, SyncBlockDataRequestReceived);
            //Same as MSGID_BLOCK_SETTINGS but SendMessageToOthers sends also to self, which will result in stack overflow
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_SETTINGS_FROM_CLIENT, SyncBlockSettingsReceived);
         }
         else
         {
            //Keep this for compatibility between new client and old server, remove this in next version after server admins had enough time to update their servers->
            MyAPIGateway.Multiplayer.RegisterMessageHandler(MSGID_MOD_SETTINGS, SyncModSettingsReceivedDeprecated);
            MyAPIGateway.Multiplayer.RegisterMessageHandler(MSGID_BLOCK_SETTINGS_FROM_SERVER, SyncBlockSettingsReceivedDeprecated);
            MyAPIGateway.Multiplayer.RegisterMessageHandler(MSGID_BLOCK_STATE_FROM_SERVER, SyncBlockStateReceivedDeprecated);
            MyAPIGateway.Multiplayer.RegisterMessageHandler(MSGID_BLOCK_VOXELBOXES_FILL_REMOVE_FROM_SERVER, SyncBlockVoxelBoxesFillRemoveReceivedDeprecated);
            //<-

            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_MOD_SETTINGS, SyncModSettingsReceived);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_SETTINGS_FROM_SERVER, SyncBlockSettingsReceived);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_STATE_FROM_SERVER, SyncBlockStateReceived);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MSGID_BLOCK_VOXELBOXES_FILL_REMOVE_FROM_SERVER, SyncBlockVoxelBoxesFillRemoveReceived);

            SyncModDataRequestSend();
            _VoxelDataCacheFillRemove = new MyStorageData(MyStorageDataTypeFlags.ContentAndMaterial);
            _VoxelDataCacheFillRemove.Resize(new Vector3I(NanobotDrillSystemBlock.VOXEL_CACHE_SIZE, NanobotDrillSystemBlock.VOXEL_CACHE_SIZE, NanobotDrillSystemBlock.VOXEL_CACHE_SIZE)); //Size of search cube
         }
         MyAPIGateway.Utilities.MessageEntered += Utilities_MessageEntered;

         Mod.Log.Write("DrillSystemMod: Initialized. Version {0}", Version);
      }

      /// <summary>
      /// 
      /// </summary>
      private static void SettingsChanged()
      {
         foreach (var entry in DrillSystems)
         {
            entry.Value.SettingsChanged();
         }
         InitControls();
      }

      /// <summary>
      /// 
      /// </summary>
      public static void InitControls()
      {
         //Call also on dedicated else the properties for the scripting interface are not initialized
         if (SettingsValid && !NanobotDrillSystemTerminal.CustomControlsInit && DrillSystems.Count > 0) NanobotDrillSystemTerminal.InitializeControls();
      }

      /// <summary>
      /// 
      /// </summary>
      private void Utilities_MessageEntered(string messageText, ref bool sendToOthers)
      {
         if (string.IsNullOrEmpty(messageText)) return;
         var cmd = messageText.ToLower();
         if (cmd.StartsWith(CmdKey))
         {
            if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemMod: Cmd: {0}", messageText);
            var args = cmd.Remove(0, CmdKey.Length).Trim().Split(' ');
            if (args.Length > 0)
            {
               if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemMod: Cmd args[0]: {0}", args[0]);
               switch (args[0].Trim())
               {
                  case CmdCpsf:
                     if (MyAPIGateway.Session.IsServer)
                     {
                        SyncModSettings.Save(Settings, false);
                        MyAPIGateway.Utilities.ShowMessage(CmdKey, "Settings file created inside mod folder");
                     }
                     else
                     {
                        MyAPIGateway.Utilities.ShowMessage(CmdKey, "command not allowed on client");
                     }
                     break;
                  case CmdCwsf:
                     if (MyAPIGateway.Session.IsServer)
                     {
                        SyncModSettings.Save(Settings, true);
                        MyAPIGateway.Utilities.ShowMessage(CmdKey, "Settings file created inside world folder");
                     }
                     else
                     {
                        MyAPIGateway.Utilities.ShowMessage(CmdKey, "command not allowed on client");
                     }
                     break;
                  case CmdLogLevel:
                     if (args.Length > 1)
                     {
                        switch (args[1].Trim())
                        {
                           case CmdLogLevel_All:
                              Mod.Log.LogLevel = Logging.Level.All;
                              MyAPIGateway.Utilities.ShowMessage(CmdKey, string.Format("Logging level switched to All [{0:X}]", Mod.Log.LogLevel));
                              Mod.Log.Write("BuildAndRepairSystemMod: Logging level switched to [{0:X}]", Mod.Log.LogLevel);
                              foreach (var modItem in MyAPIGateway.Session.Mods)
                              {
                                 Mod.Log.Write("BuildAndRepairSystemMod: Mod {0} (Name={1}, FileId={2}, Path={3})", modItem.FriendlyName, modItem.Name, modItem.PublishedFileId, modItem.GetPath());
                              }
                              break;
                           case CmdLogLevel_Default:
                           default:
                              Mod.Log.LogLevel = Settings.LogLevel;
                              MyAPIGateway.Utilities.ShowMessage(CmdKey, string.Format("Logging level switched to Default [{0:X}]", Mod.Log.LogLevel));
                              Mod.Log.Write("BuildAndRepairSystemMod: Logging level switched to [{0:X}]", Mod.Log.LogLevel);                              
                              break;
                        }
                     }
                     break;
                  case CmdWritePerfCounter:
                     break;
                  case CmdHelp1:
                  case CmdHelp2:
                  default:
                     MyAPIGateway.Utilities.ShowMissionScreen("NanobotDrillSystem", "Help", "", GetHelpText());
                     break;
               }
            }
            else
            {
               MyAPIGateway.Utilities.ShowMissionScreen("NanobotDrillSystem", "Help", "", GetHelpText());
            }
            sendToOthers = false;
         }
      }

      private string GetHelpText()
      {
         var text = string.Format(Texts.Cmd_HelpClient.String, Version, CmdHelp1, CmdHelp2, CmdLogLevel, CmdLogLevel_All, CmdLogLevel_Default);
         if (MyAPIGateway.Session.IsServer) text += string.Format(Texts.Cmd_HelpServer.String, CmdCwsf, CmdCpsf);
         return text;
   }

      /// <summary>
      /// 
      /// </summary>
      protected override void UnloadData()
      {
         while (true)
         {
            int actualBackgroundTaskCount;
            lock (AsynActions)
            {
               actualBackgroundTaskCount = ActualBackgroundTaskCount;
            }
            if (actualBackgroundTaskCount <= 0) break;
         }

         _Init = false;
         try
         {
            if (MyAPIGateway.Utilities != null && MyAPIGateway.Multiplayer != null)
            {
               if (MyAPIGateway.Session.IsServer)
               {
                  MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_MOD_DATAREQUEST, SyncModDataRequestReceived);
                  MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_BLOCK_DATAREQUEST, SyncBlockDataRequestReceived);
                  MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_BLOCK_SETTINGS_FROM_CLIENT, SyncBlockSettingsReceived);
               }
               else
               {
                  //Keep this for compatibility between new client and old server, remove this in next version after server admins had enough time to update their servers->
                  MyAPIGateway.Multiplayer.UnregisterMessageHandler(MSGID_MOD_SETTINGS, SyncModSettingsReceivedDeprecated);
                  MyAPIGateway.Multiplayer.UnregisterMessageHandler(MSGID_BLOCK_SETTINGS_FROM_SERVER, SyncBlockSettingsReceivedDeprecated);
                  MyAPIGateway.Multiplayer.UnregisterMessageHandler(MSGID_BLOCK_STATE_FROM_SERVER, SyncBlockStateReceivedDeprecated);
                  MyAPIGateway.Multiplayer.UnregisterMessageHandler(MSGID_BLOCK_VOXELBOXES_FILL_REMOVE_FROM_SERVER, SyncBlockVoxelBoxesFillRemoveReceivedDeprecated);
                  //<-

                  MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_MOD_SETTINGS, SyncModSettingsReceived);
                  MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_BLOCK_SETTINGS_FROM_SERVER, SyncBlockSettingsReceived);
                  MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_BLOCK_STATE_FROM_SERVER, SyncBlockStateReceived);
                  MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MSGID_BLOCK_VOXELBOXES_FILL_REMOVE_FROM_SERVER, SyncBlockVoxelBoxesFillRemoveReceived);
               }
            }
            Mod.Log.Write("DrillSystemMod: UnloadData.");
            Mod.Log.Close();
         }
         catch (Exception e)
         {
            Mod.Log.Error("NanobotDrillSystemMod.UnloadData: {0}", e.ToString());
         }
         base.UnloadData();
      }

      /// <summary>
      /// 
      /// </summary>
      public override void UpdateBeforeSimulation()
      {
         try
         {
            if (!_Init)
            {
               if (MyAPIGateway.Session == null) return;
               Init();
            }
            else
            {
               if (MyAPIGateway.Session.IsServer)
               {
                  RebuildSourcesAndTargetsTimer();
                  if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastSyncModvoxelBoxesSend) >= TimeSpan.FromSeconds(1))
                  {
                     SyncBlockVoxelBoxesFillRemoveSend();
                     _LastSyncModvoxelBoxesSend = MyAPIGateway.Session.ElapsedPlayTime;
                  }
               }
               else if (!SettingsValid)
               {
                  if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastSyncModDataRequestSend) >= TimeSpan.FromSeconds(10))
                  {
                     SyncModDataRequestSend();
                     _LastSyncModDataRequestSend = MyAPIGateway.Session.ElapsedPlayTime;
                  }
               }
            }
         }
         catch (Exception e)
         {
            Mod.Log.Error(e);
         }
      }

      /// <summary>
      /// Damage Handler: Prevent Damage from DrillSystem
      /// </summary>
      public void BeforeDamageHandlerNoDamageByDrillSystem(object target, ref MyDamageInformation info)
      {
         try
         {
            if (info.Type == MyDamageType.Drill)
            {
               if (target is IMyCharacter)
               {
                  var logicalComponent = DrillSystems.GetValueOrDefault(info.AttackerId);
                  if (logicalComponent != null)
                  {
                     var terminalBlock = logicalComponent.Entity as IMyTerminalBlock;
                     if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: Prevent Damage from DrillSystem={0} Amount={1}", terminalBlock != null ? terminalBlock.CustomName : logicalComponent.Entity.DisplayName, info.Amount);
                     info.Amount = 0;
                  }
               }
            }
         }
         catch (Exception e)
         {
            Mod.Log.Error("DrillSystemMod: Exception in BeforeDamageHandlerNoDamageByDrillSystem: Source={0}, Message={1}", e.Source, e.Message);
         }
      }

      /// <summary>
      /// Rebuild the list of targets and inventory sources
      /// </summary>
      protected void RebuildSourcesAndTargetsTimer()
      {
         if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastSourcesAndTargetsUpdateTimer) > SourcesAndTargetsUpdateTimerInterval)
         {
            foreach (var drillSystem in DrillSystems.Values)
            {
               drillSystem.UpdateSourcesAndTargetsTimer();
            }
            _LastSourcesAndTargetsUpdateTimer = MyAPIGateway.Session.ElapsedPlayTime;
         }
      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="newAction"></param>
      public static void AddAsyncAction(Action newAction)
      {
         lock (AsynActions)
         {
            AsynActions.Add(newAction);
            if (ActualBackgroundTaskCount < Settings.MaxBackgroundTasks)
            {
               ActualBackgroundTaskCount++;
               if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemMod: AddAsyncAction Start Task {0} of max {1}", ActualBackgroundTaskCount, Settings.MaxBackgroundTasks);
               MyAPIGateway.Parallel.StartBackground(() =>
               {
                  try
                  {
                     while (true)
                     {
                        Action pendingAction = null;
                        lock (AsynActions)
                        {
                           if (AsynActions.Count > 0)
                           {
                              pendingAction = AsynActions[0];
                              AsynActions.RemoveAt(0);
                           }
                           if (pendingAction == null)
                           {
                              ActualBackgroundTaskCount--;
                              break;
                           }
                        }
                        if (pendingAction != null)
                        {
                           try
                           {
                              if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemMod: AddAsyncAction Task Working start.");
                              pendingAction();
                              if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemMod: AddAsyncAction Task Working finished.");
                           }
                           catch { };
                        }
                     }
                     if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemMod: AddAsyncAction Task ended. Still running {0} of max {1}", ActualBackgroundTaskCount, Settings.MaxBackgroundTasks);
                  }
                  catch {
                     lock (AsynActions)
                     {
                        ActualBackgroundTaskCount--;
                     }
                  }
               });
            }
         }
      }

      /// <summary>
      /// 
      /// </summary>
      private void SyncModCommandSend(string command)
      {
      }

      /// <summary>
      /// Even if the info senderId has now the same info as msgRcv.SteamId, i've to keep the message format for compatibility
      /// </summary>
      private void SyncModCommandReceived(ushort msgid, byte[] dataRcv, ulong senderId, bool fromServer)
      {
      }

      /// <summary>
      /// 
      /// </summary>
      private void SyncModDataRequestSend()
      {
         if (MyAPIGateway.Session.IsServer) return;

         var msgSnd = new MsgModDataRequest();
         if (MyAPIGateway.Session.Player != null)
            msgSnd.SteamId = MyAPIGateway.Session.Player.SteamUserId;
         else
            msgSnd.SteamId = 0;

         if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncModDataRequestSend SteamId={0}", msgSnd.SteamId);
         MyAPIGateway.Multiplayer.SendMessageToServer(MSGID_MOD_DATAREQUEST, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
      }

      /// <summary>
      /// Even if the info senderId has now the same info as msgRcv.SteamId, i've to keep the message format for compatibility
      /// </summary>
      private void SyncModDataRequestReceived(ushort msgid, byte[] dataRcv, ulong senderId, bool fromServer)
      {
         var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgModDataRequest>(dataRcv);
         if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncModDataRequestReceived SteamId={0}", msgRcv.SteamId);
         SyncModSettingsSend(msgRcv.SteamId);
      }

      /// <summary>
      /// 
      /// </summary>
      private void SyncModSettingsSend(ulong steamId)
      {
         if (!MyAPIGateway.Session.IsServer) return;
         var msgSnd = new MsgModSettings();
         msgSnd.Settings = Settings;
         if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncModSettingsSend SteamId={0}", steamId);
         if (!MyAPIGateway.Multiplayer.SendMessageTo(MSGID_MOD_SETTINGS, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), steamId, true))
         {
            if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: SyncModSettingsSend failed");
         }
      }

      /// <summary>
      /// 
      /// </summary>
      private void SyncModSettingsReceivedDeprecated(byte[] dataRcv)
      {
         SyncModSettingsReceived(MSGID_MOD_SETTINGS, dataRcv, 0, true);
      }
      private void SyncModSettingsReceived(ushort msgid, byte[] dataRcv, ulong senderId, bool fromServer)
      {
         try
         {
            if (!fromServer)
            {
               Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: SyncModSettingsReceived ignored! As not marked as fromServer: SenderId={0}", senderId);
               return;
            }

            var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgModSettings>(dataRcv);
            Settings = msgRcv.Settings;
            SettingsValid = true;
            if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncModSettingsReceived");
            SettingsChanged();
         }
         catch (Exception ex)
         {
            Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: SyncModSettingsReceived Exception:{0}", ex);
         }
      }

      /// <summary>
      /// 
      /// </summary>
      public static void SyncBlockDataRequestSend(NanobotDrillSystemBlock block)
      {
         if (MyAPIGateway.Session.IsServer) return;

         var msgSnd = new MsgBlockDataRequest();
         if (MyAPIGateway.Session.Player != null)
            msgSnd.SteamId = MyAPIGateway.Session.Player.SteamUserId;
         else
            msgSnd.SteamId = 0;
         msgSnd.EntityId = block.Entity.EntityId;

         if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncBlockDataRequestSend SteamId={0} EntityId={1}/{2}", msgSnd.SteamId, msgSnd.EntityId, Logging.BlockName(block.Entity, Logging.BlockNameOptions.None));
         MyAPIGateway.Multiplayer.SendMessageToServer(MSGID_BLOCK_DATAREQUEST, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
      }

      /// <summary>
      /// 
      /// </summary>
      private void SyncBlockDataRequestReceived(ushort msgid, byte[] dataRcv, ulong senderId, bool fromServer)
      {

         var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgBlockDataRequest>(dataRcv);

         NanobotDrillSystemBlock system;
         if (DrillSystems.TryGetValue(msgRcv.EntityId, out system))
         {
            if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncBlockDataRequestReceived SteamId={0} EntityId={1}/{2}", msgRcv.SteamId, msgRcv.EntityId, Logging.BlockName(system.Entity, Logging.BlockNameOptions.None));
            SyncBlockSettingsSend(msgRcv.SteamId, system);
            SyncBlockStateSend(msgRcv.SteamId, system);
         }
         else
         {
            if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: SyncBlockDataRequestReceived for unknown system SteamId{0} EntityId={1}", msgRcv.SteamId, msgRcv.EntityId);
         }
      }

      /// <summary>
      /// 
      /// </summary>
      public static void SyncBlockSettingsSend(ulong steamId, NanobotDrillSystemBlock block)
      {

         var msgSnd = new MsgBlockSettings();
         msgSnd.EntityId = block.Entity.EntityId;
         msgSnd.Settings = block.Settings.GetTransmit();

         var res = false;
         if (MyAPIGateway.Session.IsServer)
         {
            if (steamId == 0)
            {
               if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncBlockSettingsSend To Others EntityId={0}/{1}", block.Entity.EntityId, Logging.BlockName(block.Entity, Logging.BlockNameOptions.None));
               res = MyAPIGateway.Multiplayer.SendMessageToOthers(MSGID_BLOCK_SETTINGS_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
            }
            else
            {
               if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncBlockSettingsSend To SteamId={2} EntityId={0}/{1}", block.Entity.EntityId, Logging.BlockName(block.Entity, Logging.BlockNameOptions.None), steamId);
               res = MyAPIGateway.Multiplayer.SendMessageTo(MSGID_BLOCK_SETTINGS_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), steamId, true);
            }
         }
         else
         {
            if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncBlockSettingsSend To Server EntityId={0}/{1} to Server", block.Entity.EntityId, Logging.BlockName(block.Entity, Logging.BlockNameOptions.None));
            res = MyAPIGateway.Multiplayer.SendMessageToServer(MSGID_BLOCK_SETTINGS_FROM_CLIENT, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
         }
         if (!res && Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: SyncBlockSettingsSend failed", Logging.BlockName(block.Entity, Logging.BlockNameOptions.None));
      }

      /// <summary>
      /// 
      /// </summary>
      private void SyncBlockSettingsReceivedDeprecated(byte[] dataRcv)
      {
         SyncBlockSettingsReceived(MSGID_BLOCK_SETTINGS_FROM_SERVER, dataRcv, 0, true);
      }
      private void SyncBlockSettingsReceived(ushort msgid, byte[] dataRcv, ulong senderId, bool fromServer)
      {
         try
         {
            var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgBlockSettings>(dataRcv);

            NanobotDrillSystemBlock system;
            if (DrillSystems.TryGetValue(msgRcv.EntityId, out system))
            {
               if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncBlockSettingsReceived EntityId={0}/{1}", msgRcv.EntityId, Logging.BlockName(system.Entity, Logging.BlockNameOptions.None));
               system.Settings.AssignReceived(msgRcv.Settings, system.DrillPriority, system.ComponentCollectPriority);
               system.SettingsChanged();
               if (MyAPIGateway.Session.IsServer)
               {
                  SyncBlockSettingsSend(0, system);
               }
            } else
            {
               if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: SyncBlockSettingsReceived for unknown system EntityId={0}", msgRcv.EntityId);
            }
         }
         catch (Exception ex)
         {
            Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: SyncBlockSettingsReceived Exception:{0}", ex);
         }
      }

      /// <summary>
      /// 
      /// </summary>
      public static void SyncBlockStateSend(ulong steamId, NanobotDrillSystemBlock system)
      {
         if (!MyAPIGateway.Session.IsServer) return;
         if (!MyAPIGateway.Multiplayer.MultiplayerActive) return;


         var msgSnd = new MsgBlockState();
         msgSnd.EntityId = system.Entity.EntityId;
         msgSnd.State = system.State.GetTransmit();

         var res = false;
         if (steamId == 0)
         {
            if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncBlockStateSend to others EntityId={0}/{1}, State={2}", system.Entity.EntityId, Logging.BlockName(system.Entity, Logging.BlockNameOptions.None), msgSnd.State.ToString());
            res = MyAPIGateway.Multiplayer.SendMessageToOthers(MSGID_BLOCK_STATE_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), true);
         }
         else
         {
            if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncBlockStateSend to SteamId={0} EntityId={1}/{2}, State={3}", steamId, system.Entity.EntityId, Logging.BlockName(system.Entity, Logging.BlockNameOptions.None), msgSnd.State.ToString());
            res = MyAPIGateway.Multiplayer.SendMessageTo(MSGID_BLOCK_STATE_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(msgSnd), steamId, true);
         }

         if (!res && Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: SyncBlockStateSend Failed");
      }

      /// <summary>
      /// 
      /// </summary>
      private void SyncBlockStateReceivedDeprecated(byte[] dataRcv)
      {
         SyncBlockStateReceived(MSGID_BLOCK_STATE_FROM_SERVER, dataRcv, 0, true);
      }
      private void SyncBlockStateReceived(ushort msgid, byte[] dataRcv, ulong senderId, bool fromServer)
      {
         try
         {
            var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgBlockState>(dataRcv);

            NanobotDrillSystemBlock system;
            if (DrillSystems.TryGetValue(msgRcv.EntityId, out system))
            {
               if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncBlockStateReceived EntityId={0}/{1}, State={2}", system.Entity.EntityId, Logging.BlockName(system.Entity, Logging.BlockNameOptions.None), msgRcv.State.ToString());
               system.State.AssignReceived(msgRcv.State);
            }
            else
            {
               if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: SyncBlockStateReceived for unknown system EntityId={0}", msgRcv.EntityId);
            }
         }
         catch (Exception ex)
         {
            Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: SyncBlockStateReceived Exception:{0})", ex);
         }
      }

      public static void SyncBlockVoxelBoxesFillRemoveAddQueue(MyVoxelBase voxelBase, Vector3I voxelCoordMin, Vector3I voxelCoordMax, List<BoundingBoxI> boxes, byte material, long content)
      {
         lock(_MsgVoxelBoxesFillRemove)
         {
            var syncId = SyncEntityId.GetSyncId(voxelBase);
            foreach(var voxelBoxes in _MsgVoxelBoxesFillRemove.VoxelBoxesData)
            {
               if (voxelBoxes.Entity == syncId && voxelBoxes.CoordMin == voxelCoordMin && voxelBoxes.CoordMax == voxelCoordMax && voxelBoxes.Material == material && Math.Sign(voxelBoxes.Content)==Math.Sign(content))
               {
                  voxelBoxes.Boxes.AddRange(boxes);
                  voxelBoxes.Content += content;
                  return;
               }
            }
            //New VoxelBoxes
            var voxelBoxesNew = new SyncTargetVoxelBoxesRemoveFillData();
            voxelBoxesNew.Entity = syncId;
            voxelBoxesNew.Boxes = new List<BoundingBoxI>();
            voxelBoxesNew.Boxes.AddRange(boxes);
            voxelBoxesNew.CoordMin = voxelCoordMin;
            voxelBoxesNew.CoordMax = voxelCoordMax;
            voxelBoxesNew.Material = material;
            voxelBoxesNew.Content = content;
            _MsgVoxelBoxesFillRemove.VoxelBoxesData.Add(voxelBoxesNew);
         }
      }

      /// <summary>
      /// 
      /// </summary>
      public static void SyncBlockVoxelBoxesFillRemoveSend()
      {
         if (!MyAPIGateway.Session.IsServer) return;
         if (!MyAPIGateway.Multiplayer.MultiplayerActive) return;

         var res = false;
         lock(_MsgVoxelBoxesFillRemove)
         {
            if (_MsgVoxelBoxesFillRemove.VoxelBoxesData.Count > 0)
            {
               if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncBlockVoxelBoxesFillRemoveSend to others boxes={0}", _MsgVoxelBoxesFillRemove.VoxelBoxesData.Count);
               res = MyAPIGateway.Multiplayer.SendMessageToOthers(MSGID_BLOCK_VOXELBOXES_FILL_REMOVE_FROM_SERVER, MyAPIGateway.Utilities.SerializeToBinary(_MsgVoxelBoxesFillRemove), true);
               _MsgVoxelBoxesFillRemove.VoxelBoxesData.Clear();
               if (!res && Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: SyncBlockVoxelBoxesFillRemoveSend Failed");
            }
         }
      }

      /// <summary>
      /// 
      /// </summary>
      private void SyncBlockVoxelBoxesFillRemoveReceivedDeprecated(byte[] dataRcv)
      {
         SyncBlockVoxelBoxesFillRemoveReceived(MSGID_BLOCK_VOXELBOXES_FILL_REMOVE_FROM_SERVER, dataRcv, 0, true);
      }
      private void SyncBlockVoxelBoxesFillRemoveReceived(ushort msgid, byte[] dataRcv, ulong senderId, bool fromServer)
      {
         try
         {
            if (MyAPIGateway.Session.IsServer || !fromServer) return;
            var msgRcv = MyAPIGateway.Utilities.SerializeFromBinary<MsgVoxelBoxesFillRemove>(dataRcv);

            if (Mod.Log.ShouldLog(Logging.Level.Communication)) Mod.Log.Write(Logging.Level.Communication, "DrillSystemMod: SyncBlockVoxelBoxesFillRemoveReceived Length={0} Count={1}", dataRcv.Length, msgRcv.VoxelBoxesData.Count);
            MyAPIGateway.Parallel.Start(() =>
            {
               foreach (var voxelBoxes in msgRcv.VoxelBoxesData)
               {
                  try
                  {
                     var voxelBase = SyncEntityId.GetItemAs<MyVoxelBase>(voxelBoxes.Entity);
                     if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemMod: VoxelBoxesFillRemove {0} Count={1}", Logging.BlockName(voxelBase), voxelBoxes.Boxes.Count);
                     UtilsVoxels.VoxelChangeContent(voxelBase, _VoxelDataCacheFillRemove, voxelBoxes.Boxes, voxelBoxes.CoordMin, voxelBoxes.CoordMax, voxelBoxes.Material, voxelBoxes.Content);
                  }
                  catch (Exception ex)
                  {
                     if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: VoxelBoxesFillRemove Exception:{0}", ex);
                  }
               }
            });               
         }
         catch (Exception ex)
         {
            Mod.Log.Write(Logging.Level.Error, "DrillSystemMod: SyncBlockVoxelBoxesFillRemoveReceived Exception:{0}", ex);
         }
      }

   }
}
