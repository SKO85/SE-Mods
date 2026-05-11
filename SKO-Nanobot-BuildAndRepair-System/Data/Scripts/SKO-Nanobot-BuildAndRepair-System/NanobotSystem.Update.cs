using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Diagnostics;
using VRage.Game;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        // FEAT-AI: how aggressively to stretch the BaR's update cadence when its
        // cluster is in idle backoff (>= IdleScansBeforeBackoff consecutive empty
        // scans) and this BaR has no active state. 4× means an idle BaR running
        // at WorkSpeed=1 with effectiveGroups=3 fires every ~12 cycles ≈ 20s
        // instead of every ~3 cycles ≈ 5s — the same order as IdleScanInterval,
        // matching the assumption that nothing is going to happen in the gap.
        private const int IdleCadenceMultiplier = 4;

        public override void UpdateBeforeSimulation()
        {
            try
            {
                base.UpdateBeforeSimulation();

                if (_Welder == null || !_IsInit) return;

                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    if ((Settings.Flags & SyncBlockSettings.Settings.ShowArea) != 0)
                    {
                        var color = Color.Black;
                        var areaBoundingBox = Settings.CorrectedAreaBoundingBox;
                        var emitterMatrix = _Welder.WorldMatrix;
                        emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
                        MySimpleObjectDraw.DrawTransparentBox(ref emitterMatrix, ref areaBoundingBox, ref color, MySimpleObjectRasterizer.Solid, 1, 0.04f, RangeGridResourceId, null, false);
                    }

                    _UpdateEffectsInterval = (++_UpdateEffectsInterval) % 2;
                    if (_UpdateEffectsInterval == 0) _Effects.UpdateEffects(this);
                }
            }
            catch (Exception ex)
            {
                if (Logging.Instance.ShouldLog(Logging.Level.Error))
                {
                    Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: UpdateBeforeSimulation Exception:{1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                }
            }
        }

        public override void UpdateBeforeSimulation10()
        {
            base.UpdateBeforeSimulation10();
            UpdateBeforeSimulation10_100();
        }

        /// <summary>
        /// True when the cluster coordinator has seen IdleScansBeforeBackoff or more
        /// consecutive empty scans AND this BaR is not holding any active state
        /// (no scan targets, no transport in flight, no buffered inventory). Used by
        /// UpdateBeforeSimulation10_100 to stretch the per-BaR update cadence on
        /// idle fleets, saving wrapper overhead (Settings.TrySave, TryTransmitState,
        /// periodic checks) without delaying the response when work appears.
        /// Mirrors the guards in Operations.isIdleNoWork — match that set so the
        /// stretch only kicks in when ServerTryWeldingGrindingCollecting would
        /// have taken its idle fast path anyway.
        /// </summary>
        private bool IsIdleForCadenceStretch()
        {
            var cluster = AssignedCluster;
            if (cluster == null) return false;
            var coordinator = cluster.Coordinator;
            var idleCount = coordinator != null ? coordinator._consecutiveEmptyScans : 0;
            if (idleCount < IdleScansBeforeBackoff) return false;

            if (State.PossibleWeldTargets.CurrentCount > 0
                || State.PossibleGrindTargets.CurrentCount > 0
                || State.PossibleFloatingTargets.CurrentCount > 0
                || State.CurrentTransportStartTime > TimeSpan.Zero
                || _TransportInventory.CurrentVolume > 0
                || State.InventoryFull)
            {
                return false;
            }
            return true;
        }

        private void UpdateBeforeSimulation10_100()
        {
            var profilerTs = MethodProfiler.Start();
            var throttleReason = "none";
            var clusterSize = 1;
            var effectiveGroups = 1;
            // BUG-121: sub-timers for unprofiled wrapper segments.
            var tsPeriodic = 0L;
            var tsResourceSink = 0L;
            var tsSettingsSave = 0L;
            var tsMsgSend = 0L;
            try
            {
                if (_Welder == null) return;
                if (!_IsInit) Init();
                if (!_IsInit) return;

                if (_Delay > 0)
                {
                    _Delay--;
                    throttleReason = "delay";
                    return;
                }

                _DelayWatch.Restart();


                if (MyAPIGateway.Session.IsServer)
                {
                    // BUG-138: disabled-BaR fast path skips stagger/save/transmit.
                    // BUG-151: reset all work-state flags on the disable transition so
                    // client-side animations/sounds stop (BUG-150 fingerprint forces real send).
                    if (!_Welder.Enabled || !_Welder.IsFunctional)
                    {
                        if (State.Ready)
                        {
                            State.Ready = false;
                            State.Welding = false;
                            State.NeedWelding = false;
                            State.Grinding = false;
                            State.NeedGrinding = false;
                            State.NeedCollecting = false;
                            State.Transporting = false;
                            State.CurrentWeldingBlock = null;
                            State.CurrentGrindingBlock = null;
                            State.CurrentTransportTarget = null;
                            State.CurrentTransportStartTime = TimeSpan.Zero;
                            State.CurrentTransportTime = TimeSpan.Zero;
                            // BUG-152: bypass all transmit gates for the disable transition.
                            State.ForceFullTransmit();
                            NetworkMessagingHandler.MsgBlockStateSend(0, this);
                            _UpdateStateTransmitLast = MyAPIGateway.Session.ElapsedPlayTime;
                            _UpdateStateTransmitInterval = 0;
                            _transmitBackoffMultiplier = 1;
                        }
                    }
                    else
                    {
                    CreativeModeActive = MyAPIGateway.Session.CreativeMode;

                    // BUG-130: per-BaR CleanupFriendlyDamage retired (handled at Mod level).

                    // WorkSpeed controls operation frequency:
                    //   1 = every 100 frames (same as old Update100, default)
                    //  10 = every 10 frames (same as old Update10, fastest)
                    // Stagger distributes BaRs within each cycle.
                    var workSpeed = Math.Max(1, Math.Min(10, Mod.Settings.Welder.WorkSpeed));
                    var cycleDivisor = 100 / workSpeed;
                    var cycle = MyAPIGateway.Session.GameplayFrameCounter / cycleDivisor;
                    clusterSize = AssignedCluster != null ? AssignedCluster.Members.Count : 1;
                    var modWideStagger = Mod.GetEffectiveStaggerGroupCount();
                    if (clusterSize == 1)
                    {
                        // BUG-102: isolated BaRs use mod-wide stagger directly.
                        effectiveGroups = modWideStagger;
                    }
                    else if (clusterSize < 6)
                    {
                        // Small cluster: shared scan amortizes the work. Collapse to 1 group.
                        effectiveGroups = 1;
                    }
                    else
                    {
                        effectiveGroups = Math.Min(modWideStagger, clusterSize - 3);
                    }

                    var simSpeed = Mod.GetEffectiveSimSpeed();
                    if (simSpeed < 0.9f)
                    {
                        var simPenalty = (int)Math.Ceiling((1.0 - simSpeed) * modWideStagger);
                        effectiveGroups = Math.Min(modWideStagger, effectiveGroups + simPenalty);
                    }

                    // Idle cadence stretch: less frequent wrapper firing during cluster idle backoff.
                    var idleStretched = false;
                    if (effectiveGroups > 0 && IsIdleForCadenceStretch())
                    {
                        effectiveGroups *= IdleCadenceMultiplier;
                        idleStretched = true;
                    }

                    var isMyTurn = _staggerSlot < 0 || effectiveGroups <= 1 || (cycle % effectiveGroups) == (_staggerSlot % effectiveGroups);
                    throttleReason = isMyTurn ? "fired" : (idleStretched ? "idleStretch" : "stagger");

                    // Sim-speed override: simulate the reduced tick rate.
                    if (isMyTurn && Mod.SimSpeedOverride.HasValue && Mod.SimSpeedOverride.Value < 1.0f)
                    {
                        var skipInterval = (int)Math.Round(1.0 / Mod.SimSpeedOverride.Value);
                        if (skipInterval > 1)
                        {
                            if ((cycle % skipInterval) != 0)
                            {
                                isMyTurn = false;
                                throttleReason = "simSkip";
                            }
                        }
                    }

                    // WorkSpeed throttle: execute at most once per cycle.
                    if (isMyTurn && cycle == _lastWorkCycle)
                    {
                        isMyTurn = false;
                        throttleReason = "workCycle";
                    }
                    if (isMyTurn)
                    {
                        _lastWorkCycle = cycle;
                        ServerTryWeldingGrindingCollecting();
                    }

                    if (State.Ready && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_PeriodicExtraChecksLast).TotalSeconds >= 2)
                    {
                        var tsPeriodicMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                        _PeriodicExtraChecksLast = MyAPIGateway.Session.ElapsedPlayTime;
                        try
                        {
                            SetSafeZoneAndShieldStates();
                            UpdateCustomInfo(true);
                        }
                        catch { }
                        if (tsPeriodicMark != 0L) tsPeriodic = Stopwatch.GetTimestamp() - tsPeriodicMark;
                    }

                    if (State.Ready && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_UpdatePowerSinkLast).TotalSeconds >= 2)
                    {
                        var tsResourceMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                        _UpdatePowerSinkLast = MyAPIGateway.Session.ElapsedPlayTime;
                        var resourceSink = _Welder.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
                        if (resourceSink != null)
                        {
                            resourceSink.Update();
                        }
                        if (tsResourceMark != 0L) tsResourceSink = Stopwatch.GetTimestamp() - tsResourceMark;
                    }

                    TryTransmitState();
                    }

                    // Persist settings for both enabled and disabled welders so admin/script
                    // edits made while a BaR is off aren't lost on reload. TrySave is cheap
                    // when nothing changed (Changed-bit gate + 20s debounce).
                    var tsSettingsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                    Settings.TrySave(Entity, Mod.ModGuid);
                    if (tsSettingsMark != 0L) tsSettingsSave = Stopwatch.GetTimestamp() - tsSettingsMark;
                }
                else
                {
                    if (State.Changed)
                    {
                        UpdateCustomInfo(true);
                        State.ResetChanged();
                    }
                }

                if (Settings.IsTransmitNeeded() && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_UpdateSettingsTransmitLast).TotalSeconds >= TransmitSettingsIntervalSeconds)
                {
                    var tsMsgMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                    _UpdateSettingsTransmitLast = MyAPIGateway.Session.ElapsedPlayTime;
                    NetworkMessagingHandler.MsgBlockSettingsSend(0, this);
                    if (tsMsgMark != 0L) tsMsgSend = Stopwatch.GetTimestamp() - tsMsgMark;

                    // Settings just mutated locally (terminal toggle, scripting API, etc.) and we
                    // broadcast to clients. On a server-with-player host the broadcast doesn't
                    // echo back, so the network-receive path's SettingsChanged() never fires for
                    // us — and TriggerImmediateRescan never gets called, leaving the BaR working
                    // the OLD sorted target list until the next scheduled scan (up to
                    // TargetsUpdateInterval = 10 s). Calling SettingsChanged() here closes the
                    // gap so a near/far toggle takes effect within 1-2 s.
                    if (MyAPIGateway.Session.IsServer)
                    {
                        SettingsChanged();
                    }
                }

                if (_UpdateCustomInfoNeeded) UpdateCustomInfo(false);

                _DelayWatch.Stop();
                if (_DelayWatch.ElapsedMilliseconds > 40)
                {
                    _Delay = _RandomDelay.Next(1, 20); //Slowdown a little bit
                }
            }
            catch (Exception ex)
            {
                if (Logging.Instance.ShouldLog(Logging.Level.Error))
                {
                    Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: UpdateBeforeSimulation10 Exception:{1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                }
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var workSpeed = Math.Max(1, Math.Min(10, Mod.Settings.Welder.WorkSpeed));
                    var _throttleReason = throttleReason;
                    var _clusterSize = clusterSize;
                    var _effectiveGroups = effectiveGroups;
                    var tsFreq = Stopwatch.Frequency;
                    var _periodicMs = tsPeriodic * 1000.0 / tsFreq;
                    var _resourceSinkMs = tsResourceSink * 1000.0 / tsFreq;
                    var _settingsSaveMs = tsSettingsSave * 1000.0 / tsFreq;
                    var _msgSendMs = tsMsgSend * 1000.0 / tsFreq;
                    MethodProfiler.StopAndLog("UpdateBeforeSimulation10_100", profilerTs, () =>
                        string.Format("entityId={0};workSpeed={1};ready={2};delay={3};clusterSize={4};effectiveGroups={5};throttle={6};periodicMs={7:F3};resourceSinkMs={8:F3};settingsSaveMs={9:F3};msgSendMs={10:F3}",
                            _Welder != null ? _Welder.EntityId : 0, workSpeed, _IsInit, _Delay,
                            _clusterSize, _effectiveGroups, _throttleReason,
                            _periodicMs, _resourceSinkMs, _settingsSaveMs, _msgSendMs));
                }
            }
        }
    }
}
