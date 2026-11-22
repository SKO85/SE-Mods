using NSubstitute;
using System;
using System.Linq;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-API-002 — companion script behaviour, run out of game against fake blocks: groups, assembler
    // queuing, info-only / queue arguments (persisted in Storage), LCD / cockpit displays, [BaR:..] tags and
    // Custom Data overrides. The script's group config is static, so every test sets it first.
    public class CompanionScriptBehaviourTests : IDisposable
    {
        private const string BarGroup = "BuildAndRepairGroup1";
        private const string AssemblerGroup = "AssemblerGroup1";
        private const string StatusPanel = "BuildAndRepairGroup1StatusPanel";

        public CompanionScriptBehaviourTests()
        {
            CompanionScriptHost.ResetConfig();
        }

        public void Dispose()
        {
            CompanionScriptHost.ResetConfig();
        }

        private static GroupSpec DefaultGroup(params DisplaySpec[] displays)
        {
            return new GroupSpec { BarGroup = BarGroup, AssemblerGroup = AssemblerGroup, Displays = displays };
        }

        /// <summary>World with one BaR in the BaR group and two assemblers in the assembler group.</summary>
        private static ScriptRun World(out FakeBar bar, out Sandbox.ModAPI.Ingame.IMyAssembler a1, out Sandbox.ModAPI.Ingame.IMyAssembler a2)
        {
            var run = new ScriptRun();
            bar = FakeBlocks.Bar("BaR 1");
            run.Gts.Add(bar.Block);
            a1 = run.Gts.Add(FakeBlocks.Assembler("Assembler 1"));
            a2 = run.Gts.Add(FakeBlocks.Assembler("Assembler 2"));
            run.Gts.Group(BarGroup, bar.Block);
            run.Gts.Group(AssemblerGroup, a1, a2);
            return run;
        }

        // ---------------------------------------------------------------- queuing

        [Fact]
        public void MissingComponents_AreQueuedInTheAssemblerGroup()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            bar.Needs("SteelPlate", 50).Needs("Motor", 5);

            run.Start().Run();

            Assert.Equal(50, bar.QueuedAmount("SteelPlate"));
            Assert.Equal(5, bar.QueuedAmount("Motor"));
            var ids = bar.Queued.First().Item1;
            Assert.Equal(new[] { a1.EntityId, a2.EntityId }, ids);
        }

        [Fact]
        public void Queuing_AddsBackWhatAlreadySitsInAssemblerOutputs()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            bar.Needs("SteelPlate", 50);
            a1.OutputInventory.GetItemAmount(Arg.Any<VRage.Game.ModAPI.Ingame.MyItemType>()).Returns((VRage.MyFixedPoint)10);

            run.Start().Run();

            Assert.Equal(60, bar.QueuedAmount("SteelPlate"));
        }

        [Fact]
        public void SeveralBaRs_QueueTheLargestNeed_NotTheSum()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            var run = new ScriptRun();
            var b1 = FakeBlocks.Bar("BaR 1").Needs("SteelPlate", 50);
            var b2 = FakeBlocks.Bar("BaR 2").Needs("SteelPlate", 80);
            run.Gts.Add(b1.Block);
            run.Gts.Add(b2.Block);
            var a1 = run.Gts.Add(FakeBlocks.Assembler("Assembler 1"));
            run.Gts.Group(BarGroup, b1.Block, b2.Block);
            run.Gts.Group(AssemblerGroup, a1);

            run.Start().Run();

            Assert.Equal(80, b1.QueuedAmount("SteelPlate")); // EnsureQueued goes through the first BaR
        }

        [Fact]
        public void NothingMissing_QueuesNothing()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);

            run.Start().Run(times: 3);

            Assert.Empty(bar.Queued);
        }

        [Fact]
        public void AssemblersByName_AreUsedWithoutAGroup()
        {
            CompanionScriptHost.SetGroups(new GroupSpec { BarGroup = BarGroup, AssemblerNames = new[] { "Assembler 2", "Missing assembler" } });
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            bar.Needs("SteelPlate", 7);

            run.Start().Run();

            Assert.Equal(new[] { a2.EntityId }, bar.Queued.Single().Item1);
        }

        [Fact]
        public void NoAssemblers_WarnsAndQueuesNothing()
        {
            CompanionScriptHost.SetGroups(new GroupSpec { BarGroup = BarGroup, AssemblerGroup = "No such group" });
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            bar.Needs("SteelPlate", 7);

            run.Start().Run();

            Assert.Empty(bar.Queued);
            Assert.Contains("Group Assemblers group empty/wrong types", run.EchoText);
        }

        // ---------------------------------------------------------------- BaR discovery

        [Fact]
        public void BaRsByName_AreUsedWithoutAGroup()
        {
            CompanionScriptHost.SetGroups(new GroupSpec { BarNames = new[] { "BaR 1" }, AssemblerGroup = AssemblerGroup });
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            bar.Needs("SteelPlate", 3);

            run.Start().Run();

            Assert.Equal(3, bar.QueuedAmount("SteelPlate"));
        }

        [Fact]
        public void NoBaRGroup_AutoDetectsBaRsBySubtype()
        {
            CompanionScriptHost.SetGroups(new GroupSpec { BarGroup = "No such group", AssemblerGroup = AssemblerGroup });
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            bar.Needs("SteelPlate", 4);

            run.Start().Run();

            Assert.Contains("Auto-detected 1 Build&Repair systems", run.EchoText);
            Assert.Equal(4, bar.QueuedAmount("SteelPlate"));
        }

        [Fact]
        public void AutoDetect_IgnoresVanillaWelders()
        {
            CompanionScriptHost.SetGroups(new GroupSpec { AssemblerGroup = AssemblerGroup });
            var run = new ScriptRun();
            var welder = FakeBlocks.Bar("Welder", "LargeShipWelder").Needs("SteelPlate", 9); // mod adds its properties to every welder
            run.Gts.Add(welder.Block);
            run.Gts.Group(AssemblerGroup, run.Gts.Add(FakeBlocks.Assembler("Assembler 1")));

            run.Start().Run();

            Assert.Empty(welder.Queued);
            Assert.Contains("Group Repairsystems group empty/wrong types", run.EchoText);
        }

        // ---------------------------------------------------------------- arguments / info-only

        [Fact]
        public void InfoOnly_StopsQueuing_IsSaved_AndQueueResumes()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            bar.Needs("SteelPlate", 5);

            run.Start().Run("info-only");
            Assert.Empty(bar.Queued);
            Assert.Equal("info-only", run.Storage);

            run.Run(times: 3); // automatic runs (empty argument) keep the mode
            Assert.Empty(bar.Queued);

            run.Run("  QUEUE ");
            Assert.Equal(5, bar.QueuedAmount("SteelPlate"));
            Assert.Equal("", run.Storage);
        }

        [Fact]
        public void InfoOnly_SurvivesARecompile()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            bar.Needs("SteelPlate", 5);

            run.Start(storage: "info-only").Run(times: 2);

            Assert.Empty(bar.Queued);
        }

        [Fact]
        public void UnknownArgument_ChangesNothing()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            bar.Needs("SteelPlate", 5);

            run.Start().Run("hello");

            Assert.Equal(5, bar.QueuedAmount("SteelPlate"));
            Assert.Equal("", run.Storage);
        }

        // ---------------------------------------------------------------- displays

        [Fact]
        public void StatusPanelByName_ShowsTheStatusPage()
        {
            CompanionScriptHost.SetGroups(DefaultGroup(new DisplaySpec { Names = new[] { StatusPanel }, Kinds = new[] { "Status" } }));
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd(StatusPanel);
            run.Gts.Add(lcd.Block);
            bar.Needs("SteelPlate", 5).Needs("Motor", 1);

            run.Start().Run();

            Assert.Contains("Online            : True", lcd.Text);
            Assert.Contains("Missing item kinds: 2", lcd.Text);
            Assert.Contains("Auto-queuing      : Enabled (2 assemblers)", lcd.Text);
        }

        [Fact]
        public void StatusPage_ShowsInfoOnlyAndMissingAssemblers()
        {
            CompanionScriptHost.SetGroups(new GroupSpec { BarGroup = BarGroup, Displays = new[] { new DisplaySpec { Names = new[] { StatusPanel } } } });
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd(StatusPanel);
            run.Gts.Add(lcd.Block);

            run.Start().Run();
            Assert.Contains("Auto-queuing      : Disabled (no assemblers)", lcd.Text);

            run.Run("info-only");
            Assert.Contains("Auto-queuing      : Disabled (info-only)", lcd.Text);
        }

        [Fact]
        public void CockpitSurfaceByIndex_IsWritten()
        {
            CompanionScriptHost.SetGroups(DefaultGroup(new DisplaySpec { Names = new[] { "Cockpit[1]" }, Kinds = new[] { "MissingItems" } }));
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var cockpit = FakeBlocks.Cockpit("Cockpit", 3);
            run.Gts.Add(cockpit.Block);
            bar.Needs("SteelPlate", 5);

            run.Start().Run();

            Assert.Contains("SteelPlate: Amount=5", cockpit.Surfaces[1].Text);
            Assert.Equal("", cockpit.Surfaces[0].Text);
        }

        [Fact]
        public void UnknownDisplayName_IsReported()
        {
            CompanionScriptHost.SetGroups(DefaultGroup(new DisplaySpec { Names = new[] { "Nowhere LCD" } }));
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);

            run.Start().Run();

            Assert.Contains("LCD Nowhere LCD not found", run.EchoText);
        }

        [Fact]
        public void MissingItemsPage_RespectsMaxLines()
        {
            CompanionScriptHost.SetGroups(DefaultGroup(new DisplaySpec { Names = new[] { StatusPanel }, Kinds = new[] { "MissingItems" }, MaxLines = 4 }));
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd(StatusPanel);
            run.Gts.Add(lcd.Block);
            foreach (var c in new[] { "SteelPlate", "Motor", "Computer", "Display", "Girder" }) bar.Needs(c, 1);

            run.Start().Run();

            Assert.Contains("Missing Items: Count 5", lcd.Text);
            Assert.Equal(2, lcd.Text.Split('\n').Count(l => l.Contains(": Amount=")));
            Assert.Contains(" ..", lcd.Text);
        }

        [Fact]
        public void Pages_RotateAfterSwitchTime()
        {
            CompanionScriptHost.SetGroups(DefaultGroup(new DisplaySpec { Names = new[] { StatusPanel }, Kinds = new[] { "ShortStatus", "MissingItems" }, SwitchTime = 3 }));
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd(StatusPanel);
            run.Gts.Add(lcd.Block);
            bar.Needs("SteelPlate", 1);

            run.Start();
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 6; i++)
            {
                run.Run();
                seen.Add(lcd.Text.Contains("Missing Items: Count") ? "missing" : lcd.Text.Contains("Online") ? "short" : "other");
            }

            Assert.Contains("missing", seen);
            Assert.Contains("short", seen);
        }

        // BUG-261122.13: the first configured page is shown first.
        [Fact]
        public void FirstRun_ShowsTheFirstPage()
        {
            CompanionScriptHost.SetGroups(DefaultGroup(new DisplaySpec { Names = new[] { StatusPanel }, Kinds = new[] { "MissingItems", "ShortStatus" }, SwitchTime = 4 }));
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd(StatusPanel);
            run.Gts.Add(lcd.Block);

            run.Start().Run();

            Assert.Contains("Missing Items: Count", lcd.Text);
        }

        // BUG-261122.13: the script re-inits every ~30 s; rotation must carry on so every page shows.
        [Fact]
        public void LongSwitchTime_EveryPageShows_AcrossReinit()
        {
            var kinds = new[] { "ShortStatus", "WeldTargets", "GrindTargets", "CollectTargets", "MissingItems" };
            CompanionScriptHost.SetGroups(DefaultGroup(new DisplaySpec { Names = new[] { StatusPanel }, Kinds = kinds, SwitchTime = 10 }));
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd(StatusPanel);
            run.Gts.Add(lcd.Block);

            run.Start();
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 60; i++) // 100 s, three re-inits
            {
                run.Run();
                foreach (var marker in new[] { "Online", "Weld Targets:", "Grind Targets:", "Collect Targets:", "Missing Items:" })
                    if (lcd.Text.Contains(marker)) seen.Add(marker);
            }

            Assert.Equal(5, seen.Count);
        }

        // ---------------------------------------------------------------- [BaR:..] tags and Custom Data

        [Fact]
        public void TaggedLcd_IsAttached_AndGetsTheCustomDataTemplate()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd("Hangar LCD [BaR:1]");
            run.Gts.Add(lcd.Block);

            run.Start().Run();

            Assert.Contains("@BaR", lcd.Block.CustomData);
            Assert.Contains("Kinds=", lcd.Block.CustomData);
            Assert.True(lcd.Writes > 0, "tagged LCD was never written");
        }

        [Fact]
        public void TaggedCockpit_WritesEachListedSurface()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var cockpit = FakeBlocks.Cockpit("Cockpit [BaR:1@0,2]", 3);
            run.Gts.Add(cockpit.Block);

            run.Start().Run();

            Assert.True(cockpit.Surfaces[0].Writes > 0);
            Assert.Equal(0, cockpit.Surfaces[1].Writes);
            Assert.True(cockpit.Surfaces[2].Writes > 0);
        }

        [Fact]
        public void TagByGroupName_Matches_CaseInsensitive()
        {
            CompanionScriptHost.SetGroups(new GroupSpec { Name = "Hangar", BarGroup = BarGroup, AssemblerGroup = AssemblerGroup });
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd("Screen [bar:hangar]");
            run.Gts.Add(lcd.Block);

            run.Start().Run();

            Assert.True(lcd.Writes > 0);
        }

        [Theory]
        [InlineData("Screen [BaR:2]")]        // no such group (1-based index)
        [InlineData("Screen [BaR:0]")]
        [InlineData("Screen [BaR:1@x]")]      // bad surface index
        [InlineData("Screen [BaR:]")]
        [InlineData("Screen [BaR:1")]         // unclosed
        public void InvalidTags_AreIgnored(string name)
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd(name);
            run.Gts.Add(lcd.Block);

            run.Start().Run();

            Assert.Equal(0, lcd.Writes);
            Assert.Equal("", lcd.Block.CustomData);
        }

        [Fact]
        public void TagOnSurfaceBeyondTheCockpit_IsReported()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            run.Gts.Add(FakeBlocks.Cockpit("Cockpit [BaR:1@5]", 2).Block);

            run.Start().Run();

            Assert.Contains("could not be resolved to a text surface", run.EchoText);
        }

        [Fact]
        public void CustomData_KindsAndMaxLines_Override()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd("Hangar [BaR:1]");
            lcd.Block.CustomData = "@BaR\nKinds=missing\nMaxLines=3\n# comment\nbogus line\n@/BaR";
            run.Gts.Add(lcd.Block);
            foreach (var c in new[] { "SteelPlate", "Motor", "Computer" }) bar.Needs(c, 2);

            run.Start().Run();

            Assert.Contains("Missing Items: Count 3", lcd.Text);
            Assert.Equal(1, lcd.Text.Split('\n').Count(l => l.Contains(": Amount=")));
        }

        [Fact]
        public void CustomData_ScopedBlock_OverridesOneSurface()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var cockpit = FakeBlocks.Cockpit("Cockpit [BaR:1@0,1]", 2);
            cockpit.Block.CustomData = "@BaR\nKinds=ShortStatus\n@/BaR\n@BaR@1\nKinds=MissingItems\n@/BaR";
            run.Gts.Add(cockpit.Block);
            bar.Needs("SteelPlate", 1);

            run.Start().Run();

            Assert.Contains("Online", cockpit.Surfaces[0].Text);
            Assert.DoesNotContain("Missing Items:", cockpit.Surfaces[0].Text);
            Assert.Contains("Missing Items: Count 1", cockpit.Surfaces[1].Text);
        }

        [Fact]
        public void CustomData_FontSize_IsApplied()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd("Hangar [BaR:1]");
            lcd.Block.CustomData = "@BaR\nFontSize=1.5\n@/BaR";
            run.Gts.Add(lcd.Block);

            run.Start().Run();

            Assert.Equal(1.5f, lcd.FontSize);
        }

        [Fact]
        public void CustomData_FontSizeAuto_FitsTheText()
        {
            CompanionScriptHost.SetGroups(DefaultGroup());
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd("Hangar [BaR:1]");
            lcd.Block.CustomData = "@BaR\nKinds=Status\nFontSize=auto\n@/BaR";
            lcd.Surface.SurfaceSize.Returns(new VRageMath.Vector2(512, 512));
            lcd.Surface.MeasureStringInPixels(Arg.Any<System.Text.StringBuilder>(), Arg.Any<string>(), Arg.Any<float>())
                .Returns(new VRageMath.Vector2(1024, 256)); // twice too wide at size 1
            run.Gts.Add(lcd.Block);

            run.Start().Run();

            Assert.InRange(lcd.FontSize, 0.45f, 0.5f);
        }

        [Fact]
        public void TaggedSurface_WinsOverTheSameSurfaceByName()
        {
            CompanionScriptHost.SetGroups(DefaultGroup(new DisplaySpec { Names = new[] { "Hangar [BaR:1]" }, Kinds = new[] { "Status" } }));
            FakeBar bar; Sandbox.ModAPI.Ingame.IMyAssembler a1, a2;
            var run = World(out bar, out a1, out a2);
            var lcd = FakeBlocks.Lcd("Hangar [BaR:1]");
            lcd.Block.CustomData = "@BaR\nKinds=MissingItems\n@/BaR";
            run.Gts.Add(lcd.Block);
            bar.Needs("SteelPlate", 1);

            run.Start().Run();

            Assert.Contains("Missing Items: Count 1", lcd.Text); // the tag's Custom Data page, not the name entry's Status
            Assert.Equal(2, lcd.Writes); // init + one refresh of the tagged entry only (a duplicate name entry would make 4)
        }
    }
}
