using Sandbox.ModAPI;
using VRage.Game.Components;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class NanobotBuildAndRepairSystemMod : MySessionComponentBase
    {
        public static readonly Guid ModGuid = new Guid("8B57046C-DA20-4DE1-8E35-513FD21E3B9F");
        public static SyncModSettings Settings { get; set; } = new SyncModSettings();
        public static bool SettingsValid { get; set; } = false;
        public static readonly Dictionary<long, NanobotBuildAndRepairSystemBlock> BuildAndRepairSystems = new Dictionary<long, NanobotBuildAndRepairSystemBlock>();

        private bool _initialized = false;
        private static readonly TimeSpan SourcesAndTargetsUpdateTimerInterval = TimeSpan.FromSeconds(2);
        private static TimeSpan _lastSourcesAndTargetsUpdateTimer;
        private static TimeSpan _lastSyncModDataRequestSend;
        internal static int TotalPCU = 0;

        public override void UpdateBeforeSimulation()
        {
            if (!_initialized)
            {
                if (MyAPIGateway.Session == null)
                    return;

                Init();
            }
            else
            {
                if (MyAPIGateway.Session.IsServer)
                {
                    UpdateSourcesAndTargets();
                }
                else if (!SettingsValid)
                {
                    TrySyncSettings();
                }
                
            }
        }

        protected override void UnloadData()
        {
            AsyncTaskQueue.Clear();
            MessageSyncHelper.UnregisterAll();
            Logging.Instance?.Close();
            NanobotBuildAndRepairSystemTerminal.Dispose();
            base.UnloadData();

            if (_chatHandlerRegistered)
            {
                MyAPIGateway.Utilities.MessageEntered -= CommandProcessor.OnChatCommand;
                _chatHandlerRegistered = false;
            }
        }

        private static bool _chatHandlerRegistered = false;

        private void Init()
        {
            Logging.Instance?.Write("NanobotBuildAndRepairSystemMod: Initializing");

            _initialized = true;

            Settings = SyncModSettings.Load();
            SettingsValid = MyAPIGateway.Session.IsServer;

            if (MyAPIGateway.Session.IsServer)
            {
                Logging.Instance.LogLevel = Settings.LogLevel;
                ApplySettingsToSystems();
                TerminalControlManager.InitControls();
            }

            DamageHandler.Register();
            MessageSyncHelper.RegisterAll();


            if (!_chatHandlerRegistered)
            {
                MyAPIGateway.Utilities.MessageEntered += CommandProcessor.OnChatCommand;
                _chatHandlerRegistered = true;
            }

            Logging.Instance?.Write("NanobotBuildAndRepairSystemMod: Initialized");
        }

        private void UpdateSourcesAndTargets()
        {
            var now = MyAPIGateway.Session.ElapsedPlayTime;

            if (now - _lastSourcesAndTargetsUpdateTimer > SourcesAndTargetsUpdateTimerInterval)
            {
                // Get all grids and get total PCU
                //var gridBlocks = UtilsCache.GetOrAdd("All-Grids", 30, () =>
                //{
                //    var grids = GetGrids();
                //    var blocks = new List<IMySlimBlock>();
                //    foreach (var grid in grids)
                //    {
                //        var tmpBlocks = new List<IMySlimBlock>();
                //        grid.GetBlocks(tmpBlocks);
                //        blocks.AddRange(tmpBlocks);
                //    }

                //    return blocks;
                //});

                //TotalPCU = 0;
                //foreach(var item in gridBlocks)
                //{
                //    var nonFunctional = item.FatBlock != null && !item.FatBlock.IsFunctional;
                //    var pcu = nonFunctional ? 1 : ((MyCubeBlockDefinition)item.BlockDefinition).PCU;

                //    TotalPCU += pcu;
                //}

                _lastSourcesAndTargetsUpdateTimer = now;
                foreach (var system in BuildAndRepairSystems.Values)
                {
                    AsyncTaskQueue.Enqueue(() => system.StartAsyncUpdateSourcesAndTargets(true));
                }
            }
        }

        public List<IMyCubeGrid> GetGrids()
        {
            var items = new List<IMyCubeGrid>();
            MyAPIGateway.Entities.GetEntities(null, (entity) =>
            {
                var grid = entity as IMyCubeGrid;
                if(grid != null)
                {
                    items.Add(grid);
                }

                return false;
            });

            return items;
        }

        private void TrySyncSettings()
        {
            var now = MyAPIGateway.Session.ElapsedPlayTime;
            if (now - _lastSyncModDataRequestSend > TimeSpan.FromSeconds(10))
            {
                _lastSyncModDataRequestSend = now;
                MessageSyncHelper.SyncModDataRequestSend();
            }
        }

        internal static void ApplySettingsToSystems()
        {
            foreach (var system in BuildAndRepairSystems.Values)
            {
                system.SettingsChanged();
            }

            if (SettingsValid)
            {
                TerminalControlManager.InitControls();
            }
        }

    }
}
