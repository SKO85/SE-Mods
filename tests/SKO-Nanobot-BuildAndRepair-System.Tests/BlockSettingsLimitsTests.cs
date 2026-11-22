using SKONanobotBuildAndRepairSystem.Models;
using System;
using VRageMath;
using Xunit;
using Flag = SKONanobotBuildAndRepairSystem.Models.SyncBlockSettings.Settings;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-CFG-003 — SyncBlockSettings.CheckLimits: every admin *Fixed / *Default pair forces the block setting,
    // allowed search / work modes fall back in order, the area is clamped and NaN / negative values sanitised.
    // Runs with system = null (large-grid scale 1), as a client message or world load would reach it.
    public class BlockSettingsLimitsTests : ModTestBase
    {
        private static SyncModSettingsWelder W { get { return Mod.Settings.Welder; } }

        private static SyncBlockSettings Fresh()
        {
            return new SyncBlockSettings(null); // init pass: defaults
        }

        private static void SetWelder(string name, object value)
        {
            typeof(SyncModSettingsWelder).GetProperty(name).SetValue(W, value);
        }

        // ---------------------------------------------------------------- flags forced by *Fixed

        [Theory]
        [InlineData("AllowBuildFixed", "AllowBuildDefault", Flag.AllowBuild)]
        [InlineData("UseIgnoreColorFixed", "UseIgnoreColorDefault", Flag.UseIgnoreColor)]
        [InlineData("UseGrindColorFixed", "UseGrindColorDefault", Flag.UseGrindColor)]
        [InlineData("PushIngotOreImmediatelyFixed", "PushIngotOreImmediatelyDefault", Flag.PushIngotOreImmediately)]
        [InlineData("PushComponentImmediatelyFixed", "PushComponentImmediatelyDefault", Flag.PushComponentImmediately)]
        [InlineData("PushItemsImmediatelyFixed", "PushItemsImmediatelyDefault", Flag.PushItemsImmediately)]
        [InlineData("CollectIfIdleFixed", "CollectIfIdleDefault", Flag.ComponentCollectIfIdle)]
        public void FixedFlag_IsForcedToTheDefault_BothWays(string fixedName, string defaultName, Flag flag)
        {
            var s = Fresh();
            SetWelder(fixedName, true);

            SetWelder(defaultName, false);
            s.Flags |= flag;
            s.CheckLimits(null, false);
            Assert.Equal((Flag)0, s.Flags & flag);

            SetWelder(defaultName, true);
            s.Flags &= ~flag;
            s.CheckLimits(null, false);
            Assert.Equal(flag, s.Flags & flag);
        }

        [Theory]
        [InlineData(Flag.AllowBuild)]
        [InlineData(Flag.UseIgnoreColor)]
        [InlineData(Flag.UseGrindColor)]
        [InlineData(Flag.PushIngotOreImmediately)]
        [InlineData(Flag.PushComponentImmediately)]
        [InlineData(Flag.PushItemsImmediately)]
        [InlineData(Flag.ComponentCollectIfIdle)]
        [InlineData(Flag.ShowArea)]
        [InlineData(Flag.ScriptControlled)]
        public void NotFixed_PlayerChoiceIsKept(Flag flag)
        {
            var s = Fresh();
            s.Flags |= flag;
            s.CheckLimits(null, false);
            Assert.Equal(flag, s.Flags & flag);

            s.Flags &= ~flag;
            s.CheckLimits(null, false);
            Assert.Equal((Flag)0, s.Flags & flag);
        }

        [Fact]
        public void ShowAreaFixed_AndScriptControllFixed_SwitchTheirFlagsOff()
        {
            var s = Fresh();
            W.ShowAreaFixed = true;
            W.ScriptControllFixed = true;
            s.Flags |= Flag.ShowArea | Flag.ScriptControlled;

            s.CheckLimits(null, false);

            Assert.Equal((Flag)0, s.Flags & (Flag.ShowArea | Flag.ScriptControlled));
        }

        [Fact]
        public void Init_SetsGrindIgnorePriorityOrder_AndShowAreaOff()
        {
            var s = Fresh();
            Assert.Equal(Flag.GrindIgnorePriorityOrder, s.Flags & Flag.GrindIgnorePriorityOrder);
            Assert.Equal((Flag)0, s.Flags & Flag.ShowArea);
        }

        [Fact]
        public void SoundVolumeFixed_ForcesTheDefault()
        {
            var s = Fresh();
            W.SoundVolumeFixed = true;
            W.SoundVolumeDefault = 3f;
            s.SoundVolume = 90f;

            s.CheckLimits(null, false);

            Assert.Equal(3f, s.SoundVolume);
        }

        [Fact]
        public void IgnoreColorFixed_ConvertsTheDefaultHsv()
        {
            var s = Fresh();
            W.UseIgnoreColorFixed = true;
            W.UseIgnoreColorDefault = true;
            W.IgnoreColorDefault = new[] { 180f, 50f, 50f };

            s.CheckLimits(null, false);

            Assert.Equal(0.5f, s.IgnoreColor.X, 3);
        }

        // ---------------------------------------------------------------- janitor

        [Fact]
        public void JanitorFixed_ForcesRelationsAndOptions()
        {
            var s = Fresh();
            W.UseGrindJanitorFixed = true;
            W.UseGrindJanitorDefault = AutoGrindRelation.Enemies;
            W.GrindJanitorOptionsDefault = AutoGrindOptions.DisableOnly;
            s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership | AutoGrindRelation.Neutral;
            s.GrindJanitorOptions = AutoGrindOptions.HackOnly;

            s.CheckLimits(null, false);

            Assert.Equal(AutoGrindRelation.Enemies, s.UseGrindJanitorOn);
            Assert.Equal(AutoGrindOptions.DisableOnly, s.GrindJanitorOptions);
        }

        [Fact]
        public void JanitorRelations_AreMaskedByTheAllowedRelations()
        {
            var s = Fresh();
            W.AllowedGrindJanitorRelations = AutoGrindRelation.Enemies;
            s.UseGrindJanitorOn = AutoGrindRelation.Enemies | AutoGrindRelation.NoOwnership | AutoGrindRelation.Neutral;

            s.CheckLimits(null, false);

            Assert.Equal(AutoGrindRelation.Enemies, s.UseGrindJanitorOn);
        }

        // ---------------------------------------------------------------- search / work modes

        [Fact]
        public void SearchMode_NotAllowed_FallsBackToTheAllowedDefault()
        {
            var s = Fresh();
            s.SearchMode = SearchModes.BoundingBox;
            W.AllowedSearchModes = SearchModes.Grids;
            W.SearchModeDefault = SearchModes.Grids;

            s.CheckLimits(null, false);

            Assert.Equal(SearchModes.Grids, s.SearchMode);
        }

        [Fact]
        public void SearchMode_DefaultNotAllowed_FallsBackToTheAllowedOne()
        {
            var s = Fresh();
            s.SearchMode = SearchModes.Grids;
            W.AllowedSearchModes = SearchModes.BoundingBox;
            W.SearchModeDefault = SearchModes.Grids;

            s.CheckLimits(null, false);

            Assert.Equal(SearchModes.BoundingBox, s.SearchMode);
        }

        [Theory]
        [InlineData(WorkModes.WeldOnly, WorkModes.WeldOnly, WorkModes.GrindOnly, WorkModes.WeldOnly)]                         // default allowed
        [InlineData(WorkModes.GrindOnly | WorkModes.WeldOnly, WorkModes.WeldBeforeGrind, WorkModes.GrindBeforeWeld, WorkModes.WeldOnly)] // first allowed in order
        [InlineData(WorkModes.GrindBeforeWeld, WorkModes.WeldBeforeGrind, WorkModes.WeldOnly, WorkModes.GrindBeforeWeld)]
        [InlineData(WorkModes.GrindIfWeldGetStuck, WorkModes.GrindOnly, WorkModes.WeldOnly, WorkModes.WeldBeforeGrind)]       // deprecated bit = WeldBeforeGrind
        public void WorkMode_NotAllowed_FallsBack(WorkModes allowed, WorkModes defaultMode, WorkModes current, WorkModes expected)
        {
            var s = Fresh();
            s.WorkMode = current;
            W.AllowedWorkModes = allowed;
            W.WorkModeDefault = defaultMode;

            s.CheckLimits(null, false);

            Assert.Equal(expected, s.WorkMode);
        }

        [Fact]
        public void WorkMode_NothingAllowed_IsLeftAlone()
        {
            var s = Fresh();
            s.WorkMode = WorkModes.GrindOnly;
            W.AllowedWorkModes = 0;

            s.CheckLimits(null, false);

            Assert.Equal(WorkModes.GrindOnly, s.WorkMode);
        }

        [Fact]
        public void AllowedWorkMode_IsKept()
        {
            var s = Fresh();
            s.WorkMode = WorkModes.GrindOnly;

            s.CheckLimits(null, false);

            Assert.Equal(WorkModes.GrindOnly, s.WorkMode);
        }

        // ---------------------------------------------------------------- area

        [Fact]
        public void Area_IsClampedToRangeAndMaximumOffset()
        {
            var s = Fresh();
            Mod.Settings.Range = 50;          // max size 100
            Mod.Settings.MaximumOffset = 20;
            s.AreaSize = new Vector3(500, 30, 500);
            s.AreaOffset = new Vector3(99, -99, 5);

            s.CheckLimits(null, false);

            Assert.Equal(new Vector3(100, 30, 100), s.AreaSize);
            Assert.Equal(new Vector3(20, -20, 5), s.AreaOffset);
        }

        [Fact]
        public void AreaFixed_ForcesFullSizeAndNoOffset()
        {
            var s = Fresh();
            W.AreaSizeFixed = true;
            W.AreaOffsetFixed = true;
            s.AreaSize = new Vector3(10, 10, 10);
            s.AreaOffset = new Vector3(5, 5, 5);

            s.CheckLimits(null, false);

            var max = Mod.Settings.Range * 2;
            Assert.Equal(new Vector3(max, max, max), s.AreaSize);
            Assert.Equal(Vector3.Zero, s.AreaOffset);
        }

        [Fact]
        public void Area_NaNAndNegativeValues_AreSanitised()
        {
            var s = Fresh();
            s.AreaSize = new Vector3(float.NaN, -5f, float.PositiveInfinity);
            s.AreaOffset = new Vector3(float.NaN, float.NegativeInfinity, 1f);

            s.CheckLimits(null, false);

            var max = Mod.Settings.Range * 2;
            Assert.Equal(new Vector3(max, 1, max), s.AreaSize);
            Assert.Equal(new Vector3(0, 0, 1), s.AreaOffset);
        }
    }
}
