using NSubstitute;
using Sandbox.Definitions;
using System;
using System.Reflection;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-COLLECT-001 / SCN-GRIND-001 — BUG-261023.5 verified moves into the transport inventory.
    public class TransportInventoryMoveTests : ModTestBase
    {
        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        private const uint ItemId = 7;

        /// <summary>Destination whose engine add accepts at most <paramref name="accepts"/> per call.</summary>
        private sealed class Dst
        {
            public readonly IMyInventory Inv = Substitute.For<IMyInventory>();
            public MyFixedPoint Amount;

            public Dst(double accepts, float maxVolume = 1000f, float currentVolume = 0f)
            {
                Inv.MaxVolume.Returns((MyFixedPoint)maxVolume);
                Inv.CurrentVolume.Returns((MyFixedPoint)currentVolume);
                Inv.GetItemAmount(Arg.Any<VRage.Game.ModAPI.Ingame.MyItemType>()).Returns(_ => Amount);
                Inv.When(i => i.AddItems(Arg.Any<MyFixedPoint>(), Arg.Any<MyObjectBuilder_PhysicalObject>(), Arg.Any<int>())).Do(ci =>
                {
                    var a = ci.Arg<MyFixedPoint>();
                    var cap = (MyFixedPoint)accepts;
                    Amount += a > cap ? cap : a;
                });
            }
        }

        private static IMyInventory Src(double amount)
        {
            var item = Substitute.For<IMyInventoryItem>();
            item.Amount.Returns((MyFixedPoint)amount);
            item.ItemId.Returns(ItemId);
            item.Content.Returns(new MyObjectBuilder_Component { SubtypeName = "SteelPlate" });
            var src = Substitute.For<IMyInventory>();
            src.GetItemByID(ItemId).Returns(item);
            return src;
        }

        private static MyPhysicalItemDefinition Def(float volume, bool integral)
        {
            var def = new MyPhysicalItemDefinition { Volume = volume };
            def.Id = integral
                ? new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate")
                : new MyDefinitionId(typeof(MyObjectBuilder_Ore), "Iron");
            return def;
        }

        private static MyFixedPoint Add(IMyInventory dst, double amount)
        {
            return (MyFixedPoint)typeof(NanobotSystem).GetMethod("AddItemsVerified", Static)
                .Invoke(null, new object[] { dst, (MyFixedPoint)amount, new MyObjectBuilder_Component { SubtypeName = "SteelPlate" } });
        }

        private static MyFixedPoint Move(IMyInventory src, MyPhysicalItemDefinition def, IMyInventory dst, double amount)
        {
            return (MyFixedPoint)typeof(NanobotSystem).GetMethod("MoveItemVerified", Static)
                .Invoke(null, new object[] { src, ItemId, def, dst, (MyFixedPoint)amount });
        }

        [Fact]
        public void AddItemsVerified_ReturnsWhatLanded()
        {
            Assert.Equal((MyFixedPoint)10, Add(new Dst(100).Inv, 10));
            Assert.Equal((MyFixedPoint)4, Add(new Dst(4).Inv, 10));
            Assert.Equal(MyFixedPoint.Zero, Add(new Dst(0).Inv, 10));
        }

        [Fact]
        public void MoveItemVerified_RefusedAdd_LeavesTheSourceUntouched()
        {
            var src = Src(10);
            var moved = Move(src, Def(0.008f, true), new Dst(0).Inv, 10);
            Assert.Equal(MyFixedPoint.Zero, moved);
            src.DidNotReceiveWithAnyArgs().RemoveItems(default(uint), null, false, false);
        }

        [Fact]
        public void MoveItemVerified_RemovesOnlyWhatLanded()
        {
            var src = Src(10);
            var dst = new Dst(4);
            var moved = Move(src, Def(0.008f, true), dst.Inv, 10);
            Assert.Equal((MyFixedPoint)4, moved);
            Assert.Equal((MyFixedPoint)4, dst.Amount);
            src.Received(1).RemoveItems(ItemId, (MyFixedPoint)4, Arg.Any<bool>(), Arg.Any<bool>());
        }

        [Fact]
        public void MoveItemVerified_ClampsToRealHeadroomAndSourceAmount()
        {
            // 0.5 m³ free, 0.1 m³ per item → 5 fit although 10 were asked.
            var dst = new Dst(100, maxVolume: 1f, currentVolume: 0.5f);
            Assert.Equal((MyFixedPoint)5, Move(Src(20), Def(0.1f, true), dst.Inv, 10));

            // Source holds less than asked.
            Assert.Equal((MyFixedPoint)3, Move(Src(3), Def(0.008f, true), new Dst(100).Inv, 10));
        }

        [Fact]
        public void MoveItemVerified_FloorsIntegralItems_KeepsFractionsForOre()
        {
            var dst = new Dst(100, maxVolume: 1f, currentVolume: 0.63f); // 3.7 items of 0.1 fit
            Assert.Equal((MyFixedPoint)3, Move(Src(20), Def(0.1f, true), dst.Inv, 10));

            var ore = new Dst(100, maxVolume: 1f, currentVolume: 0.63f);
            var moved = (double)Move(Src(20), Def(0.1f, false), ore.Inv, 10);
            Assert.InRange(moved, 3.69, 3.71);
        }
    }
}
