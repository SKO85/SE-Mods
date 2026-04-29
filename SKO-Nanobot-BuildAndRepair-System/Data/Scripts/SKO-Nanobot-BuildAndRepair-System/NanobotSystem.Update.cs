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

        private void UpdateBeforeSimulation10_100()
        {
            var profilerTs = MethodProfiler.Start();
            var throttleReason = "none";
            var clusterSize = 1;
            var effectiveGroups = 1;
            // BUG-121: sub-timers for the unprofiled wrapper segments. Latest profile shows
            // 59 ms / 49 ms spikes in this method outside ServerTryWeldingGrindingCollecting,
            // including on throttle=workCycle samples where the work payload is skipped.
            // Goal here is diagnosis only — pin down which segment dominates, then file the fix.
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
                    CreativeModeActive = MyAPIGateway.Session.CreativeMode;

                    var friendlyDmgTs = MethodProfiler.Start();
                    CleanupFriendlyDamage();
                    if (friendlyDmgTs != 0L)
                    {
                        MethodProfiler.StopAndLog("CleanupFriendlyDamage", friendlyDmgTs, () =>
                            string.Format("entityId={0};entries={1}", _Welder.EntityId, FriendlyDamage.Count));
                    }

                    // WorkSpeed controls operation frequency:
                    //   1 = every 100 frames (same as old Update100, default)
                    //  10 = every 10 frames (same as old Update10, fastest)
                    // Stagger distributes BaRs within each cycle.
                    var workSpeed = Math.Max(1, Math.Min(10, Mod.Settings.Welder.WorkSpeed));
                    var cycleDivisor = 80 / workSpeed;
                    var cycle = MyAPIGateway.Session.GameplayFrameCounter / cycleDivisor;
                    clusterSize = AssignedCluster != null ? AssignedCluster.Members.Count : 1;
                    var modWideStagger = Mod.GetEffectiveStaggerGroupCount();
                    if (clusterSize == 1)
                    {
                        // Isolated BaR (no cluster): no shared scan amortization. Use mod-wide stagger
                        // directly so N isolated BaRs don't all fire on the same tick (BUG-102).
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

                    var isMyTurn = _staggerSlot < 0 || effectiveGroups <= 1 || (cycle % effectiveGroups) == (_staggerSlot % effectiveGroups);
                    throttleReason = isMyTurn ? "fired" : "stagger";

                    // When sim-speed override is active, simulate the reduced tick rate.
                    // Real low sim-speed naturally halves ticks; the override must replicate that.
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

                    // Ensure we only execute once per cycle (WorkSpeed throttle).
                    // Each cycle spans (cycleDivisor / 10) Update10 ticks; without this guard
                    // the BaR would fire on every tick within the cycle instead of just one.
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

                    var tsSettingsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                    Settings.TrySave(Entity, Mod.ModGuid);
                    if (tsSettingsMark != 0L) tsSettingsSave = Stopwatch.GetTimestamp() - tsSettingsMark;

                    TryTransmitState();
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
