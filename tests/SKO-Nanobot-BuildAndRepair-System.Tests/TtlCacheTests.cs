using SKONanobotBuildAndRepairSystem.Caches;
using System;
using System.Threading;
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

        [Fact]
        public void CleanupExpired_KeepsEntriesRefreshedWhileItRuns()
        {
            // BUG-261023.11: cleanup runs on the background pool while the main thread re-claims.
            // Only fails when a refresh lands between the cleanup's two passes, so run several rounds.
            const int keys = 20000;
            var cache = new TtlCache<int, long>(TimeSpan.FromSeconds(1));
            var missing = 0;
            for (var round = 0; round < 10 && missing == 0; round++)
            {
                Now = TimeSpan.FromSeconds(100 * (round + 1));
                for (var i = 0; i < keys; i++) cache.Set(i, 1L);
                Now += TimeSpan.FromSeconds(2); // all expired

                var stop = 0;
                var running = new ManualResetEventSlim(false);
                var cleaner = new Thread(() =>
                {
                    running.Set();
                    while (Volatile.Read(ref stop) == 0) cache.CleanupExpired();
                });
                cleaner.Start();
                running.Wait();
                for (var i = 0; i < keys; i++) cache.Set(i, 2L, TimeSpan.FromSeconds(30));
                Volatile.Write(ref stop, 1);
                cleaner.Join();

                long v;
                for (var i = 0; i < keys; i++) if (!cache.TryGet(i, out v)) missing++;
            }
            Assert.Equal(0, missing);
        }
    }
}
