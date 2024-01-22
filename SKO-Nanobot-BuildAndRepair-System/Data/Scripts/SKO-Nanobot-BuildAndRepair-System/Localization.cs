using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Utils;

namespace SKONanobotBuildAndRepairSystem
{
    public static class LocalizationHelper
    {
        public static Dictionary<string, string> GetTexts(MyLanguagesEnum language, Dictionary<MyLanguagesEnum, Dictionary<string, string>> translations, Logging log = null)
        {
            var texts = new Dictionary<string, string>();

            var fallbackTranslation = translations[MyLanguagesEnum.English]; //Should be a complete set (all possible entries)
            Dictionary<string, string> requestedTranslation;
            if (language == MyLanguagesEnum.English || !translations.TryGetValue(language, out requestedTranslation)) requestedTranslation = null;

            foreach (var kv in fallbackTranslation)
            {
                string translation;
                if (requestedTranslation == null || !requestedTranslation.TryGetValue(kv.Key, out translation))
                {
                    if (language != MyLanguagesEnum.English && log != null && log.ShouldLog(Logging.Level.Error)) log.Write(Logging.Level.Error, "Missing translation in language={0} for key={1}", language, kv.Key);
                    translation = kv.Value;
                }
                texts.Add(kv.Key, translation);
            }
            return texts;
        }

        public static MyStringId GetStringId(Dictionary<string, string> texts, string key)
        {
            try
            {
                return MyStringId.GetOrCompute(texts[key]);
            }
            catch (Exception ex)
            {
                throw new Exception($"GetStringId Failed for Key={key}", ex);
            }
        }

        public static void ExportDictionary(string destFileName, Dictionary<string, string> texts)
        {
            using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(destFileName, typeof(LocalizationHelper)))
            {
                foreach (var kv in texts)
                {
                    writer.WriteLine($"\"{kv.Key}\",\"{kv.Value}\"");
                }
            }
        }
    }
}
