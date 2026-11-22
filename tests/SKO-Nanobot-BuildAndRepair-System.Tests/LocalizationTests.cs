using SKONanobotBuildAndRepairSystem.Localization;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SKONanobotBuildAndRepairSystem.Tests
{
    // SCN-LOC-001 — every translation carries exactly the English key set (a missing key falls back to
    // English silently; an extra key means a typo that never displays).
    public class LocalizationTests
    {
        public static IEnumerable<object[]> Translations()
        {
            yield return new object[] { "German", TextsGerman.Dictionary };
            yield return new object[] { "Polish", TextsPolish.Dictionary };
            yield return new object[] { "Russian", TextsRussian.Dictionary };
            yield return new object[] { "Korean", TextsKorean.Dictionary };
        }

        [Theory]
        [MemberData(nameof(Translations))]
        public void Translation_HasExactlyTheEnglishKeys(string language, Dictionary<string, string> dictionary)
        {
            var english = new HashSet<string>(TextsEnglish.Dictionary.Keys);
            var missing = english.Except(dictionary.Keys).ToList();
            var extra = dictionary.Keys.Except(english).ToList();
            Assert.True(missing.Count == 0, language + " missing: " + string.Join(", ", missing));
            Assert.True(extra.Count == 0, language + " unknown keys: " + string.Join(", ", extra));
        }

        [Theory]
        [MemberData(nameof(Translations))]
        public void Translation_HasNoEmptyValues(string language, Dictionary<string, string> dictionary)
        {
            var empty = dictionary.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();
            Assert.True(empty.Count == 0, language + " empty values: " + string.Join(", ", empty));
        }

        [Fact]
        public void Merge_FallsBackToEnglishPerKey()
        {
            var english = new Dictionary<string, string> { { "A", "a-en" }, { "B", "b-en" } };
            var partial = new Dictionary<string, string> { { "A", "a-ko" } };

            var merged = LocalizationHelper.Merge(english, partial);

            Assert.Equal("a-ko", merged["A"]);
            Assert.Equal("b-en", merged["B"]);
        }

        [Fact]
        public void Merge_NullRequested_IsEnglish()
        {
            var english = new Dictionary<string, string> { { "A", "a-en" } };
            Assert.Equal("a-en", LocalizationHelper.Merge(english, null)["A"]);
        }
    }
}
