namespace SKONanobotBuildAndRepairSystem.Handlers
{
    /// <summary>
    /// FEAT-261123.7: per message type counters for the mod's network traffic (messages, payload bytes, bytes on the
    /// wire incl. fan-out, largest message). Cheap (array adds under a lock); read by the test harness and profiling.
    /// </summary>
    public static class NetStats
    {
        public const ushort BaseId = 40000;
        public const int Size = 128;

        private static readonly object _lock = new object();
        private static readonly long[] _sentMsgs = new long[Size];
        private static readonly long[] _sentBytes = new long[Size];
        private static readonly long[] _wireBytes = new long[Size];
        private static readonly int[] _maxSent = new int[Size];
        private static readonly long[] _recvMsgs = new long[Size];
        private static readonly long[] _recvBytes = new long[Size];

        public sealed class Snapshot
        {
            public readonly long[] SentMsgs = new long[Size];
            public readonly long[] SentBytes = new long[Size];
            public readonly long[] WireBytes = new long[Size];
            public readonly int[] MaxSent = new int[Size];
            public readonly long[] RecvMsgs = new long[Size];
            public readonly long[] RecvBytes = new long[Size];
        }

        public static int Index(ushort msgId)
        {
            var i = msgId - BaseId;
            if (i >= 100) i = i - 100 + 16; // 40100.. block messages -> 16..
            return i >= 0 && i < Size ? i : -1;
        }

        public static ushort IdOf(int index)
        {
            return (ushort)(index >= 16 ? BaseId + 100 + index - 16 : BaseId + index);
        }

        /// <summary>One message sent to <paramref name="recipients"/> peers.</summary>
        public static void Sent(ushort msgId, int bytes, int recipients)
        {
            var i = Index(msgId);
            if (i < 0) return;
            lock (_lock)
            {
                _sentMsgs[i]++;
                _sentBytes[i] += bytes;
                _wireBytes[i] += (long)bytes * (recipients > 0 ? recipients : 0);
                if (bytes > _maxSent[i]) _maxSent[i] = bytes;
            }
        }

        /// <summary>Totals over all message types (cheap, for per-second peak sampling).</summary>
        public static void Totals(out long sentMsgs, out long wireBytes)
        {
            sentMsgs = wireBytes = 0;
            lock (_lock)
            {
                for (int i = 0; i < Size; i++) { sentMsgs += _sentMsgs[i]; wireBytes += _wireBytes[i]; }
            }
        }

        public static void Received(ushort msgId, int bytes)
        {
            var i = Index(msgId);
            if (i < 0) return;
            lock (_lock)
            {
                _recvMsgs[i]++;
                _recvBytes[i] += bytes;
            }
        }

        /// <summary>Copies the counters; <paramref name="resetMax"/> starts a new largest-message window.</summary>
        public static Snapshot Take(bool resetMax)
        {
            var s = new Snapshot();
            lock (_lock)
            {
                for (int i = 0; i < Size; i++)
                {
                    s.SentMsgs[i] = _sentMsgs[i];
                    s.SentBytes[i] = _sentBytes[i];
                    s.WireBytes[i] = _wireBytes[i];
                    s.MaxSent[i] = _maxSent[i];
                    s.RecvMsgs[i] = _recvMsgs[i];
                    s.RecvBytes[i] = _recvBytes[i];
                    if (resetMax) _maxSent[i] = 0;
                }
            }
            return s;
        }

        public static void ResetForTests()
        {
            lock (_lock)
            {
                for (int i = 0; i < Size; i++) { _sentMsgs[i] = _sentBytes[i] = _wireBytes[i] = _recvMsgs[i] = _recvBytes[i] = 0; _maxSent[i] = 0; }
            }
        }

        public static string Name(ushort msgId)
        {
            switch (msgId)
            {
                case 40000: return "modDataRequest";
                case 40001: return "modSettings";
                case 40002: return "command";
                case 40003: return "commandResponse";
                case 40004: return "debugStats";
                case 40005: return "profileSummary";
                case 40100: return "blockDataRequest";
                case 40102: return "blockSettingsFromServer";
                case 40103: return "blockSettingsFromClient";
                case 40104: return "blockState";
                default: return "msg" + msgId;
            }
        }
    }
}
