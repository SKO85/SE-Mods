using SKONanobotBuildAndRepairSystem.Caches;
using System;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-BUDGET-001 — the TTL primitive behind block assignment and failure cooldowns.
    public class TtlCacheTests : ModTestBase
    {
        [Fact]
        public void SetThenTryGet_ReturnsBeforeExpiry_AndMissesAfter()
        {
            var cache = new TtlCache<int, string>(TimeSpan.FromSeconds(5));
            Now = TimeSpan.FromSeconds(10);
            Assert.True(cache.Set(1, "a"));

            string value;
            Now = TimeSpan.FromSeconds(14.9);
            Assert.True(cache.TryGet(1, out value));
            Assert.Equal("a", value);

            Now = TimeSpan.FromSeconds(15); // IsExpired uses >=
            Assert.False(cache.TryGet(1, out value));
        }

        [Fact]
        public void CleanupExpired_RemovesOnlyExpired_TwoPass()
        {
            var cache = new TtlCache<int, int>(TimeSpan.FromSeconds(5));
            Now = TimeSpan.FromSeconds(10);
            cache.Set(1, 1, TimeSpan.FromSeconds(1));
            cache.Set(2, 2, TimeSpan.FromSeconds(1));
            cache.Set(3, 3, TimeSpan.FromSeconds(60));

            Now = TimeSpan.FromSeconds(20);
            cache.CleanupExpired();

            Assert.Equal(1, cache.Count);
            int v;
            Assert.True(cache.TryGet(3, out v));
        }

        [Fact]
        public void NoSession_SetReturnsFalse()
        {
            var cache = new TtlCache<int, int>(TimeSpan.FromSeconds(5));
            Mod.NowPlayTime = TimeSpan.Zero;
            Sandbox.ModAPI.MyAPIGateway.Session = null; // BUG-045: no session yet

            Assert.False(cache.Set(1, 1));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Set_RefreshesTtl_OnReclaim()
        {
            var cache = new TtlCache<int, int>(TimeSpan.FromSeconds(5));
            Now = TimeSpan.FromSeconds(10);
            cache.Set(1, 7);
            Now = TimeSpan.FromSeconds(14);
            cache.Set(1, 7); // lock-on refresh every tick keeps the claim alive

            int v;
            Now = TimeSpan.FromSeconds(18);
            Assert.True(cache.TryGet(1, out v));
        }
    }
}
