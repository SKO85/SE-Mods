using Sandbox.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Managers
{
    /// <summary>
    /// Per-tick claim counter capped at `Max` claims per gameplay frame.
    /// Counter resets when the frame counter advances. Used to throttle expensive
    /// operations that compound badly when many BaRs run them in the same tick
    /// (mechanical-block grinds, full dismounts, projector materializations).
    /// Read/written from the main thread only — no synchronization.
    /// </summary>
    public sealed class PerTickBudget
    {
        private readonly int _max;
        private int _thisTick;
        private int _lastTick = -1;

        public PerTickBudget(int max)
        {
            _max = max;
        }

        public bool TryClaim()
        {
            var tick = MyAPIGateway.Session.GameplayFrameCounter;
            if (tick != _lastTick)
            {
                _lastTick = tick;
                _thisTick = 0;
            }
            if (_thisTick >= _max) return false;
            _thisTick++;
            return true;
        }
    }
}
