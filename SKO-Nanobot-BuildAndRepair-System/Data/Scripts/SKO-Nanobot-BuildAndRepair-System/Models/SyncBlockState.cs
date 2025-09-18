using ProtoBuf;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Collections;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Models
{
    /// <summary>
    /// Current State of block
    /// </summary>
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class SyncBlockState
    {
        public const int MaxSyncItems = 20;
        private bool _Ready;
        private bool _Welding;
        private bool _NeedWelding;
        private bool _Grinding;
        private bool _NeedGrinding;
        private bool _Transporting;
        private bool _InventoryFull;
        private bool _LimitsExceeded;
        private List<SyncComponents> _MissingComponentsSync;
        private List<SyncTargetEntityData> _PossibleWeldTargetsSync;
        private List<SyncTargetEntityData> _PossibleGrindTargetsSync;
        private List<SyncTargetEntityData> _PossibleFloatingTargetsSync;
        private IMySlimBlock _CurrentWeldingBlock;
        private IMySlimBlock _CurrentGrindingBlock;

        private Vector3D? _CurrentTransportTarget;
        private Vector3D? _LastTransportTarget;
        private bool _CurrentTransportIsPick;
        private TimeSpan _CurrentTransportTime = TimeSpan.Zero;
        private TimeSpan _CurrentTransportStartTime = TimeSpan.Zero;

        public bool Changed { get; private set; }

        public override string ToString()
        {
            return string.Format("Ready={0}, Welding={1}/{2}, Grinding={3}/{4}, MissingComponentsCount={5}, PossibleWeldTargetsCount={6}, PossibleGrindTargetsCount={7}, PossibleFloatingTargetsCount={8}, CurrentWeldingBlock={9}, CurrentGrindingBlock={10}, CurrentTransportTarget={11}",
               Ready, Welding, NeedWelding, Grinding, NeedGrinding, MissingComponentsSync != null ? MissingComponentsSync.Count : -1, PossibleWeldTargetsSync != null ? PossibleWeldTargetsSync.Count : -1, PossibleGrindTargetsSync != null ? PossibleGrindTargetsSync.Count : -1, PossibleFloatingTargetsSync != null ? PossibleFloatingTargetsSync.Count : -1,
               Logging.BlockName(CurrentWeldingBlock, Logging.BlockNameOptions.None), Logging.BlockName(CurrentGrindingBlock, Logging.BlockNameOptions.None), CurrentTransportTarget);
        }

        [ProtoMember(1)]
        public bool Ready
        {
            get { return _Ready; }
            set
            {
                if (value != _Ready)
                {
                    _Ready = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(2)]
        public bool Welding
        {
            get { return _Welding; }
            set
            {
                if (value != _Welding)
                {
                    _Welding = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(3)]
        public bool NeedWelding
        {
            get { return _NeedWelding; }
            set
            {
                if (value != _NeedWelding)
                {
                    _NeedWelding = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(4)]
        public bool Grinding
        {
            get { return _Grinding; }
            set
            {
                if (value != _Grinding)
                {
                    _Grinding = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(5)]
        public bool NeedGrinding
        {
            get { return _NeedGrinding; }
            set
            {
                if (value != _NeedGrinding)
                {
                    _NeedGrinding = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(6)]
        public bool Transporting
        {
            get { return _Transporting; }
            set
            {
                if (value != _Transporting)
                {
                    _Transporting = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(7)]
        public TimeSpan LastTransmitted { get; set; }

        public IMySlimBlock CurrentWeldingBlock
        {
            get { return _CurrentWeldingBlock; }
            set
            {
                if (value != _CurrentWeldingBlock)
                {
                    _CurrentWeldingBlock = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(10)]
        public SyncEntityId CurrentWeldingBlockSync
        {
            get
            {
                return SyncEntityId.GetSyncId(_CurrentWeldingBlock);
            }
            set
            {
                CurrentWeldingBlock = SyncEntityId.GetItemAsSlimBlock(value);
            }
        }

        public IMySlimBlock CurrentGrindingBlock
        {
            get { return _CurrentGrindingBlock; }
            set
            {
                if (value != _CurrentGrindingBlock)
                {
                    _CurrentGrindingBlock = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(15)]
        public SyncEntityId CurrentGrindingBlockSync
        {
            get
            {
                return SyncEntityId.GetSyncId(_CurrentGrindingBlock);
            }
            set
            {
                CurrentGrindingBlock = SyncEntityId.GetItemAsSlimBlock(value);
            }
        }

        [ProtoMember(16)]
        public Vector3D? CurrentTransportTarget
        {
            get { return _CurrentTransportTarget; }
            set
            {
                if (value != _CurrentTransportTarget)
                {
                    _CurrentTransportTarget = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(17)]
        public Vector3D? LastTransportTarget
        {
            get { return _LastTransportTarget; }
            set
            {
                if (value != _LastTransportTarget)
                {
                    _LastTransportTarget = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(18)]
        public bool CurrentTransportIsPick
        {
            get { return _CurrentTransportIsPick; }
            set
            {
                if (value != _CurrentTransportIsPick)
                {
                    _CurrentTransportIsPick = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(19)]
        public TimeSpan CurrentTransportTime
        {
            get { return _CurrentTransportTime; }
            set
            {
                if (value != _CurrentTransportTime)
                {
                    _CurrentTransportTime = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(20)]
        public TimeSpan CurrentTransportStartTime
        {
            get { return _CurrentTransportStartTime; }
            set
            {
                if (value != _CurrentTransportStartTime)
                {
                    _CurrentTransportStartTime = value;
                    Changed = true;
                }
            }
        }

        public DefinitionIdHashDictionary MissingComponents { get; private set; }

        [ProtoMember(21)]
        public List<SyncComponents> MissingComponentsSync
        {
            get
            {
                if (_MissingComponentsSync == null)
                {
                    if (MissingComponents != null) _MissingComponentsSync = MissingComponents.GetSyncList();
                    else _MissingComponentsSync = new List<SyncComponents>();
                }
                return _MissingComponentsSync;
            }
        }

        [ProtoMember(22)]
        public bool InventoryFull
        {
            get { return _InventoryFull; }
            set
            {
                if (value != _InventoryFull)
                {
                    _InventoryFull = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(23)]
        public bool LimitsExceeded
        {
            get { return _LimitsExceeded; }
            set
            {
                if (value != _LimitsExceeded)
                {
                    _LimitsExceeded = value;
                    Changed = true;
                }
            }
        }

        public TargetBlockDataHashList PossibleWeldTargets { get; private set; }

        [ProtoMember(30)]
        public List<SyncTargetEntityData> PossibleWeldTargetsSync
        {
            get
            {
                if (_PossibleWeldTargetsSync == null)
                {
                    if (PossibleWeldTargets != null) _PossibleWeldTargetsSync = PossibleWeldTargets.GetSyncList();
                    else _PossibleWeldTargetsSync = new List<SyncTargetEntityData>();
                }
                return _PossibleWeldTargetsSync;
            }
        }

        public TargetBlockDataHashList PossibleGrindTargets { get; private set; }

        [ProtoMember(35)]
        public List<SyncTargetEntityData> PossibleGrindTargetsSync
        {
            get
            {
                if (_PossibleGrindTargetsSync == null)
                {
                    if (PossibleGrindTargets != null) _PossibleGrindTargetsSync = PossibleGrindTargets.GetSyncList();
                    else _PossibleGrindTargetsSync = new List<SyncTargetEntityData>();
                }
                return _PossibleGrindTargetsSync;
            }
        }

        public TargetEntityDataHashList PossibleFloatingTargets { get; private set; }

        [ProtoMember(36)]
        public List<SyncTargetEntityData> PossibleFloatingTargetsSync
        {
            get
            {
                if (_PossibleFloatingTargetsSync == null)
                {
                    if (PossibleFloatingTargets != null) _PossibleFloatingTargetsSync = PossibleFloatingTargets.GetSyncList();
                    else _PossibleFloatingTargetsSync = new List<SyncTargetEntityData>();
                }
                return _PossibleFloatingTargetsSync;
            }
        }

        public SyncBlockState()
        {
            MissingComponents = new DefinitionIdHashDictionary();
            PossibleWeldTargets = new TargetBlockDataHashList();
            PossibleGrindTargets = new TargetBlockDataHashList();
            PossibleFloatingTargets = new TargetEntityDataHashList();
        }

        internal void HasChanged()
        {
            Changed = true;
        }

        internal bool IsTransmitNeeded()
        {
            return Changed && MyAPIGateway.Session.ElapsedPlayTime.Subtract(LastTransmitted).TotalSeconds >= 2;
        }

        internal SyncBlockState GetTransmit()
        {
            _MissingComponentsSync = null;
            _PossibleWeldTargetsSync = null;
            _PossibleGrindTargetsSync = null;
            _PossibleFloatingTargetsSync = null;
            LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;
            Changed = false;
            return this;
        }

        internal void AssignReceived(SyncBlockState newState)
        {
            _Ready = newState.Ready;
            _Welding = newState.Welding;
            _NeedWelding = newState.NeedWelding;
            _Grinding = newState.Grinding;
            _NeedGrinding = newState.NeedGrinding;
            _InventoryFull = newState.InventoryFull;
            _LimitsExceeded = newState.LimitsExceeded;
            _CurrentTransportStartTime = MyAPIGateway.Session.ElapsedPlayTime - (newState.LastTransmitted - newState.CurrentTransportStartTime);
            _CurrentTransportTime = newState.CurrentTransportTime;

            _CurrentWeldingBlock = SyncEntityId.GetItemAsSlimBlock(newState.CurrentWeldingBlockSync);
            _CurrentGrindingBlock = SyncEntityId.GetItemAsSlimBlock(newState.CurrentGrindingBlockSync);
            _CurrentTransportTarget = newState.CurrentTransportTarget;
            _CurrentTransportIsPick = newState.CurrentTransportIsPick;

            MissingComponents.Clear();
            var missingComponentsSync = newState.MissingComponentsSync;
            if (missingComponentsSync != null) foreach (var item in missingComponentsSync) MissingComponents.Add(item.Component, item.Amount);

            PossibleWeldTargets.Clear();
            var possibleWeldTargetsSync = newState.PossibleWeldTargetsSync;
            if (possibleWeldTargetsSync != null) foreach (var item in possibleWeldTargetsSync) PossibleWeldTargets.Add(new TargetBlockData(SyncEntityId.GetItemAsSlimBlock(item.Entity), item.Distance, 0));

            PossibleGrindTargets.Clear();
            var possibleGrindTargetsSync = newState.PossibleGrindTargetsSync;
            if (possibleGrindTargetsSync != null) foreach (var item in possibleGrindTargetsSync) PossibleGrindTargets.Add(new TargetBlockData(SyncEntityId.GetItemAsSlimBlock(item.Entity), item.Distance, 0));

            PossibleFloatingTargets.Clear();
            var possibleFloatingTargetsSync = newState.PossibleFloatingTargetsSync;
            if (possibleFloatingTargetsSync != null) foreach (var item in possibleFloatingTargetsSync) PossibleFloatingTargets.Add(new TargetEntityData(SyncEntityId.GetItemAs<Sandbox.Game.Entities.MyFloatingObject>(item.Entity), item.Distance));

            Changed = true;
        }

        internal void ResetChanged()
        {
            Changed = false;
        }
    }
}