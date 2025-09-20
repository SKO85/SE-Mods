using ProtoBuf;
using System.Xml.Serialization;

namespace SKONanobotBuildAndRepairSystem.Models
{
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class SyncModSettingsWelder
    {
        [ProtoMember(1), XmlElement]
        public float MaximumRequiredElectricPowerWelding { get; set; }

        [ProtoMember(2), XmlElement]
        public float MaximumRequiredElectricPowerGrinding { get; set; }

        [ProtoMember(10), XmlElement]
        public float WeldingMultiplier { get; set; }

        [ProtoMember(11), XmlElement]
        public float GrindingMultiplier { get; set; }

        [ProtoMember(90), XmlElement]
        public SearchModes AllowedSearchModes { get; set; }

        [ProtoMember(91), XmlElement]
        public SearchModes SearchModeDefault { get; set; }

        [ProtoMember(101), XmlElement]
        public bool AllowBuildFixed { get; set; }

        [ProtoMember(102), XmlElement]
        public bool AllowBuildDefault { get; set; }

        [ProtoMember(105), XmlElement]
        public WorkModes AllowedWorkModes { get; set; }

        [ProtoMember(106), XmlElement]
        public WorkModes WorkModeDefault { get; set; }

        [ProtoMember(110), XmlElement]
        public bool UseIgnoreColorFixed { get; set; }

        [ProtoMember(111), XmlElement]
        public bool UseIgnoreColorDefault { get; set; }

        [ProtoMember(112), XmlArray]
        public float[] IgnoreColorDefault { get; set; }

        [ProtoMember(115), XmlElement]
        public bool UseGrindColorFixed { get; set; }

        [ProtoMember(116), XmlElement]
        public bool UseGrindColorDefault { get; set; }

        [ProtoMember(117), XmlArray]
        public float[] GrindColorDefault { get; set; }

        [ProtoMember(118), XmlElement]
        public bool UseGrindJanitorFixed { get; set; }

        [ProtoMember(119), XmlElement]
        public AutoGrindRelation UseGrindJanitorDefault { get; set; }

        [ProtoMember(120), XmlElement]
        public AutoGrindOptions GrindJanitorOptionsDefault { get; set; }

        [ProtoMember(121), XmlElement]
        public AutoGrindRelation AllowedGrindJanitorRelations { get; set; }

        [ProtoMember(125), XmlElement]
        public bool ShowAreaFixed { get; set; }

        [ProtoMember(130), XmlElement]
        public bool AreaSizeFixed { get; set; }

        [ProtoMember(131), XmlElement]
        public bool AreaOffsetFixed { get; set; }

        [ProtoMember(140), XmlElement]
        public bool PriorityFixed { get; set; }

        [ProtoMember(144), XmlElement]
        public bool CollectPriorityFixed { get; set; }

        [ProtoMember(145), XmlElement]
        public bool PushIngotOreImmediatelyFixed { get; set; }

        [ProtoMember(146), XmlElement]
        public bool PushIngotOreImmediatelyDefault { get; set; }

        [ProtoMember(147), XmlElement]
        public bool PushComponentImmediatelyFixed { get; set; }

        [ProtoMember(148), XmlElement]
        public bool PushComponentImmediatelyDefault { get; set; }

        [ProtoMember(149), XmlElement]
        public bool PushItemsImmediatelyFixed { get; set; }

        [ProtoMember(150), XmlElement]
        public bool PushItemsImmediatelyDefault { get; set; }

        [ProtoMember(156), XmlElement]
        public bool CollectIfIdleFixed { get; set; }

        [ProtoMember(157), XmlElement]
        public bool CollectIfIdleDefault { get; set; }

        [ProtoMember(160), XmlElement]
        public bool SoundVolumeFixed { get; set; }

        [ProtoMember(161), XmlElement]
        public float SoundVolumeDefault { get; set; }

        [ProtoMember(170), XmlElement]
        public bool ScriptControllFixed { get; set; }

        [ProtoMember(200), XmlElement]
        public VisualAndSoundEffects AllowedEffects { get; set; }

        public SyncModSettingsWelder()
        {
            MaximumRequiredElectricPowerWelding = NanobotSystem.WELDER_REQUIRED_ELECTRIC_POWER_WELDING_DEFAULT;
            MaximumRequiredElectricPowerGrinding = NanobotSystem.WELDER_REQUIRED_ELECTRIC_POWER_GRINDING_DEFAULT;

            WeldingMultiplier = 1f;
            GrindingMultiplier = 1f;

            AllowedSearchModes = SearchModes.Grids | SearchModes.BoundingBox;
            SearchModeDefault = SearchModes.Grids;

            AllowBuildFixed = false;
            AllowBuildDefault = true;

            AllowedWorkModes = WorkModes.GrindBeforeWeld | WorkModes.GrindIfWeldGetStuck | WorkModes.WeldBeforeGrind | WorkModes.WeldOnly | WorkModes.GrindOnly;
            WorkModeDefault = WorkModes.WeldBeforeGrind;

            UseIgnoreColorFixed = false;
            UseIgnoreColorDefault = true;
            IgnoreColorDefault = new float[] { 321f, 100f, 51f };

            UseGrindColorFixed = false;
            UseGrindColorDefault = true;
            GrindColorDefault = new float[] { 321f, 100f, 50f };

            UseGrindJanitorFixed = false;
            UseGrindJanitorDefault = AutoGrindRelation.Enemies | AutoGrindRelation.NoOwnership;
            GrindJanitorOptionsDefault = 0;
            AllowedGrindJanitorRelations = AutoGrindRelation.NoOwnership | AutoGrindRelation.Enemies | AutoGrindRelation.Neutral;

            ShowAreaFixed = false;
            AreaSizeFixed = false;
            AreaOffsetFixed = false;
            PriorityFixed = false;
            CollectPriorityFixed = false;

            PushIngotOreImmediatelyFixed = false;
            PushIngotOreImmediatelyDefault = true;
            PushItemsImmediatelyFixed = false;
            PushItemsImmediatelyDefault = true;
            PushComponentImmediatelyFixed = false;
            PushComponentImmediatelyDefault = true;

            CollectIfIdleDefault = false;

            SoundVolumeFixed = false;
            SoundVolumeDefault = NanobotSystem.WELDER_SOUND_VOLUME / 2;

            ScriptControllFixed = false;
            AllowedEffects = VisualAndSoundEffects.WeldingVisualEffect | VisualAndSoundEffects.WeldingSoundEffect
                     | VisualAndSoundEffects.GrindingVisualEffect | VisualAndSoundEffects.GrindingSoundEffect
                     | VisualAndSoundEffects.TransportVisualEffect;
        }
    }
}