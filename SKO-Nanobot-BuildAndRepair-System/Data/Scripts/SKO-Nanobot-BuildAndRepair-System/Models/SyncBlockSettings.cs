using ProtoBuf;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Xml.Serialization;
using VRage.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Models
{
    /// <summary>
    /// The settings for Block
    /// </summary>
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class SyncBlockSettings
    {
        [Flags]
        public enum Settings
        {
            AllowBuild = 0x00000001,
            ShowArea = 0x00000002,
            ScriptControlled = 0x00000004,
            UseIgnoreColor = 0x00000010,
            UseGrindColor = 0x00000020,
            GrindNearFirst = 0x00000100,
            GrindSmallestGridFirst = 0x00000200,
            ComponentCollectIfIdle = 0x00010000,
            PushIngotOreImmediately = 0x00020000,
            PushComponentImmediately = 0x00040000,
            PushItemsImmediately = 0x00080000
        }

        private BoundingBoxD _CorrectedAreaBoundingBox;
        private Vector3 _CorrectedAreaOffset;
        private Settings _Flags;
        private Vector3 _IgnoreColor;
        private uint _IgnoreColorPacked;
        private Vector3 _GrindColor;
        private uint _GrindColorPacked;
        private AutoGrindRelation _UseGrindJanitorOn;
        private AutoGrindOptions _GrindJanitorOptions;
        private AutoWeldOptions _WeldOptions;
        private Vector3 _AreaOffset;
        private Vector3 _AreaSize;
        private string _WeldPriority;
        private string _GrindPriority;
        private string _ComponentCollectPriority;
        private float _SoundVolume;
        private SearchModes _SearchMode;
        private WorkModes _WorkMode;
        private VRage.Game.ModAPI.Ingame.IMySlimBlock _CurrentPickedWeldingBlock;
        private VRage.Game.ModAPI.Ingame.IMySlimBlock _CurrentPickedGrindingBlock;
        private TimeSpan _LastStored;
        private TimeSpan _LastTransmitted;

        [XmlIgnore]
        public uint Changed { get; private set; }

        [ProtoMember(5), XmlElement]
        public Settings Flags
        {
            get
            {
                return _Flags;
            }
            set
            {
                if (_Flags != value)
                {
                    _Flags = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(20), XmlElement]
        public SearchModes SearchMode
        {
            get
            {
                return _SearchMode;
            }
            set
            {
                if (_SearchMode != value)
                {
                    _SearchMode = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(25), XmlElement]
        public WorkModes WorkMode
        {
            get
            {
                return _WorkMode;
            }
            set
            {
                if (_WorkMode != value)
                {
                    _WorkMode = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(31), XmlElement]
        public Vector3 IgnoreColor
        {
            get
            {
                return _IgnoreColor;
            }
            set
            {
                if (_IgnoreColor != value)
                {
                    _IgnoreColor = value;
                    _IgnoreColorPacked = value.PackHSVToUint();
                    Changed = 3u;
                }
            }
        }

        public uint IgnoreColorPacked
        {
            get
            {
                return _IgnoreColorPacked;
            }
        }

        [ProtoMember(36), XmlElement]
        public Vector3 GrindColor
        {
            get
            {
                return _GrindColor;
            }
            set
            {
                if (_GrindColor != value)
                {
                    _GrindColor = value;
                    _GrindColorPacked = value.PackHSVToUint();
                    Changed = 3u;
                }
            }
        }

        public uint GrindColorPacked
        {
            get
            {
                return _GrindColorPacked;
            }
        }

        [ProtoMember(39), XmlElement]
        public AutoGrindRelation UseGrindJanitorOn
        {
            get
            {
                return _UseGrindJanitorOn;
            }
            set
            {
                if (_UseGrindJanitorOn != value)
                {
                    _UseGrindJanitorOn = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(40), XmlElement]
        public AutoGrindOptions GrindJanitorOptions
        {
            get
            {
                return _GrindJanitorOptions;
            }
            set
            {
                if (_GrindJanitorOptions != value)
                {
                    _GrindJanitorOptions = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(49), XmlElement]
        public AutoWeldOptions WeldOptions
        {
            get
            {
                return _WeldOptions;
            }
            set
            {
                if (_WeldOptions != value)
                {
                    _WeldOptions = value;
                    Changed = 3u;
                }
            }
        }

        //+X = Right   -Y = Left
        //+Y = Up      -Y = Down
        //+Z = Forward -Z = Backward
        [ProtoMember(50), XmlElement]
        public Vector3 AreaOffset
        {
            get
            {
                return _AreaOffset;
            }
            set
            {
                if (_AreaOffset != value)
                {
                    _AreaOffset = value;
                    Changed = 3u;
                    RecalcAreaBoundigBox();
                }
            }
        }

        [ProtoMember(51), XmlElement]
        public Vector3 AreaSize
        {
            get
            {
                return _AreaSize;
            }
            set
            {
                if (_AreaSize != value)
                {
                    _AreaSize = value;
                    Changed = 3u;
                    RecalcAreaBoundigBox();
                }
            }
        }

        private int? _AreaWidthLeft;

        [XmlElement]
        public int? AreaWidthLeft
        {
            get
            {
                return null;
            }
            set
            {
                _AreaWidthLeft = value;
                if (value != null) RecalcOffsetAndSize();
            }
        }

        private int? _AreaWidthRight;

        [XmlElement]
        public int? AreaWidthRight
        {
            get
            {
                return null;
            }
            set
            {
                _AreaWidthRight = value;
                if (value != null) RecalcOffsetAndSize();
            }
        }

        private int? _AreaWidthTop;

        [XmlElement]
        public int? AreaHeightTop
        {
            get
            {
                return null;
            }
            set
            {
                _AreaWidthTop = value;
                if (value != null) RecalcOffsetAndSize();
            }
        }

        private int? _AreaWidthBottom;

        [XmlElement]
        public int? AreaHeightBottom
        {
            get
            {
                return null;
            }
            set
            {
                _AreaWidthBottom = value;
                if (value != null) RecalcOffsetAndSize();
            }
        }

        private int? _AreaWidthFront;

        [XmlElement]
        public int? AreaDepthFront
        {
            get
            {
                return null;
            }
            set
            {
                _AreaWidthFront = value;
                if (value != null) RecalcOffsetAndSize();
            }
        }

        private int? _AreaWidthRear;

        [XmlElement]
        public int? AreaDepthRear
        {
            get
            {
                return null;
            }
            set
            {
                _AreaWidthRear = value;
                if (value != null) RecalcOffsetAndSize();
            }
        }

        [ProtoMember(61), XmlElement]
        public string WeldPriority
        {
            get
            {
                return _WeldPriority;
            }
            set
            {
                if (_WeldPriority != value)
                {
                    _WeldPriority = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(62), XmlElement]
        public string GrindPriority
        {
            get
            {
                return _GrindPriority;
            }
            set
            {
                if (_GrindPriority != value)
                {
                    _GrindPriority = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(65), XmlElement]
        public string ComponentCollectPriority
        {
            get
            {
                return _ComponentCollectPriority;
            }
            set
            {
                if (_ComponentCollectPriority != value)
                {
                    _ComponentCollectPriority = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(80), XmlElement]
        public float SoundVolume
        {
            get
            {
                return _SoundVolume;
            }
            set
            {
                if (_SoundVolume != value)
                {
                    _SoundVolume = value;
                    Changed = 3u;
                }
            }
        }

        [XmlIgnore]
        public VRage.Game.ModAPI.Ingame.IMySlimBlock CurrentPickedWeldingBlock
        {
            get
            {
                return _CurrentPickedWeldingBlock;
            }
            set
            {
                if (_CurrentPickedWeldingBlock != value)
                {
                    _CurrentPickedWeldingBlock = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(100), XmlElement]
        public SyncEntityId CurrentPickedWeldingBlockSync
        {
            get
            {
                return SyncEntityId.GetSyncId(_CurrentPickedWeldingBlock);
            }
            set
            {
                CurrentPickedWeldingBlock = SyncEntityId.GetItemAsSlimBlock(value);
            }
        }

        [XmlIgnore]
        public VRage.Game.ModAPI.Ingame.IMySlimBlock CurrentPickedGrindingBlock
        {
            get
            {
                return _CurrentPickedGrindingBlock;
            }
            set
            {
                if (_CurrentPickedGrindingBlock != value)
                {
                    _CurrentPickedGrindingBlock = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(105), XmlElement]
        public SyncEntityId CurrentPickedGrindingBlockSync
        {
            get
            {
                return SyncEntityId.GetSyncId(_CurrentPickedGrindingBlock);
            }
            set
            {
                CurrentPickedGrindingBlock = SyncEntityId.GetItemAsSlimBlock(value);
            }
        }

        [XmlIgnore]
        public int MaximumRange { get; private set; }

        [XmlIgnore]
        public int MaximumOffset { get; private set; }

        [XmlIgnore]
        public float TransportSpeed { get; private set; }

        [XmlIgnore]
        public float MaximumRequiredElectricPowerStandby { get; private set; }

        [XmlIgnore]
        public float MaximumRequiredElectricPowerWelding { get; private set; }

        [XmlIgnore]
        public float MaximumRequiredElectricPowerGrinding { get; private set; }

        [XmlIgnore]
        public float MaximumRequiredElectricPowerTransport { get; private set; }

        //+X = Forward -X = Backward
        //+Y = Left    -Y = Right
        //+Z = Up      -Z = Down
        internal Vector3 CorrectedAreaOffset
        {
            get
            {
                return _CorrectedAreaOffset;
            }
        }

        internal BoundingBoxD CorrectedAreaBoundingBox
        {
            get
            {
                return _CorrectedAreaBoundingBox;
            }
        }

        public SyncBlockSettings() : this(null)
        {
        }

        public SyncBlockSettings(NanobotSystem system)
        {
            _WeldPriority = string.Empty;
            _GrindPriority = string.Empty;
            _ComponentCollectPriority = string.Empty;
            CheckLimits(system, true);

            Changed = 0;
            _LastStored = MyAPIGateway.Session.ElapsedPlayTime.Add(TimeSpan.FromSeconds(60));
            _LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;

            RecalcAreaBoundigBox();
        }

        public void TrySave(IMyEntity entity, Guid guid)
        {
            if ((Changed & 2u) == 0) return;
            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastStored) < TimeSpan.FromSeconds(20)) return;
            Save(entity, guid);
        }

        public void Save(IMyEntity entity, Guid guid)
        {
            if (entity.Storage == null)
            {
                entity.Storage = new MyModStorageComponent();
            }

            var storage = entity.Storage;
            storage[guid] = GetAsXML();
            Changed = (Changed & ~2u);
            _LastStored = MyAPIGateway.Session.ElapsedPlayTime;
        }

        public string GetAsXML()
        {
            return MyAPIGateway.Utilities.SerializeToXML(this);
        }

        public void ResetChanged()
        {
            Changed = (Changed & ~2u);
        }

        public static SyncBlockSettings Load(NanobotSystem system, Guid guid, BlockPriorityHandling blockWeldPriority, BlockPriorityHandling blockGrindPriority, ComponentPriorityHandling componentCollectPriority)
        {
            var storage = system.Entity.Storage;
            string data;
            SyncBlockSettings settings = null;
            if (storage != null && storage.TryGetValue(guid, out data))
            {
                try
                {
                    //Fix changed names
                    data = data.Replace("GrindColorNearFirst", "GrindNearFirst");
                    settings = MyAPIGateway.Utilities.SerializeFromXML<SyncBlockSettings>(data);
                    if (settings != null)
                    {
                        settings.RecalcAreaBoundigBox();
                        //Retrieve current settings or default if WeldPriority/GrindPriority/ComponentCollectPriority was empty
                        blockWeldPriority.SetEntries(settings.WeldPriority);
                        settings.WeldPriority = blockWeldPriority.GetEntries();

                        blockGrindPriority.SetEntries(settings.GrindPriority);
                        settings.GrindPriority = blockGrindPriority.GetEntries();

                        componentCollectPriority.SetEntries(settings.ComponentCollectPriority);
                        settings.ComponentCollectPriority = componentCollectPriority.GetEntries();

                        settings.Changed = 0;
                        settings._LastStored = MyAPIGateway.Session.ElapsedPlayTime.Add(TimeSpan.FromSeconds(60));
                        settings._LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;
                        return settings;
                    }
                }
                catch (Exception ex)
                {
                    Logging.Instance.Write("SyncBlockSettings: Exception: " + ex);
                }
            }

            settings = new SyncBlockSettings(system);
            blockWeldPriority.SetEntries(settings.WeldPriority);
            blockGrindPriority.SetEntries(settings.GrindPriority);
            componentCollectPriority.SetEntries(settings.ComponentCollectPriority);
            settings.Changed = 0;
            return settings;
        }

        public void AssignReceived(SyncBlockSettings newSettings, BlockPriorityHandling weldPriority, BlockPriorityHandling grindPriority, ComponentPriorityHandling componentCollectPriority)
        {
            _Flags = newSettings._Flags;
            _IgnoreColor = newSettings.IgnoreColor;
            _GrindColor = newSettings.GrindColor;
            _UseGrindJanitorOn = newSettings.UseGrindJanitorOn;
            _GrindJanitorOptions = newSettings.GrindJanitorOptions;
            _WeldOptions = newSettings.WeldOptions;

            _AreaOffset = newSettings.AreaOffset;
            _AreaSize = newSettings.AreaSize;

            _WeldPriority = newSettings.WeldPriority;
            _GrindPriority = newSettings.GrindPriority;
            _ComponentCollectPriority = newSettings.ComponentCollectPriority;

            _SoundVolume = newSettings.SoundVolume;
            _SearchMode = newSettings.SearchMode;
            _WorkMode = newSettings.WorkMode;

            RecalcAreaBoundigBox();
            _IgnoreColorPacked = _IgnoreColor.PackHSVToUint();
            _GrindColorPacked = _GrindColor.PackHSVToUint();
            weldPriority.SetEntries(WeldPriority);
            grindPriority.SetEntries(GrindPriority);
            componentCollectPriority.SetEntries(ComponentCollectPriority);

            Changed = 2u;
        }

        public SyncBlockSettings GetTransmit()
        {
            _LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;
            Changed = Changed & ~1u;
            return this;
        }

        public bool IsTransmitNeeded()
        {
            return (Changed & 1u) != 0 && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastTransmitted) >= TimeSpan.FromSeconds(1);
        }

        private void RecalcAreaBoundigBox()
        {
            var border = 0.25d;
            _CorrectedAreaBoundingBox = new BoundingBoxD(new Vector3D(-AreaSize.Z / 2 + border, -AreaSize.X / 2 + border, -AreaSize.Y / 2 + border), new Vector3D(AreaSize.Z / 2 - border, AreaSize.X / 2 - border, AreaSize.Y / 2 - border));
            _CorrectedAreaOffset = new Vector3(AreaOffset.Z, -AreaOffset.X, AreaOffset.Y);
        }

        private void RecalcOffsetAndSize()
        {
            if (_AreaWidthLeft != null && _AreaWidthRight != null)
            {
                AreaSize = new Vector3(_AreaWidthRight.Value + _AreaWidthLeft.Value, AreaSize.Y, AreaSize.Z);
                AreaOffset = new Vector3(AreaSize.X / 2 - _AreaWidthRight.Value, AreaOffset.Y, AreaOffset.Z);
            }
            if (_AreaWidthTop != null && _AreaWidthBottom != null)
            {
                AreaSize = new Vector3(AreaSize.X, _AreaWidthTop.Value + _AreaWidthBottom.Value, AreaSize.Z);
                AreaOffset = new Vector3(AreaOffset.X, AreaSize.Y / 2 - _AreaWidthBottom.Value, AreaOffset.Z);
            }
            if (_AreaWidthFront != null && _AreaWidthRear != null)
            {
                AreaSize = new Vector3(AreaSize.X, AreaSize.Y, _AreaWidthFront.Value + _AreaWidthRear.Value);
                AreaOffset = new Vector3(AreaOffset.X, AreaOffset.Y, AreaSize.Z / 2 - _AreaWidthRear.Value);
            }
        }

        public void CheckLimits(NanobotSystem system, bool init)
        {
            var scale = (system != null && system.Welder != null ? (system.Welder.BlockDefinition.SubtypeName.Contains("Large") ? 1f : 2f) : 1f);

            if (Mod.Settings.Welder.AreaOffsetFixed || init)
            {
                MaximumOffset = 0;
                AreaOffset = new Vector3(0, 0, 0);
            }
            else
            {
                MaximumOffset = (int)Math.Ceiling(Mod.Settings.MaximumOffset / scale);
                if (AreaOffset.X > MaximumOffset || init) AreaOffset = new Vector3(init ? 0 : (float)MaximumOffset, AreaOffset.Y, AreaOffset.Z);
                else if (AreaOffset.X < -MaximumOffset || init) AreaOffset = new Vector3(init ? 0 : (float)-MaximumOffset, AreaOffset.Y, AreaOffset.Z);

                if (AreaOffset.Y > MaximumOffset || init) AreaOffset = new Vector3(AreaOffset.X, init ? 0 : (float)MaximumOffset, AreaOffset.Z);
                else if (AreaOffset.Y < -MaximumOffset || init) AreaOffset = new Vector3(AreaOffset.X, init ? 0 : (float)-MaximumOffset, AreaOffset.Z);

                if (AreaOffset.Z > MaximumOffset || init) AreaOffset = new Vector3(AreaOffset.X, AreaOffset.Y, init ? 0 : (float)MaximumOffset);
                else if (AreaOffset.Z < -MaximumOffset || init) AreaOffset = new Vector3(AreaOffset.X, AreaOffset.Y, init ? 0 : (float)-MaximumOffset);
            }

            MaximumRange = (int)Math.Ceiling(Mod.Settings.Range * 2 / scale);
            if (Mod.Settings.Welder.AreaSizeFixed || init)
            {
                AreaSize = new Vector3(MaximumRange, MaximumRange, MaximumRange);
            }
            else
            {
                if (AreaSize.X > MaximumRange || init) AreaSize = new Vector3(MaximumRange, AreaSize.Y, AreaSize.Z);
                if (AreaSize.Y > MaximumRange || init) AreaSize = new Vector3(AreaSize.X, MaximumRange, AreaSize.Z);
                if (AreaSize.Z > MaximumRange || init) AreaSize = new Vector3(AreaSize.X, AreaSize.Y, MaximumRange);
            }

            MaximumRequiredElectricPowerStandby = Mod.Settings.MaximumRequiredElectricPowerStandby / scale;
            MaximumRequiredElectricPowerTransport = Mod.Settings.MaximumRequiredElectricPowerTransport / scale;
            MaximumRequiredElectricPowerWelding = Mod.Settings.Welder.MaximumRequiredElectricPowerWelding / scale;
            MaximumRequiredElectricPowerGrinding = Mod.Settings.Welder.MaximumRequiredElectricPowerGrinding / scale;

            var maxMultiplier = Math.Max(Mod.Settings.Welder.WeldingMultiplier, Mod.Settings.Welder.GrindingMultiplier);
            TransportSpeed = maxMultiplier * NanobotSystem.WELDER_TRANSPORTSPEED_METER_PER_SECOND_DEFAULT * Math.Min(Mod.Settings.Range / NanobotSystem.WELDER_RANGE_DEFAULT_IN_M, 4.0f);

            if (Mod.Settings.Welder.AllowBuildFixed || init)
            {
                Flags = (Flags & ~Settings.AllowBuild) | (Mod.Settings.Welder.AllowBuildDefault ? Settings.AllowBuild : 0);
            }

            if (Mod.Settings.Welder.UseIgnoreColorFixed || init)
            {
                Flags = (Flags & ~Settings.UseIgnoreColor) | (Mod.Settings.Welder.UseIgnoreColorDefault ? Settings.UseIgnoreColor : 0);
                if (Mod.Settings.Welder.IgnoreColorDefault != null && Mod.Settings.Welder.IgnoreColorDefault.Length >= 3)
                {
                    IgnoreColor = new Vector3D(Mod.Settings.Welder.IgnoreColorDefault[0] / 360f,
                                              ((float)Math.Round(Mod.Settings.Welder.IgnoreColorDefault[1], 1, MidpointRounding.AwayFromZero) / 100f) - NanobotTerminal.SATURATION_DELTA,
                                              ((float)Math.Round(Mod.Settings.Welder.IgnoreColorDefault[2], 1, MidpointRounding.AwayFromZero) / 100f) - NanobotTerminal.VALUE_DELTA + NanobotTerminal.VALUE_COLORIZE_DELTA);
                }
            }

            if (Mod.Settings.Welder.UseGrindColorFixed || init)
            {
                Flags = (Flags & ~Settings.UseGrindColor) | (Mod.Settings.Welder.UseGrindColorDefault ? Settings.UseGrindColor : 0);
                if (Mod.Settings.Welder.GrindColorDefault != null && Mod.Settings.Welder.GrindColorDefault.Length >= 3)
                {
                    GrindColor = new Vector3D(Mod.Settings.Welder.GrindColorDefault[0] / 360f,
                                              ((float)Math.Round(Mod.Settings.Welder.GrindColorDefault[1], 1, MidpointRounding.AwayFromZero) / 100f) - NanobotTerminal.SATURATION_DELTA,
                                              ((float)Math.Round(Mod.Settings.Welder.GrindColorDefault[2], 1, MidpointRounding.AwayFromZero) / 100f) - NanobotTerminal.VALUE_DELTA + NanobotTerminal.VALUE_COLORIZE_DELTA);
                }
            }

            if (Mod.Settings.Welder.UseGrindJanitorFixed || init)
            {
                UseGrindJanitorOn = Mod.Settings.Welder.UseGrindJanitorDefault;
                GrindJanitorOptions = Mod.Settings.Welder.GrindJanitorOptionsDefault;
            }

            UseGrindJanitorOn &= Mod.Settings.Welder.AllowedGrindJanitorRelations;

            if (Mod.Settings.Welder.ShowAreaFixed || init) Flags = (Flags & ~Settings.ShowArea);
            if (Mod.Settings.Welder.PushIngotOreImmediatelyFixed || init) Flags = (Flags & ~Settings.PushIngotOreImmediately) | (Mod.Settings.Welder.PushIngotOreImmediatelyDefault ? Settings.PushIngotOreImmediately : 0);
            if (Mod.Settings.Welder.PushComponentImmediatelyFixed || init) Flags = (Flags & ~Settings.PushComponentImmediately) | (Mod.Settings.Welder.PushComponentImmediatelyDefault ? Settings.PushComponentImmediately : 0);
            if (Mod.Settings.Welder.PushItemsImmediatelyFixed || init) Flags = (Flags & ~Settings.PushItemsImmediately) | (Mod.Settings.Welder.PushItemsImmediatelyDefault ? Settings.PushItemsImmediately : 0);
            if (Mod.Settings.Welder.CollectIfIdleFixed || init) Flags = (Flags & ~Settings.ComponentCollectIfIdle) | (Mod.Settings.Welder.CollectIfIdleDefault ? Settings.ComponentCollectIfIdle : 0);
            if (Mod.Settings.Welder.SoundVolumeFixed || init) SoundVolume = Mod.Settings.Welder.SoundVolumeDefault;
            if (Mod.Settings.Welder.ScriptControllFixed || init) Flags = (Flags & ~Settings.ScriptControlled);
            if ((Mod.Settings.Welder.AllowedSearchModes & SearchMode) == 0 || init)
            {
                if ((Mod.Settings.Welder.AllowedSearchModes & Mod.Settings.Welder.SearchModeDefault) != 0)
                {
                    SearchMode = Mod.Settings.Welder.SearchModeDefault;
                }
                else
                {
                    if ((Mod.Settings.Welder.AllowedSearchModes & SearchModes.Grids) != 0) SearchMode = SearchModes.Grids;
                    else if ((Mod.Settings.Welder.AllowedSearchModes & SearchModes.BoundingBox) != 0) SearchMode = SearchModes.BoundingBox;
                }
            }

            if ((Mod.Settings.Welder.AllowedWorkModes & WorkMode) == 0 || init)
            {
                if ((Mod.Settings.Welder.AllowedWorkModes & Mod.Settings.Welder.WorkModeDefault) != 0)
                {
                    WorkMode = Mod.Settings.Welder.WorkModeDefault;
                }
                else
                {
                    if ((Mod.Settings.Welder.AllowedWorkModes & WorkModes.WeldBeforeGrind) != 0) WorkMode = WorkModes.WeldBeforeGrind;
                    else if ((Mod.Settings.Welder.AllowedWorkModes & WorkModes.GrindBeforeWeld) != 0) WorkMode = WorkModes.GrindBeforeWeld;
                    else if ((Mod.Settings.Welder.AllowedWorkModes & WorkModes.GrindIfWeldGetStuck) != 0) WorkMode = WorkModes.GrindIfWeldGetStuck;
                }
            }
        }
    }
}