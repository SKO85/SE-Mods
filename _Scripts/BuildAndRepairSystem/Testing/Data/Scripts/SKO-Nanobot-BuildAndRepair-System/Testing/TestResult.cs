using System.Collections.Generic;
using System.Globalization;

namespace SKONanobotBuildAndRepairSystem.Testing
{
    /// <summary>Outcome of one scenario run: pass/fail, failure reasons, key=value metrics.</summary>
    internal sealed class TestResult
    {
        public string Id;
        public int Run;
        public bool Passed = true;
        public double DurationSeconds;
        public readonly List<string> Failures = new List<string>();
        public readonly List<KeyValuePair<string, string>> Metrics = new List<KeyValuePair<string, string>>();

        public TestResult(string id, int run)
        {
            Id = id;
            Run = run;
        }

        public void Fail(string reason)
        {
            Passed = false;
            Failures.Add(reason);
        }

        public void Metric(string key, double value)
        {
            Metrics.Add(new KeyValuePair<string, string>(key, value.ToString("F3", CultureInfo.InvariantCulture)));
        }

        public void Metric(string key, long value)
        {
            Metrics.Add(new KeyValuePair<string, string>(key, value.ToString(CultureInfo.InvariantCulture)));
        }

        public void Metric(string key, bool value)
        {
            Metrics.Add(new KeyValuePair<string, string>(key, value ? "true" : "false"));
        }

        /// <summary>Numeric value of a metric, or NaN when absent / non-numeric.</summary>
        public double GetNumeric(string key)
        {
            for (int i = 0; i < Metrics.Count; i++)
            {
                if (Metrics[i].Key != key) continue;
                double v;
                if (double.TryParse(Metrics[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
                return double.NaN;
            }
            return double.NaN;
        }
    }
}
