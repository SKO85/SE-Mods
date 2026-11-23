using Sandbox.Game;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    public static class UtilsFaction
    {
        public const int DamageAmount = 5;
        private const int MinFactionReputation = -1500;

        // FEAT-261123.5: grind hits are collected per (BaR owner, target owner) and applied once every FlushInterval as
        // one total (same reputation loss, far fewer engine calls; no player list per grind). Main thread only.
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
        private static readonly Dictionary<long, Dictionary<long, int>> _pending = new Dictionary<long, Dictionary<long, int>>();
        private static readonly List<long> _sourceKeys = new List<long>();
        private static TimeSpan _nextFlush = TimeSpan.Zero;

        /// <summary>Queue one grind hit; applied by Flush.</summary>
        public static void QueueReputationDamage(long sourcePlayerId, long targetPlayerId)
        {
            Dictionary<long, int> targets;
            if (!_pending.TryGetValue(sourcePlayerId, out targets))
            {
                targets = new Dictionary<long, int>();
                _pending[sourcePlayerId] = targets;
            }
            int hits;
            targets.TryGetValue(targetPlayerId, out hits);
            targets[targetPlayerId] = hits + 1;
        }

        /// <summary>Applies the queued hits (at most every FlushInterval unless forced).</summary>
        public static void Flush(TimeSpan now, bool force = false)
        {
            if (_pending.Count == 0 || (!force && now < _nextFlush)) return;
            _nextFlush = now + FlushInterval;
            _sourceKeys.Clear();
            foreach (var key in _pending.Keys) _sourceKeys.Add(key);
            for (int i = 0; i < _sourceKeys.Count; i++)
            {
                var source = _sourceKeys[i];
                var targets = _pending[source];
                foreach (var kv in targets)
                {
                    if (kv.Value > 0) DamageReputationWithPlayerFaction(source, kv.Key, kv.Value * DamageAmount);
                }
                targets.Clear();
            }
        }

        public static int PendingHits(long sourcePlayerId, long targetPlayerId)
        {
            Dictionary<long, int> targets;
            int hits;
            return _pending.TryGetValue(sourcePlayerId, out targets) && targets.TryGetValue(targetPlayerId, out hits) ? hits : 0;
        }

        /// <summary>World unload: drop what is still queued.</summary>
        public static void Reset()
        {
            _pending.Clear();
            _nextFlush = TimeSpan.Zero;
        }

        public static void DamageReputationWithPlayerFaction(long sourcePlayerId, long targetPlayerId)
        {
            DamageReputationWithPlayerFaction(sourcePlayerId, targetPlayerId, DamageAmount);
        }

        public static void DamageReputationWithPlayerFaction(long sourcePlayerId, long targetPlayerId, int amount)
        {
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Factions == null)
                return;

            var targetFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(targetPlayerId);
            if (targetFaction != null)
            {
                var player = UtilsPlayer.GetPlayer(sourcePlayerId);
                if (player == null)
                    return;

                var myFaction = GetPlayerFaction(player.IdentityId);
                if (myFaction == null || myFaction.FactionId != targetFaction.FactionId)
                {
                    var currentReputation = MyVisualScriptLogicProvider.GetRelationBetweenPlayerAndFaction(sourcePlayerId, targetFaction.Tag);
                    if (currentReputation > MinFactionReputation)
                    {
                        var newReputation = currentReputation - amount;
                        if (newReputation < MinFactionReputation)
                            newReputation = MinFactionReputation;

                        MyVisualScriptLogicProvider.SetRelationBetweenPlayerAndFaction(sourcePlayerId, targetFaction.Tag, newReputation);
                    }
                }
            }
        }

        private static IMyFaction GetPlayerFaction(long playerId)
        {
            return MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
        }
    }
}
