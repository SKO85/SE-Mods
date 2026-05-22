using Sandbox.ModAPI;
using System;

namespace SKONanobotBuildAndRepairSystem.Managers
{
    /// <summary>
    /// Per-tick claim counter capped at `Max` claims per gameplay frame, with optional
    /// per-tick wall-clock budget. Counter resets when the frame counter advances. Used
    /// to throttle expensive operations that compound badly when many BaRs run them in
    /// the same tick (mechanical-block grinds, full dismounts, projector materializations,
    /// weld / grind work).
    /// Read/written from the main thread only — no synchronization.
    ///
    /// CON-2: extended with an optional max-count resolver delegate (so callers can
    /// scale the cap dynamically — e.g. with BaR count) and an optional time budget
    /// + ReportTime accumulator. Peak usage is tracked for HUD diagnostics. The simple
    /// (int max) constructor preserves the original semantics for the existing
    /// mechanical-grind / dismount / projBuild slots.
    /// </summary>
    public sealed class PerTickBudget
    {
        private readonly int _max;
        private readonly Func<int> _maxResolver;
        private readonly double _maxMs;
        private readonly Func<double> _maxMsResolver;
        private int _thisTick;
        private double _msThisTick;
        private int _lastTick = -1;
        private int _peakUsed;

        public PerTickBudget(int max)
        {
            _max = max;
        }

        public PerTickBudget(Func<int> maxResolver, double maxMsPerTick)
        {
            _maxResolver = maxResolver;
            _maxMs = maxMsPerTick;
        }

        /// <summary>
        /// Dynamic-cap constructor: both the count and the ms-per-tick budget are
        /// resolved via delegates each TryClaim call, so admins can re-tune from
        /// settings without restarting the session.
        /// </summary>
        public PerTickBudget(Func<int> maxResolver, Func<double> maxMsResolver)
        {
            _maxResolver = maxResolver;
            _maxMsResolver = maxMsResolver;
        }

        public int PeakUsed { get { return _peakUsed; } }

        public int EffectiveMax { get { return _maxResolver != null ? _maxResolver() : _max; } }

        public bool TryClaim()
        {
            var tick = MyAPIGateway.Session.GameplayFrameCounter;
            if (tick != _lastTick)
            {
                _lastTick = tick;
                _thisTick = 0;
                _msThisTick = 0.0;
            }
            var max = _maxResolver != null ? _maxResolver() : _max;
            if (_thisTick >= max) return false;
            var maxMs = _maxMsResolver != null ? _maxMsResolver() : _maxMs;
            if (maxMs > 0 && _msThisTick >= maxMs) return false;
            _thisTick++;
            if (_thisTick > _peakUsed) _peakUsed = _thisTick;
            return true;
        }

        /// <summary>
        /// Accumulates time spent in the throttled operation against the per-tick ms
        /// budget. No-op when the budget was constructed without a time cap.
        /// </summary>
        public void ReportTime(double ms)
        {
            _msThisTick += ms;
        }

        /// <summary>
        /// Resets the peak usage counter. Called from the HUD reset path.
        /// </summary>
        public void ResetStats()
        {
            _peakUsed = 0;
        }
    }
}
