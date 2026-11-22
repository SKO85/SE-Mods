using NSubstitute;
using Sandbox.Definitions;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.Collections.Generic;
using System.Reflection;
using VRage;
using VRage.Game.ModAPI;
using Xunit;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-WELD-005 / SCN-BUDGET-006 — BUG-261023.1 time-sliced missing-components aggregate.
    public class MissingComponentsAggregateTests : ModTestBase
    {
        private const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MyCubeBlockDefinition Def = new MyCubeBlockDefinition { Components = new MyCubeBlockDefinition.Component[1] };
        private static int _frameSeed = 10000000;

        public MissingComponentsAggregateTests()
        {
            // Fresh frames so the static per-tick slice budget never carries over.
            _frameSeed += 100000;
            Frame = _frameSeed;
        }

        private static IMySlimBlock Block(string subtype, int missing)
        {
            var block = Substitute.For<IMySlimBlock>();
            block.BlockDefinition.Returns(Def);
            block.When(b => b.GetMissingComponents(Arg.Any<Dictionary<string, int>>())).Do(ci =>
            {
                var d = ci.Arg<Dictionary<string, int>>();
                int v;
                d.TryGetValue(subtype, out v);
                d[subtype] = v + missing;
            });
            return block;
        }

        private static IMyInventory Stock(string subtype, int amount, bool reachable = true)
        {
            var inv = Substitute.For<IMyInventory>();
            inv.ItemCount.Returns(1);
            inv.CanTransferItemTo(Arg.Any<VRage.Game.ModAPI.Ingame.IMyInventory>(), Arg.Any<MyItemType>()).Returns(reachable);
            inv.When(i => i.GetItems(Arg.Any<List<MyInventoryItem>>(), Arg.Any<Func<MyInventoryItem, bool>>())).Do(ci =>
                ci.Arg<List<MyInventoryItem>>().Add(new MyInventoryItem(MyItemType.MakeComponent(subtype), 1u, (MyFixedPoint)amount)));
            return inv;
        }

        private static NanobotSystem NewSystem(long entityId, int targets, int missingEach)
        {
            var system = new NanobotSystem();
            // Settings first: CheckLimits reads the welder subtype when a welder is set.
            typeof(NanobotSystem).GetField("_Settings", Inst).SetValue(system, new SyncBlockSettings(system));
            var welder = Substitute.For<IMyShipWelder>();
            welder.EntityId.Returns(entityId);
            typeof(NanobotSystem).GetField("_Welder", Inst).SetValue(system, welder);
            system._TransportInventory = Substitute.For<IMyInventory>();
            for (var i = 0; i < targets; i++)
                system.State.PossibleWeldTargets.Add(new TargetBlockData(Block("SteelPlate", missingEach), i, 0));
            return system;
        }

        private void Run(NanobotSystem system)
        {
            Frame++;
            typeof(NanobotSystem).GetMethod("ServerTryRebuildMissingComponentsAggregate", Inst).Invoke(system, new object[] { Now });
        }

        private static int Missing(NanobotSystem system, string subtype)
        {
            foreach (var kv in system.State.MissingComponents)
                if (kv.Key.SubtypeName == subtype) return kv.Value;
            return 0;
        }

        private static T Field<T>(NanobotSystem system, string name)
        {
            return (T)typeof(NanobotSystem).GetField(name, Inst).GetValue(system);
        }

        [Fact]
        public void WalksAtMost128TargetsPerCall_AndPublishesTheTotalOnce()
        {
            // EntityId 3000 → phase offset 0, so the first call starts the pass.
            var system = NewSystem(3000, 300, 2);

            Run(system);
            Assert.Equal(128, Field<int>(system, "_AggCursor"));
            Assert.Empty(system.State.MissingComponents);

            Run(system);
            Assert.Equal(256, Field<int>(system, "_AggCursor"));
            Assert.Empty(system.State.MissingComponents);

            Run(system);
            Assert.Equal(0, Field<int>(system, "_AggPhase"));
            Assert.Equal(600, Missing(system, "SteelPlate"));
        }

        [Fact]
        public void PreviousListStaysUntilThePassCompletes()
        {
            var system = NewSystem(3000, 200, 1);
            Run(system);
            Run(system);
            Assert.Equal(200, Missing(system, "SteelPlate"));

            // Next pass (after the interval) with more need: old value visible until it completes.
            system.State.PossibleWeldTargets.Add(new TargetBlockData(Block("SteelPlate", 50), 0, 0));
            Now += TimeSpan.FromSeconds(3);
            Run(system);
            Assert.Equal(1, Field<int>(system, "_AggPhase"));
            Assert.Equal(200, Missing(system, "SteelPlate"));
            Run(system);
            Assert.Equal(250, Missing(system, "SteelPlate"));
        }

        [Fact]
        public void SliceIsRefused_WhenTheGlobalPerTickBudgetIsSpent()
        {
            var system = NewSystem(3000, 300, 1);
            Frame++;
            for (var i = 0; i < Mod.MaxAggregateSlicesPerTickDefault; i++) Assert.True(Mod.TryClaimAggregateSlice());

            // Same frame: the pass starts (snapshot) but walks nothing.
            typeof(NanobotSystem).GetMethod("ServerTryRebuildMissingComponentsAggregate", Inst).Invoke(system, new object[] { Now });
            Assert.Equal(1, Field<int>(system, "_AggPhase"));
            Assert.Equal(0, Field<int>(system, "_AggCursor"));

            Run(system);
            Assert.Equal(128, Field<int>(system, "_AggCursor"));
        }

        [Fact]
        public void FirstPassIsPhaseOffsetByEntityId_ThenWaitsTheInterval()
        {
            var system = NewSystem(1500, 10, 1); // offset 1.5 s
            var start = Now;

            Run(system);
            Assert.Equal(0, Field<int>(system, "_AggPhase"));
            Now = start + TimeSpan.FromMilliseconds(1499);
            Run(system);
            Assert.Empty(system.State.MissingComponents);

            Now = start + TimeSpan.FromMilliseconds(1500);
            Run(system);
            Assert.Equal(10, Missing(system, "SteelPlate"));

            // Next pass only 3 s after the publish.
            system.State.PossibleWeldTargets.Add(new TargetBlockData(Block("SteelPlate", 5), 0, 0));
            Now += TimeSpan.FromMilliseconds(2999);
            Run(system);
            Assert.Equal(10, Missing(system, "SteelPlate"));
            Now += TimeSpan.FromMilliseconds(1);
            Run(system);
            Assert.Equal(15, Missing(system, "SteelPlate"));
        }

        [Fact]
        public void StockOnHandIsSubtracted()
        {
            var system = NewSystem(3000, 10, 20); // need 200
            system._TransportInventory = Stock("SteelPlate", 150);
            Run(system);
            Assert.Equal(50, Missing(system, "SteelPlate"));
        }

        [Fact]
        public void CoveredNeed_PublishesAnEmptyList()
        {
            var system = NewSystem(3000, 10, 2);
            system._TransportInventory = Stock("SteelPlate", 100);
            Run(system);
            Assert.Equal(0, Field<int>(system, "_AggPhase"));
            Assert.Empty(system.State.MissingComponents);
        }

        [Fact]
        public void SourcesAreWalkedInSlices_AfterTheTargets()
        {
            var system = NewSystem(3000, 150, 2); // need 300
            var sources = Field<List<IMyInventory>>(system, "_PossibleSources");
            for (var i = 0; i < 40; i++) sources.Add(Stock("SteelPlate", 1));

            Run(system); // targets 0..127
            Run(system); // targets 128..149 → phase 2
            Assert.Equal(2, Field<int>(system, "_AggPhase"));
            Run(system); // sources 0..31
            Assert.Equal(2, Field<int>(system, "_AggPhase"));
            Assert.Equal(32, Field<int>(system, "_AggCursor"));
            Assert.Empty(system.State.MissingComponents);
            Run(system); // sources 32..39 → publish
            Assert.Equal(0, Field<int>(system, "_AggPhase"));
            Assert.Equal(260, Missing(system, "SteelPlate"));
        }

        // PR #152 review: an ignore-color projection is only placed, so it needs its creation component only.
        [Fact]
        public void IgnoreColorProjection_CountsTheCreationComponentOnly()
        {
            var system = NewSystem(3000, 0, 0);
            var settings = Field<SyncBlockSettings>(system, "_Settings");
            var ignore = new VRageMath.Vector3(0.5f, 0.2f, 0.1f);
            settings.IgnoreColor = ignore;
            settings.Flags |= SyncBlockSettings.Settings.UseIgnoreColor;

            system.State.PossibleWeldTargets.Add(new TargetBlockData(Projected(ignore), 0, TargetBlockData.AttributeFlags.Projected));
            system.State.PossibleWeldTargets.Add(new TargetBlockData(Projected(new VRageMath.Vector3(0.9f, 0.9f, 0.9f)), 1, TargetBlockData.AttributeFlags.Projected));

            Run(system);
            Assert.Equal(1 + 25, Missing(system, "SteelPlate")); // ignore-colored: creation 1; other: full 25
        }

        // Armor-like definition: creation component SteelPlate; 25 missing for the full block (engine path).
        private static readonly MyCubeBlockDefinition ArmorDef = new MyCubeBlockDefinition
        {
            Components = new[]
            {
                new MyCubeBlockDefinition.Component
                {
                    Definition = new MyComponentDefinition { Id = new VRage.Game.MyDefinitionId(typeof(VRage.Game.MyObjectBuilder_Component), "SteelPlate") },
                    Count = 25
                }
            }
        };

        private static IMySlimBlock Projected(VRageMath.Vector3 color)
        {
            var block = Substitute.For<IMySlimBlock>();
            block.BlockDefinition.Returns(ArmorDef);
            block.GetColorMask().Returns(color);
            block.When(b => b.GetMissingComponents(Arg.Any<Dictionary<string, int>>())).Do(ci =>
            {
                var d = ci.Arg<Dictionary<string, int>>();
                int v;
                d.TryGetValue("SteelPlate", out v);
                d["SteelPlate"] = v + 25;
            });
            return block;
        }

        // PR #152 review: a sorter can block a component between a connected source and the BaR; that stock
        // must stay "missing", or the companion script never queues it and the build stalls.
        [Fact]
        public void SourceStockTheBarCannotPull_IsNotSubtracted()
        {
            var system = NewSystem(3000, 10, 20); // need 200
            var sources = Field<List<IMyInventory>>(system, "_PossibleSources");
            sources.Add(Stock("SteelPlate", 500, reachable: false)); // behind a sorter
            sources.Add(Stock("SteelPlate", 50));

            Run(system); // targets → phase 2
            Run(system); // sources → publish
            Assert.Equal(0, Field<int>(system, "_AggPhase"));
            Assert.Equal(150, Missing(system, "SteelPlate"));
        }

        [Fact]
        public void NoTargets_ClearsAStaleListImmediately()
        {
            var system = NewSystem(3000, 0, 0);
            system.State.MissingComponents.Add(new VRage.Game.MyDefinitionId(typeof(VRage.Game.MyObjectBuilder_Component), "SteelPlate"), 7);
            Run(system);
            Assert.Empty(system.State.MissingComponents);
            Assert.Equal(0, Field<int>(system, "_AggPhase"));
        }

        [Fact]
        public void IgnoredTargets_AreSkippedMidPass()
        {
            var system = NewSystem(3000, 200, 1);
            Run(system);
            // Targets completed (Ignore) between slices are not counted.
            for (var i = 128; i < 200; i++) system.State.PossibleWeldTargets[i].Ignore = true;
            Run(system);
            Assert.Equal(128, Missing(system, "SteelPlate"));
        }

        [Fact]
        public void Abort_DropsThePassAndReseedsThePhaseOffset()
        {
            var system = NewSystem(4500, 300, 1); // offset 1.5 s
            var start = Now;
            Run(system);
            Now = start + TimeSpan.FromMilliseconds(1500);
            Run(system);
            Assert.Equal(1, Field<int>(system, "_AggPhase"));

            typeof(NanobotSystem).GetMethod("AbortMissingComponentsAggregate", Inst).Invoke(system, null);
            Assert.Equal(0, Field<int>(system, "_AggPhase"));
            Assert.Empty(Field<List<TargetBlockData>>(system, "_AggTargetsSnapshot"));

            // Re-enabled later: waits its offset again instead of starting at once.
            Now += TimeSpan.FromSeconds(60);
            Run(system);
            Assert.Equal(0, Field<int>(system, "_AggPhase"));
            Now += TimeSpan.FromMilliseconds(1500);
            Run(system);
            Assert.Equal(1, Field<int>(system, "_AggPhase"));
        }
    }
}
