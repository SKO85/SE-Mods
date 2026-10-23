using ProtoBuf;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // Network sync (SCN-NET-001): every synced type must have unique ProtoMember tags, or
    // client/server silently deserialize the wrong field. Cheap guard for every new setting.
    public class ProtoContractTests
    {
        public static IEnumerable<object[]> ContractTypes()
        {
            return typeof(Mod).Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<ProtoContractAttribute>() != null)
                .Select(t => new object[] { t.FullName });
        }

        [Theory]
        [MemberData(nameof(ContractTypes))]
        public void ProtoMemberTags_AreUniquePerType(string typeName)
        {
            var type = typeof(Mod).Assembly.GetType(typeName, true);
            var tags = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => new { m.Name, Attr = m.GetCustomAttribute<ProtoMemberAttribute>() })
                .Where(x => x.Attr != null)
                .GroupBy(x => x.Attr.Tag)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key + ": " + string.Join(", ", g.Select(x => x.Name)))
                .ToList();

            Assert.True(tags.Count == 0, typeName + " has duplicate ProtoMember tags: " + string.Join("; ", tags));
        }

        [Fact]
        public void ThereAreContractTypesToCheck()
        {
            Assert.True(ContractTypes().Count() >= 10);
        }
    }
}
