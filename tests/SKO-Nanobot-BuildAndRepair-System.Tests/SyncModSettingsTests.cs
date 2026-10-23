using SKONanobotBuildAndRepairSystem.Models;
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-CFG-001 / SCN-CFG-002 — ModSettings.xml clamp, migration, serialization.
    public class SyncModSettingsTests : ModTestBase
    {
        [Fact]
        public void ValidateAndClamp_ClampsEveryBoundedField()
        {
            var s = new SyncModSettings
            {
                MaxBackgroundTasks = 99,
                AssignmentTtlSeconds = 1,
                StaggerGroupCount = 50,
                MaxGrindMsPerTick = 0,
                MaxWeldMsPerTick = 1000,
                MaxGrindsPerTick = 1000,
                MaxWeldsPerTick = -5,
            };

            Assert.True(SyncModSettings.ValidateAndClamp(s));

            Assert.InRange(s.MaxBackgroundTasks, Mod.MaxBackgroundTasks_Min, Mod.MaxBackgroundTasks_Max);
            Assert.InRange(s.AssignmentTtlSeconds, 2, 30);
            Assert.InRange(s.StaggerGroupCount, 0, 10);
            Assert.Equal((int)Mod.MaxGrindMsPerTickDefault, s.MaxGrindMsPerTick); // < 1 means "default", not 1
            Assert.InRange(s.MaxWeldMsPerTick, 1, 100);
            Assert.InRange(s.MaxGrindsPerTick, 0, 100);
            Assert.InRange(s.MaxWeldsPerTick, 0, 100);
        }

        [Fact]
        public void ValidateAndClamp_SecondPassIsNoOp()
        {
            var s = new SyncModSettings { MaxBackgroundTasks = 99 };
            SyncModSettings.ValidateAndClamp(s);
            Assert.False(SyncModSettings.ValidateAndClamp(s));
        }

        [Fact]
        public void ValidateAndClamp_FoldsDeprecatedGrindIfWeldGetStuck()
        {
            var s = new SyncModSettings();
            s.Welder.AllowedWorkModes = WorkModes.GrindIfWeldGetStuck;
            s.Welder.WorkModeDefault = WorkModes.GrindIfWeldGetStuck;
            s.Welder.AllowedGrindJanitorRelations = 0;

            SyncModSettings.ValidateAndClamp(s);

            Assert.Equal(0, (int)(s.Welder.AllowedWorkModes & WorkModes.GrindIfWeldGetStuck));
            Assert.NotEqual(0, (int)(s.Welder.AllowedWorkModes & WorkModes.WeldBeforeGrind));
            Assert.Equal(WorkModes.WeldBeforeGrind, s.Welder.WorkModeDefault);
            Assert.NotEqual(0, (int)s.Welder.AllowedGrindJanitorRelations); // BUG-093
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("ko", "ko")]
        [InlineData(" KO ", "ko")]
        [InlineData("xx", "")]
        public void ValidateAndClamp_LanguageOverride(string input, string expected)
        {
            var s = new SyncModSettings { LanguageOverride = input };
            SyncModSettings.ValidateAndClamp(s);
            Assert.Equal(expected, s.LanguageOverride);
        }

        [Fact]
        public void AdjustSettings_MigratesOldVersion_AndIsIdempotent()
        {
            var s = new SyncModSettings { Version = 0 };
            s.Welder.AllowedSearchModes = 0;
            s.Welder.AllowedWorkModes = 0;
            s.Welder.WeldingMultiplier = 0;
            s.Welder.GrindingMultiplier = 0;
            s.Welder.AllowedGrindJanitorRelations = 0;

            Assert.True(SyncModSettings.AdjustSettings(s));

            var current = (int)typeof(SyncModSettings).GetField("CurrentSettingsVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetRawConstantValue();
            Assert.Equal(current, s.Version);
            Assert.NotEqual(0, (int)s.Welder.AllowedSearchModes);
            Assert.NotEqual(0, (int)s.Welder.AllowedWorkModes);
            Assert.Equal(1f, s.Welder.WeldingMultiplier);
            Assert.Equal(1f, s.Welder.GrindingMultiplier);
            Assert.NotEqual(0, (int)s.Welder.AllowedGrindJanitorRelations);
            Assert.False(SyncModSettings.AdjustSettings(s)); // already current
        }

        [Fact]
        public void AdjustSettings_HighMultiplierMeansFastWorkSpeed()
        {
            // Old configs used multiplier > 10 to switch to Update10; keep that behaviour.
            var s = new SyncModSettings { Version = 6 };
            s.Welder.WeldingMultiplier = 20;
            s.Welder.WorkSpeed = 1;
            SyncModSettings.AdjustSettings(s);
            Assert.Equal(10, s.Welder.WorkSpeed);
        }

        [Fact]
        public void XmlRoundTrip_KeepsValues()
        {
            var s = new SyncModSettings { MaxSystemsPerTargetGrid = 7, LanguageOverride = "ko", DebugMode = true };
            s.Welder.WeldingMultiplier = 2.5f;

            var serializer = new XmlSerializer(typeof(SyncModSettings));
            var writer = new StringWriter();
            serializer.Serialize(writer, s);
            var back = (SyncModSettings)serializer.Deserialize(new StringReader(writer.ToString()));

            Assert.Equal(7, back.MaxSystemsPerTargetGrid);
            Assert.Equal("ko", back.LanguageOverride);
            Assert.True(back.DebugMode);
            Assert.Equal(2.5f, back.Welder.WeldingMultiplier);
        }

        [Fact]
        public void XmlWithoutNewFields_LoadsDefaults()
        {
            // A ModSettings.xml written by an older version has no LanguageOverride element.
            const string oldXml = "<?xml version=\"1.0\"?><SyncModSettings><MaxSystemsPerTargetGrid>5</MaxSystemsPerTargetGrid></SyncModSettings>";
            var back = (SyncModSettings)new XmlSerializer(typeof(SyncModSettings)).Deserialize(new StringReader(oldXml));
            SyncModSettings.ValidateAndClamp(back);
            Assert.Equal(5, back.MaxSystemsPerTargetGrid);
            Assert.Equal("", back.LanguageOverride);
        }
    }
}
