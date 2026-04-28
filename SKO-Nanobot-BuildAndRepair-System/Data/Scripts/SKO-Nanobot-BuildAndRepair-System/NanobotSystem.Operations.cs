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
                // BUG-014: On first ready tick (or after re-enable), trigger an immediate
                // scan WITH sources so the BaR doesn't operate with empty source/push lists.
                // Skip operations this tick — they'll start once the scan completes.
                if (!_InitialScanCompleted)
                {
                    _LastSourceUpdate = -Mod.Settings.SourcesUpdateInterval;
                    _LastTargetsUpdate = TimeSpan.Zero;
                    UpdateSourcesAndTargetsTimer();
                }
                else
                {
                    // FEAT-039: Skip all sub-method dispatch for idle BaRs.
                    // When there are no targets, no transport, and inventory isn't full,
                    // calling ServerTryWelding/Grinding/Collecting would each just exit
                    // immediately but incur profiler + method-call overhead (~0.15ms per BaR).
                    var isIdleNoWork = State.PossibleWeldTargets.CurrentCount == 0
                        && State.PossibleGrindTargets.CurrentCount == 0
                        && State.PossibleFloatingTargets.CurrentCount == 0
                        && State.CurrentTransportStartTime <= TimeSpan.Zero
                        && _TransportInventory.CurrentVolume == 0
                        && !State.InventoryFull;

                    // BUG-089: Don't take the idle fast-path when auto-push is enabled and
                    // the welder still has leftover items — otherwise ServerTryPushInventory
                    // never runs and items from the last grind cycle pile up indefinitely.
                    // GetInventory is only called in the idle corner case with push enabled,
                    // so the hot path pays nothing extra.
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

                    // BUG-015: Proactively detect full welder inventory after push attempt.
                    // If welder is full and we couldn't push, mark inventory full early
                    // so grinding/collecting are blocked before wasting a cycle.
                    var diagTs = MethodProfiler.Start();
                    CheckAndUpdateInventoryFull();
                    if (diagTs != 0L)
                    {
                        MethodProfiler.StopAndLog("CheckAndUpdateInventoryFull", diagTs, () =>
                            string.Format("entityId={0}", _Welder.EntityId));
                    }

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
                    // BUG-103: Don't gate the work-mode dispatch on the cosmetic transport timer.
                    // Items picked by ServerFindMissingComponents are already in the welder; the
                    // 5-6s timer was only driving the visual particle. Welding and grinding now
                    // proceed every work cycle; the inner ServerFindMissingComponents/IsTransportRunning
                    // calls still prevent restarting an in-flight transport.
                    State.MissingComponents.Clear();
                    State.LimitsExceeded = false;

                    diagTs = MethodProfiler.Start();
                    if (!Mod.Settings.DisableLimitSystemsPerTargetGrid
                        && (State.PossibleWeldTargets.CurrentCount > 0 || State.PossibleGrindTargets.CurrentCount > 0))
                    {
                        RebuildSaturatedGrids();
                    }
                    if (diagTs != 0L)
                    {
                        MethodProfiler.StopAndLog("RebuildSaturatedGrids", diagTs, () =>
                            string.Format("entityId={0};saturated={1}", _Welder.EntityId, _saturatedGridIds.Count));
                    }

                    switch (Settings.WorkMode)
                    {
                        case WorkModes.WeldBeforeGrind:
                        case WorkModes.GrindIfWeldGetStuck: // deprecated; treated as WeldBeforeGrind (defense; setter migrates on entry)
                            ServerTryWelding(out welding, out needWelding, out transporting, out currentWeldingBlock);
                            if (!(welding || transporting) || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedGrindingBlock != null))
                            {
                                primaryStuck = needWelding && !welding;
                                ServerTryGrinding(out grinding, out needGrinding, out transporting, out currentGrindingBlock);
                            }
                            break;

                        case WorkModes.GrindBeforeWeld:
                            ServerTryGrinding(out grinding, out needGrinding, out transporting, out currentGrindingBlock);
                            if (!(grinding || transporting) || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedWeldingBlock != null))
                            {
                                primaryStuck = needGrinding && !grinding;
                                ServerTryWelding(out welding, out needWelding, out transporting, out currentWeldingBlock);
                            }
                            break;

                        case WorkModes.WeldOnly:
                            ServerTryWelding(out welding, out needWelding, out transporting, out currentWeldingBlock);
                            break;

                        case WorkModes.GrindOnly:
                            ServerTryGrinding(out grinding, out needGrinding, out transporting, out currentGrindingBlock);
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

            // Subtract this system's contribution (mirrors the old "if (system == this) continue" logic).
            long myWeldGridId = 0;
            long myGrindGridId = 0;
            var myWeldBlock = State.CurrentWeldingBlock;
            if (myWeldBlock != null && myWeldBlock.CubeGrid != null)
                myWeldGridId = myWeldBlock.CubeGrid.EntityId;
            var myGrindBlock = State.CurrentGrindingBlock;
            if (myGrindBlock != null && myGrindBlock.CubeGrid != null)
                myGrindGridId = myGrindBlock.CubeGrid.EntityId;

            if (myWeldGridId == gridEntityId)
                count--;
            if (myGrindGridId == gridEntityId && myGrindGridId != myWeldGridId)
                count--;

            return count;
        }

        /// <summary>
        /// Checks the welder inventory and sets State.InventoryFull if at capacity.
        /// Shared by Operations (pre-weld/grind) and Collecting (pre-collect).
        /// </summary>
        private void CheckAndUpdateInventoryFull()
        {
            if (!State.InventoryFull && !CreativeModeActive)
            {
                var welderInventory = _Welder.GetInventory(0);
                if (welderInventory != null && (float)welderInventory.CurrentVolume >= (float)welderInventory.MaxVolume)
                {
                    State.InventoryFull = true;
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
            if (_saturatedGridIds.Contains(gridId)
                || gridId == lastRejectedGridId
                || GetCachedSystemCountOnGrid(gridId) >= Mod.Settings.MaxSystemsPerTargetGrid)
            {
                lastRejectedGridId = gridId;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Precomputes the set of grids that are definitely over MaxSystemsPerTargetGrid.
        /// Uses strictly-greater-than (>) because the cache includes this BaR's own count
        /// and GetCachedSystemCountOnGrid subtracts at most 1 for self.
        /// Called once per tick before weld/grind loops.
        /// </summary>
        private void RebuildSaturatedGrids()
        {
            _saturatedGridIds.Clear();
            var limit = Mod.Settings.MaxSystemsPerTargetGrid;
            foreach (var kvp in Mod.GridSystemCount)
            {
                if (kvp.Value > limit)
                    _saturatedGridIds.Add(kvp.Key);
            }
        }

        /// <summary>
        /// FEAT-038: Transmit state with progressive backoff for unchanged state.
        /// Compares a lightweight fingerprint of key state fields against last transmit.
        /// When unchanged, progressively extends the interval (1-2s → 2-4s → 4-8s).
        /// Resets to base interval on any visible state change.
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
            if (fingerprintChanged)
            {
                _transmitBackoffMultiplier = 1;
                _lastTransmittedFingerprint = fingerprint;
            }

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
                    string.Format("entityId={0};action=send;fpChanged={1};backoff={2};excluded={3}",
                        _Welder.EntityId, fingerprintChanged, _transmitBackoffMultiplier, excludedBefore));
            }
        }

        /// <summary>
        /// Lightweight hash of key visible state fields for transmit backoff comparison.
        /// Captures working state + target counts — enough to detect meaningful changes
        /// without comparing full serialized payloads.
        /// </summary>
        private int ComputeStateFingerprint()
        {
            var hash = 17;
            hash = hash * 31 + (State.Ready ? 1 : 0);
            hash = hash * 31 + (State.Welding ? 1 : 0);
            hash = hash * 31 + (State.NeedWelding ? 1 : 0);
            hash = hash * 31 + (State.Grinding ? 1 : 0);
            hash = hash * 31 + (State.NeedGrinding ? 1 : 0);
            hash = hash * 31 + (State.NeedCollecting ? 1 : 0);
            hash = hash * 31 + (State.Transporting ? 1 : 0);
            hash = hash * 31 + (State.InventoryFull ? 1 : 0);
            hash = hash * 31 + (State.LimitsExceeded ? 1 : 0);
            hash = hash * 31 + State.PossibleWeldTargets.CurrentCount;
            hash = hash * 31 + State.PossibleGrindTargets.CurrentCount;
            hash = hash * 31 + State.PossibleFloatingTargets.CurrentCount;
            return hash;
        }
    }
}
