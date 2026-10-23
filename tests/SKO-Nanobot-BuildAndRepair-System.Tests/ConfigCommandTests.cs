using NSubstitute;
using SKONanobotBuildAndRepairSystem.Chat.Commands;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-CHAT-001 — /nanobars config set/get round trip and bounds rejection.
    public class ConfigCommandTests : ModTestBase
    {
        public ConfigCommandTests()
        {
            Session.IsServer.Returns(false); // keeps BroadcastModSettings a no-op
        }

        private static Chat.ChatCommandResult Run(params string[] args)
        {
            return ConfigCommand.Execute(args);
        }

        [Fact]
        public void SetGetRoundTrip_AndBoundsRejection()
        {
            Assert.False(Run("config", "set", "MaxGrindsPerTick", "20").IsError);
            Assert.Equal(20, Mod.Settings.MaxGrindsPerTick);
            Assert.Contains("20", Run("config", "get", "MaxGrindsPerTick").Message);

            Assert.True(Run("config", "set", "MaxGrindsPerTick", "101").IsError);
            Assert.Equal(20, Mod.Settings.MaxGrindsPerTick);
        }

        [Fact]
        public void FloatsParseInvariant()
        {
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.False(Run("config", "set", "WeldingMultiplier", "2.5").IsError);
                Assert.Equal(2.5f, Mod.Settings.Welder.WeldingMultiplier); // BUG-260610.20: not 25
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void UnknownSettingAndBadBool_AreErrors()
        {
            Assert.True(Run("config", "set", "NoSuchSetting", "1").IsError);
            Assert.True(Run("config", "set", "DebugMode", "yes").IsError);
        }

        [Fact]
        public void SettingNamesAreCaseInsensitive()
        {
            Assert.False(Run("config", "set", "maxgrindspertick", "12").IsError);
            Assert.Equal(12, Mod.Settings.MaxGrindsPerTick);
        }

        [Theory]
        [InlineData("ko", "ko")]
        [InlineData("none", "")]
        [InlineData("game", "")]
        public void LanguageOverride_AcceptsKnownCodes(string value, string expected)
        {
            Assert.False(Run("config", "set", "LanguageOverride", value).IsError);
            Assert.Equal(expected, Mod.Settings.LanguageOverride);
        }

        [Fact]
        public void LanguageOverride_RejectsUnknownCode()
        {
            Mod.Settings.LanguageOverride = "ko";
            Assert.True(Run("config", "set", "LanguageOverride", "xx").IsError);
            Assert.Equal("ko", Mod.Settings.LanguageOverride);
        }
    }
}
