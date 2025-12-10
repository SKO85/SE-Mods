namespace SpaceEquipmentLtd.NanobotDrillSystem
{
   using System;
   using System.Collections.Generic;
   using System.Xml.Serialization;

   using VRage.Game;
   using VRage.ModAPI;
   using VRageMath;

   using Sandbox.Game.EntityComponents;
   using Sandbox.ModAPI;

   using SpaceEquipmentLtd.Utils;
   using ProtoBuf;
   using Sandbox.Game.Entities;
   using VRage.ObjectBuilders;
   using Sandbox.Definitions;
   using VRage.Game.ModAPI;

   /// <summary>
   /// The settings for Mod
   /// </summary>
   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class SyncModSettings
   {
      private const int CurrentSettingsVersion = 4;
      [XmlElement]
      public int Version { get; set; }

      [XmlElement]
      public bool DisableLocalization { get; set; }

      [ProtoMember(1), XmlElement]
      public Logging.Level LogLevel { get; set; }

      [XmlIgnore]
      public TimeSpan SourcesUpdateInterval { get; set; }

      [XmlIgnore]
      public TimeSpan TargetsUpdateInterval { get; set; }

      [ProtoMember(10), XmlElement]
      public int MaxBackgroundTasks { get; set; }

      [ProtoMember(20), XmlElement]
      public int Range { get; set; }

      [ProtoMember(30), XmlElement]
      public int MaximumOffset { get; set; }

      [ProtoMember(40), XmlElement]
      public long SourcesAndTargetsUpdateIntervalTicks
      {
         get { return TargetsUpdateInterval.Ticks; }
         set {
            TargetsUpdateInterval = new TimeSpan(value);
            SourcesUpdateInterval = new TimeSpan(value * 6);
         }
      }

      [ProtoMember(100), XmlElement]
      public float MaximumRequiredElectricPowerStandby { get; set; }

      [ProtoMember(110), XmlElement]
      public float MaximumRequiredElectricPowerTransport { get; set; }

      [ProtoMember(120), XmlElement]
      public SyncModSettingsDrill Drill { get; set; }

      public SyncModSettings()
      {
         DisableLocalization = false;
         LogLevel = Logging.Level.Error; //Default
         MaxBackgroundTasks = NanobotDrillSystemMod.MaxBackgroundTasks_Default;
         TargetsUpdateInterval = TimeSpan.FromSeconds(10);
         SourcesUpdateInterval = TimeSpan.FromSeconds(60);
         Range = NanobotDrillSystemBlock.DRILL_RANGE_DEFAULT_IN_M;
         MaximumOffset = NanobotDrillSystemBlock.DRILL_OFFSET_MAX_IN_M;
         MaximumRequiredElectricPowerStandby = NanobotDrillSystemBlock.DRILL_REQUIRED_ELECTRIC_POWER_STANDBY_DEFAULT;
         MaximumRequiredElectricPowerTransport = NanobotDrillSystemBlock.DRILL_REQUIRED_ELECTRIC_POWER_TRANSPORT_DEFAULT;
         Drill = new SyncModSettingsDrill();
      }

      public static SyncModSettings Load()
      {
         var world = false;
         SyncModSettings settings = null;
         try
         {
            if (MyAPIGateway.Utilities.FileExistsInWorldStorage("ModSettings.xml", typeof(SyncModSettings)))
            {
               world = true;
               using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage("ModSettings.xml", typeof(SyncModSettings)))
               {
                  settings = MyAPIGateway.Utilities.SerializeFromXML<SyncModSettings>(reader.ReadToEnd());
                  Mod.Log.Write("NanobotDrillSystemSettings: Loaded from world file.");
               }
            }
            else if (MyAPIGateway.Utilities.FileExistsInLocalStorage("ModSettings.xml", typeof(SyncModSettings)))
            {
               using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage("ModSettings.xml", typeof(SyncModSettings)))
               {
                  settings = MyAPIGateway.Utilities.SerializeFromXML<SyncModSettings>(reader.ReadToEnd());
                  Mod.Log.Write("NanobotDrillSystemSettings: Loaded from local storage.");
               }
            }

            if (settings != null)
            {
               var adjusted = false;
               if (settings.Version < CurrentSettingsVersion)
               {
                  Mod.Log.Write("NanobotDrillSystemSettings: Settings have old version: {0} update to {1}", settings.Version, CurrentSettingsVersion);
                  switch (settings.Version)
                  {
                     case 0:
                        settings.LogLevel = Logging.Level.Error;
                        break;
                  }

                  if (settings.Drill.AllowedWorkModes == 0) settings.Drill.AllowedWorkModes = WorkModes.Drill | WorkModes.Collect | WorkModes.Fill;
                  if (settings.Drill.DrillingMultiplier == 0) settings.Drill.DrillingMultiplier = 1;
                  if (settings.Drill.FillingMultiplier == 0) settings.Drill.FillingMultiplier = 1;

                  adjusted = true;
                  settings.Version = CurrentSettingsVersion;
               }

               if (settings.MaxBackgroundTasks > NanobotDrillSystemMod.MaxBackgroundTasks_Max)
               {
                  settings.MaxBackgroundTasks = NanobotDrillSystemMod.MaxBackgroundTasks_Max;
                  adjusted = true;
               }
               else if (settings.MaxBackgroundTasks < NanobotDrillSystemMod.MaxBackgroundTasks_Min)
               {
                  settings.MaxBackgroundTasks = NanobotDrillSystemMod.MaxBackgroundTasks_Min;
                  adjusted = true;
               }

               if (settings.Range > NanobotDrillSystemBlock.DRILL_RANGE_MAX_IN_M)
               {
                  settings.Range = NanobotDrillSystemBlock.DRILL_RANGE_MAX_IN_M;
                  adjusted = true;
               }
               else if (settings.Range < NanobotDrillSystemBlock.DRILL_RANGE_MIN_IN_M)
               {
                  settings.Range = NanobotDrillSystemBlock.DRILL_RANGE_MIN_IN_M;
                  adjusted = true;
               }

               if (settings.MaximumOffset > NanobotDrillSystemBlock.DRILL_OFFSET_MAX_IN_M)
               {
                  settings.MaximumOffset = NanobotDrillSystemBlock.DRILL_OFFSET_MAX_IN_M;
                  adjusted = true;
               }
               else if (settings.MaximumOffset < 0)
               {
                  settings.MaximumOffset = 0;
                  adjusted = true;
               }

               if (settings.Drill.DrillingMultiplier < NanobotDrillSystemBlock.DRILL_FILL_MULTIPLIER_MIN)
               {
                  settings.Drill.DrillingMultiplier = NanobotDrillSystemBlock.DRILL_FILL_MULTIPLIER_MIN;
                  adjusted = true;
               }
               else if (settings.Drill.DrillingMultiplier >= NanobotDrillSystemBlock.DRILL_FILL_MULTIPLIER_MAX)
               {
                  settings.Drill.DrillingMultiplier = NanobotDrillSystemBlock.DRILL_FILL_MULTIPLIER_MAX;
                  adjusted = true;
               }

               if (settings.Drill.FillingMultiplier < NanobotDrillSystemBlock.DRILL_FILL_MULTIPLIER_MIN)
               {
                  settings.Drill.FillingMultiplier = NanobotDrillSystemBlock.DRILL_FILL_MULTIPLIER_MIN;
                  adjusted = true;
               }
               else if (settings.Drill.FillingMultiplier >= NanobotDrillSystemBlock.DRILL_FILL_MULTIPLIER_MAX)
               {
                  settings.Drill.FillingMultiplier = NanobotDrillSystemBlock.DRILL_FILL_MULTIPLIER_MAX;
                  adjusted = true;
               }

               Mod.Log.Write(Logging.Level.Info, "NanobotDrillSystemSettings: Settings {0} ({1}/{2})", settings, settings.Drill.DrillingMultiplier, settings.Drill.FillingMultiplier);
               if (adjusted) Save(settings, world);
            }
            else
            {
               settings = new SyncModSettings() { Version = CurrentSettingsVersion };
               //Save(settings, world); don't save file with default values
            }
         }
         catch (Exception ex)
         {
            Mod.Log.Write(Logging.Level.Error, "NanobotDrillSystemSettings: Exception while loading: {0}", ex);
         }

         return settings;
      }

      public static void Save(SyncModSettings settings, bool world)
      {
         if (world)
         {
            using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage("ModSettings.xml", typeof(SyncModSettings)))
            {
               writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
            }
         }
         else
         {
            using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage("ModSettings.xml", typeof(SyncModSettings)))
            {
               writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
            }
         }
      }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class SyncModSettingsDrill
   {
      [ProtoMember(10), XmlElement]
      public float MaximumRequiredElectricPowerDrilling { get; set; }

      [ProtoMember(20), XmlElement]
      public float MaximumRequiredElectricPowerFilling { get; set; }

      [ProtoMember(30), XmlElement]
      public float DrillingMultiplier { get; set; }

      [ProtoMember(40), XmlElement]
      public float FillingMultiplier { get; set; }

      [ProtoMember(100), XmlElement]
      public WorkModes AllowedWorkModes { get; set; }
      [ProtoMember(110), XmlElement]
      public WorkModes WorkModeDefault { get; set; }

      [ProtoMember(120), XmlElement]
      public bool ShowAreaFixed { get; set; }

      [ProtoMember(130), XmlElement]
      public bool AreaSizeFixed { get; set; }
      [ProtoMember(131), XmlElement]
      public bool AreaOffsetFixed { get; set; }

      [ProtoMember(140), XmlElement]
      public bool DrillPriorityFixed { get; set; }
      [ProtoMember(150), XmlElement]
      public bool CollectPriorityFixed { get; set; }

      [ProtoMember(160), XmlElement]
      public bool CollectIfIdleFixed { get; set; }
      [ProtoMember(170), XmlElement]
      public bool CollectIfIdleDefault { get; set; }

      [ProtoMember(180), XmlElement]
      public bool RemoteControlledFixed { get; set; }

      [ProtoMember(200), XmlElement]
      public bool SoundVolumeFixed { get; set; }
      [ProtoMember(201), XmlElement]
      public float SoundVolumeDefault { get; set; }

      [ProtoMember(210), XmlElement]
      public bool ScriptControllFixed { get; set; }

      [ProtoMember(220), XmlElement]
      public VisualAndSoundEffects AllowedEffects { get; set; }

      public SyncModSettingsDrill()
      {
         MaximumRequiredElectricPowerDrilling = NanobotDrillSystemBlock.DRILL_REQUIRED_ELECTRIC_POWER_DRILLING_DEFAULT;
         MaximumRequiredElectricPowerFilling = NanobotDrillSystemBlock.DRILL_REQUIRED_ELECTRIC_POWER_FILLING_DEFAULT;

         DrillingMultiplier = 1f;
         FillingMultiplier = 1f;

         AllowedWorkModes = WorkModes.Drill | WorkModes.Fill | WorkModes.Collect;
         WorkModeDefault = WorkModes.Drill;

         ShowAreaFixed = false;
         AreaSizeFixed = false;
         AreaOffsetFixed = false;
         DrillPriorityFixed = false;
         CollectPriorityFixed = false;

         CollectIfIdleDefault = false;

         RemoteControlledFixed = false;

         SoundVolumeFixed = false;
         SoundVolumeDefault = NanobotDrillSystemBlock.DRILL_SOUND_VOLUME / 2;

         ScriptControllFixed = false;
         AllowedEffects = VisualAndSoundEffects.DrillingSoundEffect | VisualAndSoundEffects.DrillingVisualEffect |
                          VisualAndSoundEffects.FillingSoundEffect | VisualAndSoundEffects.FillingVisualEffect |
                          VisualAndSoundEffects.TransportVisualEffect;
      }
   }

   /// <summary>
   /// The settings for Block
   /// </summary>
   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class SyncBlockSettings
   {
      [Flags]
      public enum Settings
      {
         ShowArea = 0x00000002,
         ScriptControlled = 0x00000004,
         ComponentCollectIfIdle = 0x00010000,

         RemoteControlled = 0x01000000,
         RemoteControlShowArea = 0x02000000,
         RemoteShowArea = 0x04000000,
         RemoteControlWorkdisabled = 0x10000000,
         RemoteWorkdisabled = 0x20000000,
      }
      private Settings _Flags;
      private BoundingBoxD _AreaBoundingBox;
      private Vector3 _AreaOffset;
      private Vector3 _AreaSize;
      private string _DrillPriority;
      private string _ComponentCollectPriority;
      private long _FillMaterial;
      private float _SoundVolume;
      private WorkModes _WorkMode;
      private NanobotDrillSystemBlock _ParentSystem;
      private long? _RemoteControlledBy;
      private IMyCharacter _RemoteControlledByCharacter;
      private object _CurrentPickedDrillingItem;
      private object _CurrentPickedFillingItem;
      private TimeSpan _LastStored;
      private TimeSpan _LastTransmitted;

      private void SetFlags(bool set, Settings setting)
      {
         if (set != ((_Flags & setting) != 0))
         {
            _Flags = (_Flags & ~setting) | (set ? setting : 0);
            Changed = 3u;
         }
      }

      [XmlIgnore]
      public uint Changed { get; private set; }

      [ProtoMember(10), XmlElement]
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

      //+X = Forward -X = Backward
      //+Y = Left    -Y = Right
      //+Z = Up      -Z = Down
      [ProtoMember(30), XmlElement]
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
            }
         }
      }

      [ProtoMember(40), XmlElement]
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

      [ProtoMember(50), XmlElement]
      public string DrillPriority
      {
         get
         {
            return _DrillPriority;
         }
         set
         {
            if (_DrillPriority != value)
            {
               _DrillPriority = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(60), XmlElement]
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

      [ProtoMember(70), XmlElement]
      public long FillMaterial
      {
         get
         {
            return _FillMaterial;
         }
         set
         {
            if (_FillMaterial != value)
            {
               _FillMaterial = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(80), XmlElement]
      public long? RemoteControlledBy
      {
         get
         {
            return _RemoteControlledBy;
         }
         set
         {
            if (_RemoteControlledBy != value)
            {
               _RemoteControlledBy = value;
               _RemoteControlledByCharacter = null;
               Changed = 3u;
            }
         }
      }

      [XmlIgnore]
      public NanobotDrillSystemBlock ParentSystem
      {
         get
         {
            return _ParentSystem;
         }
         set
         {
            if (_ParentSystem != value)
            {
               _ParentSystem = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(86), XmlElement]
      public SyncEntityId ParentSystemSync
      {
         get
         {
            return SyncEntityId.GetSyncId(_ParentSystem);
         }
         set
         {
            var slimBlock = SyncEntityId.GetItemAsSlimBlock(value);
            _ParentSystem = (slimBlock != null && slimBlock.FatBlock != null) ? slimBlock.FatBlock.GameLogic.GetAs<NanobotDrillSystemBlock>() : null;
         }
      }

      [ProtoMember(90), XmlElement]
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
      public object CurrentPickedDrillingItem
      {
         get
         {
            return _CurrentPickedDrillingItem;
         }
         set
         {
            if (_CurrentPickedDrillingItem != value)
            {
               _CurrentPickedDrillingItem = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(100), XmlElement]
      public SyncEntityId CurrentPickedDrillingItemSync
      {
         get
         {
            return SyncEntityId.GetSyncId(_CurrentPickedDrillingItem);               
         }
         set
         {
            _CurrentPickedDrillingItem = SyncEntityId.GetItem(value);
         }
      }

      [XmlIgnore]
      public object CurrentPickedFillingItem
      {
         get
         {
            return _CurrentPickedFillingItem;
         }
         set
         {
            if (_CurrentPickedFillingItem != value)
            {
               _CurrentPickedFillingItem = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(110), XmlElement]
      public SyncEntityId CurrentPickedFillingItemSync
      {
         get
         {
            return SyncEntityId.GetSyncId(_CurrentPickedFillingItem);
         }
         set
         {
            _CurrentPickedFillingItem = SyncEntityId.GetItem(value);
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
      public float MaximumRequiredElectricPowerDrilling { get; private set; }
      [XmlIgnore]
      public float MaximumRequiredElectricPowerFilling { get; private set; }
      [XmlIgnore]
      public float MaximumRequiredElectricPowerTransport { get; private set; }

      internal BoundingBoxD AreaBoundingBox
      {
         get
         {
            return _AreaBoundingBox;
         }
      }

      public SyncBlockSettings() : this(null)
      {

      }

      public SyncBlockSettings(NanobotDrillSystemBlock system)
      {
         _DrillPriority = string.Empty;
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
         return MyAPIGateway.Utilities.SerializeToXML(this); ;
      }

      public static SyncBlockSettings Load(NanobotDrillSystemBlock system, Guid guid, NanobotDrillSystemDrillPriorityHandling blockDrillPriority, NanobotDrillSystemComponentPriorityHandling componentCollectPriority)
      {
         var storage = system.Entity.Storage;
         string data;
         SyncBlockSettings settings = null;
         if (storage != null && storage.TryGetValue(guid, out data))
         {
            try
            {
               settings = MyAPIGateway.Utilities.SerializeFromXML<SyncBlockSettings>(data);
               if (settings != null)
               {
                  settings.RecalcAreaBoundigBox();
                  //Retrieve current settings or default if DrillPriority/ComponentCollectPriority was empty
                  blockDrillPriority.SetEntries(settings.DrillPriority);
                  settings.DrillPriority = blockDrillPriority.GetEntries();
                  blockDrillPriority.UpdateHash();

                  componentCollectPriority.SetEntries(settings.ComponentCollectPriority);
                  settings.ComponentCollectPriority = componentCollectPriority.GetEntries();
                  componentCollectPriority.UpdateHash();

                  settings.Changed = 0;
                  settings._LastStored = MyAPIGateway.Session.ElapsedPlayTime.Add(TimeSpan.FromSeconds(60));
                  settings._LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;
                  return settings;
               }
            }
            catch (Exception ex)
            {
               Mod.Log.Write("SyncBlockSettings: Exception: " + ex);
            }
         }

         settings = new SyncBlockSettings(system);
         blockDrillPriority.SetEntries(settings.DrillPriority);
         componentCollectPriority.SetEntries(settings.ComponentCollectPriority);
         settings.Changed = 0;
         return settings;
      }

      public void AssignReceived(SyncBlockSettings newSettings, NanobotDrillSystemDrillPriorityHandling drillPriority, NanobotDrillSystemComponentPriorityHandling componentCollectPriority)
      {
         _Flags = newSettings._Flags;

         _AreaOffset = newSettings.AreaOffset;
         _AreaSize = newSettings.AreaSize;

         _DrillPriority = newSettings.DrillPriority;
         _ComponentCollectPriority = newSettings.ComponentCollectPriority;

         _SoundVolume = newSettings.SoundVolume;
         _WorkMode = newSettings.WorkMode;
         _RemoteControlledBy = newSettings.RemoteControlledBy;

         RecalcAreaBoundigBox();
         drillPriority.SetEntries(DrillPriority);
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
         return (Changed & 1u) != 0 && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastTransmitted) >= TimeSpan.FromSeconds(2);
      }

      private void RecalcAreaBoundigBox()
      {
         _AreaBoundingBox = new BoundingBoxD(new Vector3D(-AreaSize.X/2, -AreaSize.Y/2, -AreaSize.Z/2), new Vector3D(AreaSize.X/2, AreaSize.Y/2, AreaSize.Z/2));
      }

      public bool RemoteControlCharacter(out IMyCharacter character)
      {
         if (_RemoteControlledByCharacter != null && !Utils.IsCharacterPlayerAndActive(_RemoteControlledByCharacter))
         {
            _RemoteControlledByCharacter = null;
         }

         if (RemoteControlledBy.HasValue && _RemoteControlledByCharacter == null)
         {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, (player) => { return player.IdentityId == RemoteControlledBy && Utils.IsCharacterPlayerAndActive(player.Character); });
            if (players.Count > 0)
            {
               _RemoteControlledByCharacter = players[0].Character;
            }
         }

         character = _RemoteControlledByCharacter;
         return RemoteControlledBy.HasValue;
      }

      public void CheckLimits(NanobotDrillSystemBlock system, bool init)
      {
         var scale = (system != null && system.Drill != null ? (system.Drill.BlockDefinition.SubtypeName.Contains("Large") ? 1f : 3f) : 1f);

         if (NanobotDrillSystemMod.Settings.Drill.AreaOffsetFixed || init)
         {
            MaximumOffset = 0;
            AreaOffset = new Vector3(0, 0, 0);
         } else {
            MaximumOffset = (int)Math.Ceiling(NanobotDrillSystemMod.Settings.MaximumOffset / scale);
            if (AreaOffset.X > MaximumOffset || init) AreaOffset = new Vector3(init ? 0 :(float)MaximumOffset, AreaOffset.Y, AreaOffset.Z);
            else if(AreaOffset.X < -MaximumOffset || init) AreaOffset = new Vector3(init ? 0 : (float)-MaximumOffset, AreaOffset.Y, AreaOffset.Z);

            if (AreaOffset.Y > MaximumOffset || init) AreaOffset = new Vector3(AreaOffset.X, init ? 0 : (float)MaximumOffset, AreaOffset.Z);
            else if (AreaOffset.Y < -MaximumOffset || init) AreaOffset = new Vector3(AreaOffset.X, init ? 0 : (float)-MaximumOffset, AreaOffset.Z);

            if (AreaOffset.Z > MaximumOffset || init) AreaOffset = new Vector3(AreaOffset.X, AreaOffset.Y, init ? 0 : (float)MaximumOffset);
            else if (AreaOffset.Z < -MaximumOffset || init) AreaOffset = new Vector3(AreaOffset.X, AreaOffset.Y, init ? 0 : (float)-MaximumOffset);
         }

         MaximumRange = (int)Math.Ceiling(NanobotDrillSystemMod.Settings.Range / scale);
         if (NanobotDrillSystemMod.Settings.Drill.AreaSizeFixed || init)
         {
            AreaSize = new Vector3(MaximumRange, MaximumRange, MaximumRange);   
         } else {
            if (AreaSize.X > MaximumRange || init) AreaSize = new Vector3(MaximumRange, AreaSize.Y, AreaSize.Z);
            if (AreaSize.Y > MaximumRange || init) AreaSize = new Vector3(AreaSize.X, MaximumRange, AreaSize.Z);
            if (AreaSize.Z > MaximumRange || init) AreaSize = new Vector3(AreaSize.X, AreaSize.Y, MaximumRange);
         }

         MaximumRequiredElectricPowerStandby = NanobotDrillSystemMod.Settings.MaximumRequiredElectricPowerStandby / scale;
         MaximumRequiredElectricPowerTransport = NanobotDrillSystemMod.Settings.MaximumRequiredElectricPowerTransport / scale;
         MaximumRequiredElectricPowerDrilling = NanobotDrillSystemMod.Settings.Drill.MaximumRequiredElectricPowerDrilling / scale;
         MaximumRequiredElectricPowerFilling = NanobotDrillSystemMod.Settings.Drill.MaximumRequiredElectricPowerFilling / scale;

         var maxMultiplier = Math.Max(NanobotDrillSystemMod.Settings.Drill.DrillingMultiplier, NanobotDrillSystemMod.Settings.Drill.FillingMultiplier);
         TransportSpeed = maxMultiplier * NanobotDrillSystemBlock.DRILL_TRANSPORTSPEED_METER_PER_SECOND_DEFAULT * Math.Min(NanobotDrillSystemMod.Settings.Range / NanobotDrillSystemBlock.DRILL_RANGE_DEFAULT_IN_M, 4.0f);

         if (NanobotDrillSystemMod.Settings.Drill.ShowAreaFixed || init) Flags = (Flags & ~Settings.ShowArea);
         if (NanobotDrillSystemMod.Settings.Drill.RemoteControlledFixed || init) RemoteControlledBy = null;
         if (NanobotDrillSystemMod.Settings.Drill.CollectIfIdleFixed || init) Flags = (Flags & ~Settings.ComponentCollectIfIdle) | (NanobotDrillSystemMod.Settings.Drill.CollectIfIdleDefault ? Settings.ComponentCollectIfIdle : 0);
         if (NanobotDrillSystemMod.Settings.Drill.SoundVolumeFixed || init) SoundVolume = NanobotDrillSystemMod.Settings.Drill.SoundVolumeDefault;
         if (NanobotDrillSystemMod.Settings.Drill.ScriptControllFixed || init) Flags = (Flags & ~Settings.ScriptControlled);

         if ((NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes & WorkMode) == 0 || init)
         {
            if ((NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes & NanobotDrillSystemMod.Settings.Drill.WorkModeDefault) != 0)
            {
               WorkMode = NanobotDrillSystemMod.Settings.Drill.WorkModeDefault;
            }
            else
            {
               if ((NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes & WorkModes.Drill) != 0) WorkMode = WorkModes.Drill;
               else if ((NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes & WorkModes.Collect) != 0) WorkMode = WorkModes.Collect;
               else if ((NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes & WorkModes.Fill) != 0) WorkMode = WorkModes.Fill;
            }
         }
      }
   }

   /// <summary>
   /// Current State of block
   /// </summary>
   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class SyncBlockState
   {
      public const int MaxSyncItems = 20;
      private bool _Ready;
      private bool _Drilling;
      private bool _NeedDrilling;
      private bool _Filling;
      private bool _NeedFilling;
      private bool _Transporting;
      private bool _InventoryFull;
      private bool _MissingMaterial;
      private bool _CharacterInWorkingArea;
      private object _CurrentDrillingEntity;
      private object _CurrentFillingEntity;
      private List<SyncTargetVoxelData> _PossibleDrillTargetsSync;
      private List<SyncTargetVoxelData> _PossibleFillTargetsSync;
      private List<SyncTargetEntityData> _PossibleFloatingTargetsSync;

      private Vector3D? _NextTransportTarget;
      private Vector3D? _CurrentTransportTarget;
      private Vector3D? _LastTransportTarget;
      private bool _NextTransportIsPick;
      private bool _CurrentTransportIsPick;
      private TimeSpan _NextTransportTime = TimeSpan.Zero;
      private TimeSpan _CurrentTransportTime = TimeSpan.Zero;
      private TimeSpan _CurrentTransportStartTime = TimeSpan.Zero;

      public bool Changed { get; private set; }
      public override string ToString()
      {
         return string.Format("Ready={0}, Drilling={1}/{2}, Filling={3}/{4}, PossibleDrillTargetsCount={5}, PossibleFloatingTargetsCount={6}, CurrentDrilling={7}, CurrentFilling={8}, CurrentTransportTarget={9}",
            Ready, Drilling, NeedDrilling, Filling, NeedFilling, PossibleDrillTargetsSync != null ? PossibleDrillTargetsSync.Count : -1, PossibleFloatingTargetsSync != null ? PossibleFloatingTargetsSync.Count : -1,
            Logging.BlockName(CurrentDrillingEntity, Logging.BlockNameOptions.None), Logging.BlockName(CurrentFillingEntity, Logging.BlockNameOptions.None), (CurrentTransportTarget != null ? CurrentTransportTarget.ToString() : "NULL"));
      }

      [ProtoMember(10)]
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

      [ProtoMember(20)]
      public bool Drilling
      {
         get { return _Drilling; }
         set
         {
            if (value != _Drilling)
            {
               _Drilling = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(30)]
      public bool NeedDrilling
      {
         get { return _NeedDrilling; }
         set
         {
            if (value != _NeedDrilling)
            {
               _NeedDrilling = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(40)]
      public bool Filling
      {
         get { return _Filling; }
         set
         {
            if (value != _Filling)
            {
               _Filling = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(50)]
      public bool NeedFilling
      {
         get { return _NeedFilling; }
         set
         {
            if (value != _NeedFilling)
            {
               _NeedFilling = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(60)]
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

      [ProtoMember(70)]
      public TimeSpan LastTransmitted { get; set; }

      public object CurrentDrillingEntity
      {
         get { return _CurrentDrillingEntity; }
         set
         {
            if (value != _CurrentDrillingEntity)
            {
               _CurrentDrillingEntity = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(100)]
      public SyncTargetVoxelData CurrentDrillingEntitySync
      {
         get
         {
            return SyncTargetVoxelData.GetSyncItem(_CurrentDrillingEntity as TargetVoxelData);
         }
         set
         {
            CurrentDrillingEntity = SyncTargetVoxelData.GetItem(value);
         }
      }

      public object CurrentFillingEntity
      {
         get { return _CurrentFillingEntity; }
         set
         {
            if (value != _CurrentFillingEntity)
            {
               _CurrentFillingEntity = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(110)]
      public SyncTargetVoxelData CurrentFillingEntitySync
      {
         get
         {
            return SyncTargetVoxelData.GetSyncItem(_CurrentFillingEntity as TargetVoxelData);
         }
         set
         {
            CurrentDrillingEntity = SyncTargetVoxelData.GetItem(value);
         }
      }

      [ProtoMember(120)]
      public Vector3D? NextTransportTarget
      {
         get { return _NextTransportTarget; }
         set
         {
            if (value != _NextTransportTarget)
            {
               _NextTransportTarget = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(140)]
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

      [ProtoMember(160)]
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

      [ProtoMember(180)]
      public bool NextTransportIsPick
      {
         get { return _NextTransportIsPick; }
         set
         {
            if (value != _NextTransportIsPick)
            {
               _NextTransportIsPick = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(200)]
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

      [ProtoMember(250)]
      public TimeSpan NextTransportTime
      {
         get { return _NextTransportTime; }
         set
         {
            if (value != _NextTransportTime)
            {
               _NextTransportTime = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(300)]
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

      [ProtoMember(310)]
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

      [ProtoMember(320)]
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

      [ProtoMember(330)]
      public bool MissingMaterial
      {
         get { return _MissingMaterial; }
         set
         {
            if (value != _MissingMaterial)
            {
               _MissingMaterial = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(340)]
      public bool CharacterInWorkingArea
      {
         get { return _CharacterInWorkingArea; }
         set
         {
            if (value != _CharacterInWorkingArea)
            {
               _CharacterInWorkingArea = value;
               Changed = true;
            }
         }
      }

      public TargetVoxelDataHashList PossibleDrillTargets { get; private set; }

      [ProtoMember(400)]
      public List<SyncTargetVoxelData> PossibleDrillTargetsSync
      {
         get
         {
            if (_PossibleDrillTargetsSync == null)
            {
               if (PossibleDrillTargets != null) _PossibleDrillTargetsSync = PossibleDrillTargets.GetSyncList();
               else _PossibleDrillTargetsSync = new List<SyncTargetVoxelData>();
            }
            return _PossibleDrillTargetsSync;
         }
      }

      public TargetVoxelDataHashList PossibleFillTargets { get; private set; }

      [ProtoMember(410)]
      public List<SyncTargetVoxelData> PossibleFillTargetsSync
      {
         get
         {
            if (_PossibleFillTargetsSync == null)
            {
               if (PossibleFillTargets != null) _PossibleFillTargetsSync = PossibleFillTargets.GetSyncList();
               else _PossibleFillTargetsSync = new List<SyncTargetVoxelData>();
            }
            return _PossibleFillTargetsSync;
         }
      }

      public TargetEntityDataHashList PossibleFloatingTargets { get; private set; }

      [ProtoMember(420)]
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
         PossibleDrillTargets = new TargetVoxelDataHashList();
         PossibleFillTargets = new TargetVoxelDataHashList();
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
         _PossibleDrillTargetsSync = null;
         _PossibleFillTargetsSync = null;
         _PossibleFloatingTargetsSync = null;
         LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;
         Changed = false;
         return this;
      }

      internal void AssignReceived(SyncBlockState newState)
      {
         _Ready = newState.Ready;
         _Drilling = newState.Drilling;
         _NeedDrilling = newState.NeedDrilling;
         _Filling = newState.Filling;
         _NeedFilling = newState.NeedFilling;
         _Transporting = newState.Transporting;
         _InventoryFull = newState.InventoryFull;
         _MissingMaterial = newState.MissingMaterial;
         _CharacterInWorkingArea = newState.CharacterInWorkingArea;

         _CurrentDrillingEntity = SyncTargetVoxelData.GetItem(newState.CurrentDrillingEntitySync);
         _CurrentFillingEntity = SyncTargetVoxelData.GetItem(newState.CurrentFillingEntitySync);

         _CurrentTransportIsPick = newState.CurrentTransportIsPick;
         _CurrentTransportTarget = newState.CurrentTransportTarget;
         _CurrentTransportTime = newState.CurrentTransportTime;
         _CurrentTransportStartTime = MyAPIGateway.Session.ElapsedPlayTime - (newState.LastTransmitted - newState.CurrentTransportStartTime);

         _NextTransportIsPick = newState.NextTransportIsPick;
         _NextTransportTarget = newState.NextTransportTarget;
         _NextTransportTime = newState.NextTransportTime;

         PossibleDrillTargets.Clear();
         var possibleDrillTargetsSync = newState.PossibleDrillTargetsSync;
         if (possibleDrillTargetsSync != null) foreach (var item in possibleDrillTargetsSync) PossibleDrillTargets.Add(new TargetVoxelData(SyncEntityId.GetItemAs<MyVoxelBase>(item.EntityData.Entity), item.Id, item.EntityData.Distance, Vector3I.Zero, Vector3I.Zero, item.WorldPos, item.MaterialDef, item.Amount));

         PossibleFillTargets.Clear();
         var possibleFillTargetsSync = newState.PossibleFillTargetsSync;
         if (possibleFillTargetsSync != null) foreach (var item in possibleFillTargetsSync) PossibleFillTargets.Add(new TargetVoxelData(SyncEntityId.GetItemAs<MyVoxelBase>(item.EntityData.Entity), item.Id, item.EntityData.Distance, Vector3I.Zero, Vector3I.Zero, item.WorldPos, item.MaterialDef, item.Amount));

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

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgModCommand
   {
      [ProtoMember(1)]
      public ulong SteamId { get; set; }

      [ProtoMember(2)]
      public string Command { get; set; }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgModDataRequest
   {
      [ProtoMember(1)]
      public ulong SteamId { get; set; }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgModSettings
   {
      [ProtoMember(1)]
      public SyncModSettings Settings { get; set; }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgBlockDataRequest
   {
      [ProtoMember(1)]
      public ulong SteamId { get; set; }
      [ProtoMember(2)]
      public long EntityId { get; set; }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgBlockSettings
   {
      [ProtoMember(1)]
      public long EntityId { get; set; }
      [ProtoMember(2)]
      public SyncBlockSettings Settings { get; set; }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgBlockState
   {
      [ProtoMember(1)]
      public long EntityId { get; set; }
      [ProtoMember(2)]
      public SyncBlockState State { get; set; }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgVoxelBoxesFillRemove
   {
      [ProtoMember(10)]
      public List<SyncTargetVoxelBoxesRemoveFillData> VoxelBoxesData { get; set; }
   }

   /// <summary>
   /// Hash list for TargetEntityData
   /// </summary>
   public class TargetEntityDataHashList : HashList<TargetEntityData, SyncTargetEntityData>
   {
      public override List<SyncTargetEntityData> GetSyncList()
      {
         var result = new List<SyncTargetEntityData>();
         var idx = 0;
         foreach (var item in this)
         {
            result.Add(new SyncTargetEntityData() { Entity = SyncEntityId.GetSyncId(item.Entity), Distance = item.Distance });
            idx++;
            if (idx > SyncBlockState.MaxSyncItems) break;
         }
         return result;
      }

      public override void RebuildHash()
      {
         uint hash = 0;
         var idx = 0;
         lock (this)
         {
            foreach (var entry in this)
            {
               hash ^= UtilsSynchronization.RotateLeft((uint)entry.GetHashCode(), idx + 1);
               idx++;
               if (idx >= SyncBlockState.MaxSyncItems) break;
            }
            CurrentCount = this.Count;
            CurrentHash = hash;
         }
      }
   }

   /// <summary>
   /// Hash list for TargetEntityData
   /// </summary>
   public class TargetVoxelDataHashList : HashList<TargetVoxelData, SyncTargetVoxelData>
   {
      public override List<SyncTargetVoxelData> GetSyncList()
      {
         var result = new List<SyncTargetVoxelData>();
         var idx = 0;
         foreach (var item in this)
         {
            result.Add(SyncTargetVoxelData.GetSyncItem(item));
            idx++;
            if (idx > SyncBlockState.MaxSyncItems) break;
         }
         return result;
      }

      public override void RebuildHash()
      {
         uint hash = 0;
         var idx = 0;
         lock (this)
         {
            foreach (var entry in this)
            {
               hash ^= UtilsSynchronization.RotateLeft((uint)entry.Voxel.GetHashCode(), idx + 1);
               idx++;
               if (idx >= SyncBlockState.MaxSyncItems) break;
            }
            CurrentCount = this.Count;
            CurrentHash = hash;
         }
      }
   }

   public class DefinitionIdHashDictionary : HashDictionary<MyDefinitionId, int, SyncComponents>
   {
      public override List<SyncComponents> GetSyncList()
      {
         var result = new List<SyncComponents>();
         var idx = 0;
         foreach (var item in this)
         {
            result.Add(new SyncComponents() { Component = item.Key, Amount = item.Value });
            idx++;
            if (idx > SyncBlockState.MaxSyncItems) break;
         }
         return result;
      }

      public override void RebuildHash()
      {
         uint hash = 0;
         var idx = 0;
         lock (this)
         {
            foreach (var entry in this)
            {
               hash ^= UtilsSynchronization.RotateLeft((uint)entry.GetHashCode(), idx + 1);
               idx++;
               if (idx >= SyncBlockState.MaxSyncItems) break;
            }
            CurrentCount = Count;
            CurrentHash = hash;
         }
      }
   }

   public class TargetEntityData
   {
      public IMyEntity Entity { get; internal set; }
      public double Distance { get; internal set; }
      public bool Ignore { get; set; }
      public TargetEntityData(IMyEntity entity, double distance)
      {
         Entity = entity;
         Distance = distance;
         Ignore = false;
      }

      public override int GetHashCode()
      {
         return Entity.GetHashCode() + Ignore.GetHashCode();
      }
   }

   public class TargetVoxelData : TargetEntityData
   {
      public MyVoxelBase Voxel { get; internal set; }
      public uint Id { get; internal set; }
      public Vector3I VoxelCoordMin { get; internal set; }
      public Vector3I VoxelCoordMax { get; internal set; }
      public Vector3D WorldPos { get; internal set; }
      public MyVoxelMaterialDefinition MaterialDef { get; internal set; }

      public Vector3D CurrentTargetPos { get; set; }
      public MyVoxelMaterialDefinition CurrentMaterialDef { get; set; }
      public float Amount { get; internal set; }
      public TargetVoxelData(MyVoxelBase voxel, uint id, double distance, Vector3I voxelCoordMin, Vector3I voxelCoordMax, Vector3D worldPos, MyVoxelMaterialDefinition materialDef, float amount) : base(voxel, distance)
      {
         Voxel = voxel;
         Id = id;
         VoxelCoordMin = voxelCoordMin;
         VoxelCoordMax = voxelCoordMax;
         WorldPos = worldPos;
         MaterialDef = materialDef;
         Amount = amount;

         CurrentTargetPos = WorldPos;
         CurrentMaterialDef = materialDef;
      }

      public TargetVoxelData(MyVoxelBase voxel, uint id, double distance, Vector3I voxelCoordMin, Vector3I voxelCoordMax, Vector3D worldPos, SerializableDefinitionId materialDef, float amount) : base(voxel, distance)
      {
         Voxel = voxel;
         Id = id;
         VoxelCoordMin = voxelCoordMin;
         VoxelCoordMax = voxelCoordMax;
         WorldPos = worldPos;

         var materialDefObject = MyDefinitionManager.Static.GetVoxelMaterialDefinition(materialDef.SubtypeName);

         MaterialDef = materialDefObject;
         Amount = amount;

         CurrentTargetPos = WorldPos;
         CurrentMaterialDef = materialDefObject;
      }

      public override int GetHashCode()
      {
         return base.GetHashCode() | (int)UtilsSynchronization.RotateLeft((uint)WorldPos.GetHashCode(), 4);
      }

      override public string ToString()
      {
         return string.Format("{0} Id={1} {2}={3} (Dist={4}, Min={5}, Max={6})", Voxel.ToString(), Id, MaterialDef, Amount, Distance, VoxelCoordMin, VoxelCoordMax);
      }
   }

   [ProtoContract(UseProtoMembersOnly = true)]
   public class SyncTargetVoxelData
   {
      [ProtoMember(1)]
      public SyncTargetEntityData EntityData { get; set; }
      [ProtoMember(30)]
      public uint Id { get; set; }
      [ProtoMember(31)]
      public Vector3D WorldPos { get; set; }
      [ProtoMember(32)]
      public SerializableDefinitionId MaterialDef { get; set; }
      [ProtoMember(33)]
      public float Amount { get; set; }

      [ProtoMember(40)]
      public Vector3D CurrentTargetPos { get; set; }

      public static SyncTargetVoxelData GetSyncItem(TargetVoxelData item)
      {
         if (item == null) return null;
         return new SyncTargetVoxelData()
         {
            EntityData = new SyncTargetEntityData() { Entity = SyncEntityId.GetSyncId(item.Voxel), Distance = item.Distance },
            MaterialDef = item.MaterialDef != null ? item.MaterialDef.Id : new MyDefinitionId(),
            Amount = item.Amount,
            WorldPos = item.WorldPos,
            Id = item.Id,
            CurrentTargetPos = item.CurrentTargetPos
         };
      }

      public static TargetVoxelData GetItem(SyncTargetVoxelData item)
      {
         if (item == null) return null;
         var targetVoxelData = new TargetVoxelData(SyncEntityId.GetItemAs<MyVoxelBase>(item.EntityData.Entity), item.Id, item.EntityData.Distance, Vector3I.MinValue, Vector3I.MinValue, item.WorldPos, item.MaterialDef, item.Amount);
         targetVoxelData.CurrentTargetPos = item.CurrentTargetPos;
         return targetVoxelData;
      }
   }

   [ProtoContract(UseProtoMembersOnly = true)]
   public class SyncTargetVoxelBoxesRemoveFillData
   {
      [ProtoMember(1)]
      public SyncEntityId Entity { get; set; }

      [ProtoMember(40)]
      public Vector3I CoordMin { get; set; }
      [ProtoMember(41)]
      public Vector3I CoordMax { get; set; }

      [ProtoMember(50)]
      public List<BoundingBoxI> Boxes { get; set; }

      [ProtoMember(60)]
      public byte Material { get; set; }
      [ProtoMember(61)]
      public long Content { get; set; } //<0 Removed >0 added
   }
}
