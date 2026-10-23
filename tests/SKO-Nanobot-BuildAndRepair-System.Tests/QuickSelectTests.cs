using NSubstitute;
using SKONanobotBuildAndRepairSystem.Cluster;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-GRIND-003 — the capped sort (FEAT-074) must keep the same set as a full sort would.
    public class QuickSelectTests : ModTestBase
    {
        private sealed class RankComparer : IComparer<ClusterTargetCandidate>
        {
            private readonly Dictionary<IMySlimBlock, int> _rank;
            public RankComparer(Dictionary<IMySlimBlock, int> rank) { _rank = rank; }
            public int Compare(ClusterTargetCandidate a, ClusterTargetCandidate b) { return _rank[a.Block].CompareTo(_rank[b.Block]); }
        }

        private static void QuickSelect(List<ClusterTargetCandidate> list, int left, int right, int k, IComparer<ClusterTargetCandidate> comparer)
        {
            InvokePrivateStatic(typeof(NanobotSystem), "QuickSelect", list, left, right, k, comparer);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(42)]
        [InlineData(1337)]
        public void MatchesSortForKSmallest_RandomInputs(int seed)
        {
            var random = new Random(seed);
            for (var round = 0; round < 40; round++)
            {
                var n = random.Next(1, 300);
                var k = random.Next(1, n + 1);
                var rank = new Dictionary<IMySlimBlock, int>();
                var list = new List<ClusterTargetCandidate>();
                for (var i = 0; i < n; i++)
                {
                    var block = Substitute.For<IMySlimBlock>();
                    rank[block] = random.Next(0, 50); // duplicates on purpose
                    list.Add(new ClusterTargetCandidate(block, 0));
                }
                var comparer = new RankComparer(rank);
                var expected = list.Select(c => rank[c.Block]).OrderBy(r => r).Take(k).ToList();

                QuickSelect(list, 0, n - 1, k, comparer);

                var actual = list.Take(k).Select(c => rank[c.Block]).OrderBy(r => r).ToList();
                Assert.Equal(expected, actual);
                Assert.Equal(n, list.Count);
            }
        }

        [Fact]
        public void RespectsSubrangeBounds()
        {
            var rank = new Dictionary<IMySlimBlock, int>();
            var list = new List<ClusterTargetCandidate>();
            for (var i = 0; i < 60; i++)
            {
                var block = Substitute.For<IMySlimBlock>();
                rank[block] = 60 - i;
                list.Add(new ClusterTargetCandidate(block, 0));
            }
            var prefix = list.Take(10).ToList();

            QuickSelect(list, 10, 59, 5, new RankComparer(rank)); // only [10..59] may move

            Assert.Equal(prefix, list.Take(10).ToList());
            var expected = Enumerable.Range(1, 5).ToList();
            Assert.Equal(expected, list.Skip(10).Take(5).Select(c => rank[c.Block]).OrderBy(r => r).ToList());
        }
    }
}
