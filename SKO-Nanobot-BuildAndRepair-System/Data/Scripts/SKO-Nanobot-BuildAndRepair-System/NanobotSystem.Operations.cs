using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        /// <summary>
        /// Try to weld/grind/collect the possible targets
        /// </summary>
        private void ServerTryWeldingGrindingCollecting()
        {
            var profilerTs = MethodProfiler.Start();
            var primaryStuck = false;
            var transportBlocked = false;
            try
            {
            var inventoryFull = State.InventoryFull;
            var limitsExceeded = State.LimitsExceeded;

            var welding = false;
            var needWelding = false;
            var grinding = false;
            var needGrinding = false;
            var collecting = false;
            var needCollecting = false;
            var transporting = false;

            if (_Welder.Closed || _Welder.MarkedForClose)
                return;

            var ready = _Welder.Enabled && _Welder.IsWorking && _Welder.IsFunctional;

            // Fast path: block was already off and still off, no transport in progress, nothing to drain.
            // The transition tick (State.Ready -> false) has already run, so the panel is up to date.
            if (!ready && !State.Ready && !State.Transporting && _TransportInventory.CurrentVolume == 0)
            {
                return;
            }

            IMySlimBlock currentWeldingBlock = null;
            IMySlimBlock currentGrindingBlock = null;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var isFullInventoryAndPicking = State.InventoryFull && State.CurrentTransportIsPick;

            if (ready)
            {
                // BUG-014: trigger immediate scan on first ready tick so we don't operate
                // with empty source/push lists. Skip operations until the scan completes.
                if (!_InitialScanCompleted)
                {
                    _LastSourceUpdate = -Mod.Settings.SourcesUpdateInterval;
                    _LastTargetsUpdate = TimeSpan.Zero;
                    UpdateSourcesAndTargetsTimer();
                }
                else
                {
                    // FEAT-039: skip sub-method dispatch for idle BaRs.
                    var isIdleNoWork = State.PossibleWeldTargets.CurrentCount == 0
                        && State.PossibleGrindTargets.CurrentCount == 0
                        && State.PossibleFloatingTargets.CurrentCount == 0
                        && State.CurrentTransportStartTime <= TimeSpan.Zero
                        && _TransportInventory.CurrentVolume == 0
                        && !State.InventoryFull;

                    // BUG-089: don't take idle fast-path when auto-push is on and welder has items.
                    if (isIdleNoWork
                        && (Settings.Flags & (SyncBlockSettings.Settings.PushIngotOreImmediately | SyncBlockSettings.Settings.PushComponentImmediately | SyncBlockSettings.Settings.PushItemsImmediately)) != 0)
                    {
                        var welderInv = _Welder.GetInventory(0);
                        if (welderInv != null && !welderInv.Empty())
                            isIdleNoWork = false;
                    }

                    if (!isIdleNoWork)
                    {
                    ServerTryPushInventory();

                    // BUG-015: detect full welder inventory after push so grind/collect skip early.
                    // BUG-260526.1: also clears the flag once the welder drains.
                    CheckAndUpdateInventoryFull();

                    if (isFullInventoryAndPicking)
                    {
                        State.LastTransportTarget = State.CurrentTransportTarget;
                        State.CurrentTransportTarget = null;
                        State.CurrentTransportStartTime = TimeSpan.Zero;
                        transporting = false;
                    }
                    else
                    {
                        transporting = IsTransportRunning(playTime);
                    }

                    if (transporting && State.CurrentTransportIsPick)
                    {
                        if (State.CurrentTransportIsCollecting) needCollecting = true;
                        else needGrinding = true;
                    }

                    if ((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) == 0 && !transporting)
                        ServerTryCollectingFloatingTargets(out collecting, out needCollecting, out transporting);

                    transportBlocked = transporting;
                    // BUG-103: don't gate work dispatch on the cosmetic transport timer.
                    State.MissingComponents.Clear();
                    State.LimitsExceeded = false;

                    var diagTs = MethodProfiler.Start();
                    if (!Mod.Settings.DisableLimitSystemsPerTargetGrid
                        && (State.PossibleWeldTargets.CurrentCount > 0 || State.PossibleGrindTargets.CurrentCount > 0))
                    {
                        _gridSaturation.Rebuild();
                    }
                    if (diagTs != 0L)
                    {
                        MethodProfiler.StopAndLog("RebuildSaturatedGrids", diagTs, () =>
                            string.Format("entityId={0};saturated={1}", _Welder.EntityId, _gridSaturation.Count));
                    }

                    switch (Settings.WorkMode)
                    {
                        case WorkModes.WeldBeforeGrind:
                        case WorkModes.GrindIfWeldGetStuck: // deprecated; treated as WeldBeforeGrind (defense; setter migrates on entry)
                            MultiWeld(ref welding, ref needWelding, ref transporting, ref currentWeldingBlock);
                            if (!(welding || transporting) || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedGrindingBlock != null))
                            {
                                primaryStuck = needWelding && !welding;
                                MultiGrind(ref grinding, ref needGrinding, ref transporting, ref currentGrindingBlock);
                            }
                            break;

                        case WorkModes.GrindBeforeWeld:
                            MultiGrind(ref grinding, ref needGrinding, ref transporting, ref currentGrindingBlock);
                            if (!(grinding || transporting) || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedWeldingBlock != null))
                            {
                                primaryStuck = needGrinding && !grinding;
                                MultiWeld(ref welding, ref needWelding, ref transporting, ref currentWeldingBlock);
                            }
                            break;

                        case WorkModes.WeldOnly:
                            MultiWeld(ref welding, ref needWelding, ref transporting, ref currentWeldingBlock);
                            break;

                        case WorkModes.GrindOnly:
                            MultiGrind(ref grinding, ref needGrinding, ref transporting, ref currentGrindingBlock);
                            break;
                    }
                    State.MissingComponents.RebuildHash();

                    if (((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) != 0) && !transporting && !welding && !grinding)
                        ServerTryCollectingFloatingTargets(out collecting, out needCollecting, out transporting);
                    }
                }
            }
            else
            {
                if (isFullInventoryAndPicking)
                {
                    State.LastTransportTarget = State.CurrentTransportTarget;
                    State.CurrentTransportTarget = null;
                    State.CurrentTransportStartTime = TimeSpan.Zero;
                    transporting = false;
                }
                else
                {
                    transporting = IsTransportRunning(playTime); //Finish running transport
                }

                State.MissingComponents.Clear();
                State.MissingComponents.RebuildHash();
            }

            if (!(welding || grinding || collecting || transporting) && _TransportInventory.CurrentVolume > 0)
            {
                // Idle but not empty -> empty inventory
                if (!isFullInventoryAndPicking && State.LastTransportTarget.HasValue)
                {
                    State.CurrentTransportIsPick = true;
                    State.CurrentTransportIsCollecting = false;
                    State.CurrentTransportTarget = State.LastTransportTarget;
                    State.CurrentTransportStartTime = playTime;

                    transporting = true;
                }

                if (ready)
                    ServerEmptyTransportInventory(true);
            }

            if (((State.Welding && !welding) || (State.Grinding && !(grinding || collecting))))
            {
                if (!isFullInventoryAndPicking && ready)
                {
                    TriggerImmediateRescan(State.Welding ? "weldComplete" : "grindComplete");
                }
            }

            // When transporting components for welding (delivery, not a pick), preserve the
            // previous NeedWelding state so the "Blocks to Build" panel remains visible and
            // MissingComponents doesn't flicker while the transport timer is ticking.
            if (transporting && !State.CurrentTransportIsPick && !needWelding && State.NeedWelding)
            {
                needWelding = State.NeedWelding;
                currentWeldingBlock = State.CurrentWeldingBlock;
            }

            var readyChanged = State.Ready != ready;
            var weldingChanged = State.Welding != welding;
            var needWeldingChanged = State.NeedWelding != needWelding;
            var grindingChanged = State.Grinding != grinding;
            var needGrindingChanged = State.NeedGrinding != needGrinding;
            var needCollectingChanged = State.NeedCollecting != needCollecting;
            State.Ready = ready;
            State.Welding = welding;
            State.NeedWelding = needWelding;
            State.CurrentWeldingBlock = currentWeldingBlock;

            State.Grinding = grinding;
            State.NeedGrinding = needGrinding;
            State.NeedCollecting = needCollecting;
            State.CurrentGrindingBlock = currentGrindingBlock;

            var transportChanged = State.Transporting != transporting;
            State.Transporting = transporting;

            var inventoryFullChanged = State.InventoryFull != inventoryFull;
            var limitsExceededChanged = State.LimitsExceeded != limitsExceeded;

            var missingComponentsChanged = State.MissingComponents.LastHash != State.MissingComponents.CurrentHash;
            State.MissingComponents.LastHash = State.MissingComponents.CurrentHash;

            var possibleWeldTargetsChanged = State.PossibleWeldTargets.LastHash != State.PossibleWeldTargets.CurrentHash;
            State.PossibleWeldTargets.LastHash = State.PossibleWeldTargets.CurrentHash;

            var possibleGrindTargetsChanged = State.PossibleGrindTargets.LastHash != State.PossibleGrindTargets.CurrentHash;
            State.PossibleGrindTargets.LastHash = State.PossibleGrindTargets.CurrentHash;

            var possibleFloatingTargetsChanged = State.PossibleFloatingTargets.LastHash != State.PossibleFloatingTargets.CurrentHash;
            State.PossibleFloatingTargets.LastHash = State.PossibleFloatingTargets.CurrentHash;

            if (missingComponentsChanged || possibleWeldTargetsChanged || possibleGrindTargetsChanged || possibleFloatingTargetsChanged || transportChanged)
                State.HasChanged();

            if (MyAPIGateway.Session.IsServer)
            {
                TryTransmitState();
            }

            UpdateCustomInfo(
                missingComponentsChanged ||
                possibleWeldTargetsChanged ||
                possibleGrindTargetsChanged ||
                possibleFloatingTargetsChanged ||
                readyChanged ||
                weldingChanged ||
                needWeldingChanged ||
                grindingChanged ||
                needGrindingChanged ||
                needCollectingChanged ||
                inventoryFullChanged ||
                limitsExceededChanged ||
                transportChanged);
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _transportBlocked = transportBlocked;
                    var _transportTimeMs = State.CurrentTransportTime.TotalMilliseconds;
                    var _workSpeed = Mod.Settings.Welder.WorkSpeed;
                    MethodProfiler.StopAndLog("ServerTryWeldingGrindingCollecting", profilerTs, () =>
                        string.Format("entityId={0};workMode={1};welding={2};grinding={3};needCollecting={4};transporting={5};transportIsCollecting={6};weldTargets={7};grindTargets={8};floatingTargets={9};inventoryFull={10};scanReady={11};pushFull={12};primaryStuck={13};transportBlocked={14};transportTimeMs={15:F1};workSpeed={16}",
                            _Welder.EntityId, Settings.WorkMode, State.Welding, State.Grinding,
                            State.NeedCollecting, State.Transporting, State.CurrentTransportIsCollecting,
                            State.PossibleWeldTargets.CurrentCount, State.PossibleGrindTargets.CurrentCount,
                            State.PossibleFloatingTargets.CurrentCount, State.InventoryFull,
                            _InitialScanCompleted, _PushTargetsFull, primaryStuck,
                            _transportBlocked, _transportTimeMs, _workSpeed));
                }
            }
        }

        // Cap on multi-action iterations per cycle per BaR.
        //
        // Set to 1 so each BaR claims exactly one grid slot per cycle. Higher
        // values let one BaR work blocks on multiple grids within a single cycle,
        // which silently bypasses MaxSystemsPerTargetGrid: iter 1 puts the BaR on
        // grid G (count += 1), iter 2 moves it to grid H (count[G] -= 1, count[H]
        // += 1) — so by end of cycle grid G is undercounted and another BaR can
        // sneak in. Throughput is paced by the per-tick budgets (MaxGrindsPerTick
        // + MaxGrindMsPerTick) which are now both configurable, so keeping this
        // at 1 doesn't reduce total work — it just distributes the budget more
        // evenly across BaRs.
        private const int MaxActionsPerCycle = 1;

        /// <summary>
        /// Calls ServerTryWelding repeatedly within a single cycle so the BaR can
        /// consume the full PerTickBudget instead of doing one weld per Update10.
        /// Stops when no further welding fires (no target, budget exhausted, or
        /// transport gate). The internal lock-on logic re-picks the same block
        /// each call until it completes, then moves to the next eligible block.
        ///
        /// State.CurrentWeldingBlock is committed between iterations so the next
        /// iteration's IsGridOverSystemLimit check sees the updated GridSystemCount
        /// — otherwise a BaR that touched grid G in iter 1 and grid H in iter 2
        /// looks "still on its previous grid" to the limit check inside iter 3,
        /// effectively bypassing MaxSystemsPerTargetGrid.
        /// </summary>
        private void MultiWeld(ref bool welding, ref bool needWelding, ref bool transporting, ref IMySlimBlock currentWeldingBlock)
        {
            for (int i = 0; i < MaxActionsPerCycle; i++)
            {
                bool w, nw, t;
                IMySlimBlock cwb;
                ServerTryWelding(out w, out nw, out t, out cwb);
                if (w) welding = true;
                if (nw) needWelding = true;
                if (t) transporting = true;
                if (cwb != null)
                {
                    currentWeldingBlock = cwb;
                    // Commit the lock-on/grid-count change immediately so subsequent
                    // iterations see the updated Mod.GridSystemCount.
                    State.CurrentWeldingBlock = cwb;
                }
                if (!w) break;
            }
        }

        /// <summary>
        /// Calls ServerTryGrinding repeatedly within a single cycle so the BaR can
        /// consume the full PerTickBudget instead of doing one grind per Update10.
        /// Stops when no further grinding fires (no target left, or budget exhausted).
        /// State.CurrentGrindingBlock is committed between iterations for the same
        /// MaxSystemsPerTargetGrid-correctness reason as MultiWeld above.
        /// </summary>
        private void MultiGrind(ref bool grinding, ref bool needGrinding, ref bool transporting, ref IMySlimBlock currentGrindingBlock)
        {
            for (int i = 0; i < MaxActionsPerCycle; i++)
            {
                bool g, ng, t;
                IMySlimBlock cgb;
                ServerTryGrinding(out g, out ng, out t, out cgb);
                if (g) grinding = true;
                if (ng) needGrinding = true;
                if (t) transporting = true;
                if (cgb != null)
                {
                    currentGrindingBlock = cgb;
                    State.CurrentGrindingBlock = cgb;
                }
                if (!g) break;
            }
        }

        /// <summary>
        /// Returns the cached count of OTHER systems targeting the given grid.
        /// Reads from the live Mod.GridSystemCount counter and subtracts this BaR's own
        /// contribution to match the old per-BaR "skip self" behavior.
        /// </summary>
        private int GetCachedSystemCountOnGrid(long gridEntityId)
        {
            int count;
            if (!Mod.GridSystemCount.TryGetValue(gridEntityId, out count))
                return 0;

            // BUG-160: each BaR contributes +1 per grid; subtract at most 1 here.
            var myWeldBlock = State.CurrentWeldingBlock;
            var myWeldGridId = (myWeldBlock != null && myWeldBlock.CubeGrid != null) ? myWeldBlock.CubeGrid.EntityId : 0L;
            if (myWeldGridId == gridEntityId)
            {
                count--;
            }
            else
            {
                var myGrindBlock = State.CurrentGrindingBlock;
                var myGrindGridId = (myGrindBlock != null && myGrindBlock.CubeGrid != null) ? myGrindBlock.CubeGrid.EntityId : 0L;
                if (myGrindGridId == gridEntityId) count--;
            }

            return count;
        }

        /// <summary>
        /// Checks the welder inventory and updates State.InventoryFull.
        /// Bidirectional with hysteresis: sets true at >=100% capacity, clears at &lt;90%.
        /// Shared by Operations (pre-weld/grind) and Collecting (pre-collect).
        /// </summary>
        private void CheckAndUpdateInventoryFull()
        {
            var profilerTs = MethodProfiler.Start();
            var wasFull = State.InventoryFull;
            var nowFull = wasFull;
            float currentVolume = 0f;
            float maxVolume = 0f;
            try
            {
                if (CreativeModeActive) return;

                var welderInventory = _Welder.GetInventory(0);
                if (welderInventory == null) return;

                currentVolume = (float)welderInventory.CurrentVolume;
                maxVolume = (float)welderInventory.MaxVolume;

                if (!wasFull)
                {
                    if (currentVolume >= maxVolume)
                    {
                        State.InventoryFull = true;
                        nowFull = true;
                    }
                }
                else
                {
                    // BUG-260526.1: clear the sticky InventoryFull flag once the welder
                    // has drained (e.g. ServerTryPushInventory moved items into a container).
                    // Without this, the flag stayed true forever because the only other
                    // clear path (ServerEmptyTransportInventory) requires an active transport.
                    // 90% hysteresis avoids flicker when a partial weld nudges it just under max.
                    if (currentVolume < maxVolume * 0.9f)
                    {
                        State.InventoryFull = false;
                        nowFull = false;
                    }
                }
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _wasFull = wasFull;
                    var _nowFull = nowFull;
                    var _currentVolume = currentVolume;
                    var _maxVolume = maxVolume;
                    MethodProfiler.StopAndLog("CheckAndUpdateInventoryFull", profilerTs, () =>
                        string.Format("entityId={0};wasFull={1};nowFull={2};currentVolume={3:F3};maxVolume={4:F3};fillPct={5:F1}",
                            _Welder.EntityId, _wasFull, _nowFull, _currentVolume, _maxVolume,
                            _maxVolume > 0f ? (_currentVolume / _maxVolume * 100f) : 0f));
                }
            }
        }

        /// <summary>
        /// Returns true if the grid has reached the MaxSystemsPerTargetGrid limit
        /// and should be skipped. Checks the precomputed saturated set first (O(1)),
        /// then falls back to the per-block dictionary lookup for edge cases.
        /// </summary>
        private bool IsGridOverSystemLimit(long gridId, ref long lastRejectedGridId)
        {
            if (Mod.Settings.DisableLimitSystemsPerTargetGrid) return false;
            if (_gridSaturation.Contains(gridId)
                || gridId == lastRejectedGridId
                || GetCachedSystemCountOnGrid(gridId) >= Mod.Settings.MaxSystemsPerTargetGrid)
            {
                lastRejectedGridId = gridId;
                return true;
            }
            return false;
        }

        /// <summary>
        /// BUG-164: resolve to the projector's parent grid for projected blocks so the
        /// limit check uses the same grid the post-materialization Inc fires on.
        /// </summary>
        private static long GetEffectiveGridId(IMySlimBlock block)
        {
            if (block == null || block.CubeGrid == null) return 0L;
            var myCubeGrid = block.CubeGrid as Sandbox.Game.Entities.MyCubeGrid;
            if (myCubeGrid != null && myCubeGrid.Projector != null && myCubeGrid.Projector.CubeGrid != null)
                return myCubeGrid.Projector.CubeGrid.EntityId;
            return block.CubeGrid.EntityId;
        }

        /// <summary>
        /// FEAT-038/BUG-150: fingerprint-gated state transmit with progressive backoff
        /// (skip the send entirely when nothing visible changed).
        /// </summary>
        private void TryTransmitState()
        {
            var profilerTs = MethodProfiler.Start();
            if (!State.IsTransmitNeeded() || !MyAPIGateway.Multiplayer.MultiplayerActive)
            {
                Mod.ReportSyncSkipped();
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("TryTransmitState", profilerTs, () =>
                        string.Format("entityId={0};action=skip;reason=notNeeded", _Welder.EntityId));
                }
                return;
            }

            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_UpdateStateTransmitLast).TotalSeconds < _UpdateStateTransmitInterval)
            {
                Mod.ReportSyncSkipped();
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("TryTransmitState", profilerTs, () =>
                        string.Format("entityId={0};action=skip;reason=interval;backoff={1}", _Welder.EntityId, _transmitBackoffMultiplier));
                }
                return;
            }

            var fingerprint = ComputeStateFingerprint();
            var fingerprintChanged = fingerprint != _lastTransmittedFingerprint;

            // BUG-150: skip the send when fingerprint matches; extend backoff.
            if (!fingerprintChanged)
            {
                State.ResetChanged();
                _UpdateStateTransmitLast = MyAPIGateway.Session.ElapsedPlayTime;
                var baseIntervalSkip = _RandomDelay.Next(TransmitStateMinIntervalSeconds, TransmitStateMaxIntervalSeconds + 1);
                _UpdateStateTransmitInterval = baseIntervalSkip * _transmitBackoffMultiplier;
                _transmitBackoffMultiplier = Math.Min(_transmitBackoffMultiplier * 2, 4);
                Mod.ReportSyncSkipped();
                if (profilerTs != 0L)
                {
                    var _backoffSkip = _transmitBackoffMultiplier;
                    MethodProfiler.StopAndLog("TryTransmitState", profilerTs, () =>
                        string.Format("entityId={0};action=skip;reason=fpUnchanged;backoff={1}", _Welder.EntityId, _backoffSkip));
                }
                return;
            }

            // Fingerprint changed → real send.
            _transmitBackoffMultiplier = 1;
            _lastTransmittedFingerprint = fingerprint;

            _UpdateStateTransmitLast = MyAPIGateway.Session.ElapsedPlayTime;
            var baseInterval = _RandomDelay.Next(TransmitStateMinIntervalSeconds, TransmitStateMaxIntervalSeconds + 1);
            _UpdateStateTransmitInterval = baseInterval * _transmitBackoffMultiplier;
            _transmitBackoffMultiplier = Math.Min(_transmitBackoffMultiplier * 2, 4);

            var excludedBefore = State.ExcludedLists;
            NetworkMessagingHandler.MsgBlockStateSend(0, this);
            Mod.ReportSyncSent();

            if (profilerTs != 0L)
            {
                MethodProfiler.StopAndLog("TryTransmitState", profilerTs, () =>
                    string.Format("entityId={0};action=send;fpChanged=True;backoff={1};excluded={2}",
                        _Welder.EntityId, _transmitBackoffMultiplier, excludedBefore));
            }
        }

        /// <summary>
        /// BUG-150: fingerprint of working state + target list CurrentHash; content-aware
        /// so a fingerprint match safely indicates "nothing visible changed".
        /// </summary>
        private long ComputeStateFingerprint()
        {
            // Mix bool flags into the high bits, list hashes into the low bits.
            long hash = 17;
            hash = hash * 31 + (State.Ready ? 1 : 0);
            hash = hash * 31 + (State.Welding ? 1 : 0);
            hash = hash * 31 + (State.NeedWelding ? 1 : 0);
            hash = hash * 31 + (State.Grinding ? 1 : 0);
            hash = hash * 31 + (State.NeedGrinding ? 1 : 0);
            hash = hash * 31 + (State.NeedCollecting ? 1 : 0);
            hash = hash * 31 + (State.Transporting ? 1 : 0);
            hash = hash * 31 + (State.InventoryFull ? 1 : 0);
            hash = hash * 31 + (State.LimitsExceeded ? 1 : 0);
            hash = hash * 31 + State.PossibleWeldTargets.CurrentHash;
            hash = hash * 31 + State.PossibleGrindTargets.CurrentHash;
            hash = hash * 31 + State.PossibleFloatingTargets.CurrentHash;
            hash = hash * 31 + State.MissingComponents.CurrentHash;
            // BUG-155: include transport target (quantized to int metres) so block-to-block
            // transitions trigger a transmit; otherwise client beam endpoint sticks.
            if (State.CurrentTransportTarget.HasValue)
            {
                var t = State.CurrentTransportTarget.Value;
                hash = hash * 31 + (long)t.X;
                hash = hash * 31 + (long)t.Y;
                hash = hash * 31 + (long)t.Z;
            }
            // BUG-260511.8: include lock-on targets (welding + grinding). Their setters
            // flip State.Changed when the physical block changes, but the fingerprint
            // didn't hash them — so the BUG-150 fpUnchanged early-return swallowed
            // lock-on changes and clients kept stale beam/effect targets.
            hash = hash * 31 + ComputeBlockKey(State.CurrentWeldingBlock);
            hash = hash * 31 + ComputeBlockKey(State.CurrentGrindingBlock);

            // BUG-260511.9: every SyncBlockState setter that flips Changed=true must
            // be reflected in the fingerprint, or the BUG-150 fpUnchanged path will
            // swallow the change. The audit caught two more groups:
            //   * Safe-zone / shield visibility — flips when entering/leaving a zone.
            //   * Transport metadata (LastTransportTarget, IsPick, Time, StartTime) —
            //     seeded once per transport (BUG-260511.1 gate), not per tick, so
            //     hashing them does not churn the fingerprint.
            hash = hash * 31 + (State.SafeZoneAllowsWelding ? 1 : 0);
            hash = hash * 31 + (State.SafeZoneAllowsGrinding ? 1 : 0);
            hash = hash * 31 + (State.SafeZoneAllowsBuildingProjections ? 1 : 0);
            hash = hash * 31 + (State.IsShielded ? 1 : 0);
            hash = hash * 31 + (State.CurrentTransportIsPick ? 1 : 0);
            hash = hash * 31 + State.CurrentTransportTime.Ticks;
            hash = hash * 31 + State.CurrentTransportStartTime.Ticks;
            if (State.LastTransportTarget.HasValue)
            {
                var lt = State.LastTransportTarget.Value;
                hash = hash * 31 + (long)lt.X;
                hash = hash * 31 + (long)lt.Y;
                hash = hash * 31 + (long)lt.Z;
            }
            return hash;
        }

        private static long ComputeBlockKey(IMySlimBlock block)
        {
            if (block == null) return 0L;
            var gridId = block.CubeGrid != null ? block.CubeGrid.EntityId : 0L;
            var pos = block.Position;
            // Mix grid id with the 3 int coords. Position values are small (block
            // coords on a grid), so a simple 31-multiplier mix is enough to avoid
            // collisions between different blocks on the same grid.
            long key = gridId;
            key = key * 31 + pos.X;
            key = key * 31 + pos.Y;
            key = key * 31 + pos.Z;
            return key;
        }
    }
}
