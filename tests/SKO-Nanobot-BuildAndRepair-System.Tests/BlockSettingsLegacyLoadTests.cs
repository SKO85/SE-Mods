using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.IO;
using System.Xml.Serialization;
using Xunit;
using Flag = SKONanobotBuildAndRepairSystem.Models.SyncBlockSettings.Settings;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-CFG-001 — block settings from older saves: SyncBlockSettings.Load rewrites renamed values
    // (MigrateLegacyXml) before the game's XmlSerializer reads them; a deprecated work mode maps on read.
    // Fixtures are today's XML with the old spellings put back in.
    public class BlockSettingsLegacyLoadTests : ModTestBase
    {
        private static readonly XmlSerializer Xml = new XmlSerializer(typeof(SyncBlockSettings));

        private static string Serialize(SyncBlockSettings s)
        {
            using (var w = new StringWriter()) { Xml.Serialize(w, s); return w.ToString(); }
        }

        private static SyncBlockSettings Deserialize(string xml)
        {
            using (var r = new StringReader(xml)) return (SyncBlockSettings)Xml.Deserialize(r);
        }

        private static SyncBlockSettings LoadStored(string xml)
        {
            return Deserialize(SyncBlockSettings.MigrateLegacyXml(xml));
        }

        private static string CurrentXml(Action<SyncBlockSettings> edit)
        {
            var s = new SyncBlockSettings(null);
            edit(s);
            return Serialize(s);
        }

        [Fact]
        public void CurrentXml_RoundTrips()
        {
            var xml = CurrentXml(s =>
            {
                s.Flags = Flag.AllowBuild | Flag.GrindNearFirst | Flag.UseIgnoreColor;
                s.WorkMode = WorkModes.GrindOnly;
                s.SearchMode = SearchModes.BoundingBox;
                s.WeldOptions = AutoWeldOptions.WeldFunctional;
                s.UseGrindJanitorOn = AutoGrindRelation.Enemies | AutoGrindRelation.NoOwnership;
                s.WeldPriority = "ArmorBlock;True|Conveyor;False";
            });

            var s2 = LoadStored(xml);

            Assert.Equal(Flag.AllowBuild | Flag.GrindNearFirst | Flag.UseIgnoreColor, s2.Flags & (Flag.AllowBuild | Flag.GrindNearFirst | Flag.UseIgnoreColor));
            Assert.Equal(WorkModes.GrindOnly, s2.WorkMode);
            Assert.Equal(SearchModes.BoundingBox, s2.SearchMode);
            Assert.Equal(AutoWeldOptions.WeldFunctional, s2.WeldOptions);
            Assert.Equal(AutoGrindRelation.Enemies | AutoGrindRelation.NoOwnership, s2.UseGrindJanitorOn);
            Assert.Equal("ArmorBlock;True|Conveyor;False", s2.WeldPriority);
        }

        [Fact]
        public void OldFlagName_GrindColorNearFirst_IsRead()
        {
            var xml = CurrentXml(s => s.Flags = Flag.AllowBuild | Flag.GrindNearFirst).Replace("GrindNearFirst", "GrindColorNearFirst");
            Assert.Throws<InvalidOperationException>(() => Deserialize(xml)); // the fixture really is unreadable as is

            var s2 = LoadStored(xml);

            Assert.Equal(Flag.GrindNearFirst, s2.Flags & Flag.GrindNearFirst);
            Assert.Equal(Flag.AllowBuild, s2.Flags & Flag.AllowBuild);
        }

        [Theory]
        [InlineData("<WeldOptions />", AutoWeldOptions.WeldFull)]
        [InlineData("<WeldOptions></WeldOptions>", AutoWeldOptions.WeldFull)]
        [InlineData("<WeldOptions>FunctionalOnly</WeldOptions>", AutoWeldOptions.WeldFunctional)]
        public void OldWeldOptions_AreMapped(string legacy, AutoWeldOptions expected)
        {
            var current = CurrentXml(s => s.WeldOptions = AutoWeldOptions.WeldSkeleton);
            var xml = current.Replace("<WeldOptions>WeldSkeleton</WeldOptions>", legacy);
            Assert.NotEqual(current, xml);

            Assert.Equal(expected, LoadStored(xml).WeldOptions);
        }

        [Fact]
        public void DeprecatedWorkMode_GrindIfWeldGetStuck_ReadsAsWeldBeforeGrind()
        {
            var xml = CurrentXml(s => s.WorkMode = WorkModes.GrindOnly).Replace("<WorkMode>GrindOnly</WorkMode>", "<WorkMode>GrindIfWeldGetStuck</WorkMode>");

            Assert.Equal(WorkModes.WeldBeforeGrind, LoadStored(xml).WorkMode);
        }

        [Fact]
        public void MigrateLegacyXml_LeavesCurrentXmlUnchanged()
        {
            var xml = CurrentXml(s => { s.Flags = Flag.GrindNearFirst; s.WeldOptions = AutoWeldOptions.WeldFull; });
            Assert.Equal(xml, SyncBlockSettings.MigrateLegacyXml(xml));
            Assert.Null(SyncBlockSettings.MigrateLegacyXml(null));
        }
    }
}
