using NSubstitute;
using System;
using System.Collections.Generic;
using System.Reflection;
using VRage.Game;
using VRage.Game.ModAPI;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-GRIND-006 — BUG-261122.15: BaR grinding raises BeforeDamage like vanilla grinders, so a protection plugin /
    // mod can veto it; a refused grid is skipped by that BaR for 30 s (no retry every tick).
    public class GrindDamageVetoTests : ModTestBase
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly IMyDamageSystem _damage = Substitute.For<IMyDamageSystem>();
        private float _setAmountTo = -1f; // < 0: leave the damage as is
        private int _raised;

        public GrindDamageVetoTests()
        {
            Session.DamageSystem.Returns(_damage);
            _damage.When(d => d.RaiseBeforeDamageApplied(Arg.Any<object>(), ref Arg.Any<MyDamageInformation>())).Do(ci =>
            {
                _raised++;
                if (_setAmountTo < 0f) return;
                var info = (MyDamageInformation)ci[1];
                info.Amount = _setAmountTo;
                ci[1] = info;
            });
        }

        private static IMySlimBlock Block(long gridId)
        {
            var grid = Substitute.For<IMyCubeGrid>();
            grid.EntityId.Returns(gridId);
            var block = Substitute.For<IMySlimBlock>();
            block.CubeGrid.Returns(grid);
            return block;
        }

        private static bool Raise(NanobotSystem system, IMySlimBlock block, ref MyDamageInformation info)
        {
            var args = new object[] { block, info };
            var ok = (bool)typeof(NanobotSystem).GetMethod("RaiseGrindDamage", Any).Invoke(system, args);
            info = (MyDamageInformation)args[1];
            return ok;
        }

        private static bool Refused(NanobotSystem system, long gridId)
        {
            return (bool)typeof(NanobotSystem).GetMethod("IsGrindRefused", Any).Invoke(system, new object[] { gridId });
        }

        [Fact]
        public void UntouchedDamage_Grinds_AndRefusesNothing()
        {
            var system = new NanobotSystem();
            var info = new MyDamageInformation(false, 5f, MyDamageType.Grind, 1);

            Assert.True(Raise(system, Block(7), ref info));
            Assert.Equal(5f, info.Amount);
            Assert.Equal(1, _raised);
            Assert.False(Refused(system, 7));
        }

        [Fact]
        public void ReducedDamage_GrindsWithTheReducedAmount()
        {
            var system = new NanobotSystem();
            _setAmountTo = 2f;
            var info = new MyDamageInformation(false, 5f, MyDamageType.Grind, 1);

            Assert.True(Raise(system, Block(7), ref info));
            Assert.Equal(2f, info.Amount);
        }

        [Fact]
        public void ZeroedDamage_RefusesTheGridFor30s_ThenRetries()
        {
            var system = new NanobotSystem();
            _setAmountTo = 0f;
            var info = new MyDamageInformation(false, 5f, MyDamageType.Grind, 1);

            Assert.False(Raise(system, Block(7), ref info));
            Assert.True(Refused(system, 7));
            Assert.False(Refused(system, 8));        // other grids unaffected

            Now = Now + TimeSpan.FromSeconds(29);
            Assert.True(Refused(system, 7));
            Now = Now + TimeSpan.FromSeconds(2);
            Assert.False(Refused(system, 7));        // retried after the skip
            var refused = (Dictionary<long, TimeSpan>)typeof(NanobotSystem).GetField("_grindRefusedGrids", Any).GetValue(system);
            Assert.Empty(refused);                   // expired entry dropped on lookup
        }
    }
}
