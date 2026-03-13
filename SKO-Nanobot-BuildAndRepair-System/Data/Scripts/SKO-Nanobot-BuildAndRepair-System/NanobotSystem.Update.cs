using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
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
                        var colorWelder = _Welder.SlimBlock.GetColorMask().HSVtoColor();
                        var color = Color.FromNonPremultiplied(colorWelder.R, colorWelder.G, colorWelder.B, 255);
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
            UpdateBeforeSimulation10_100(true);
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();
            UpdateBeforeSimulation10_100(false);
        }

        private void UpdateBeforeSimulation10_100(bool fast)
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                if (_Welder == null) return;
                if (!_IsInit) Init();
                if (!_IsInit) return;

                if (_Delay > 0)
                {
                    _Delay--;
                    return;
                }

                _DelayWatch.Restart();


                if (MyAPIGateway.Session.IsServer)
                {
                    CreativeModeActive = MyAPIGateway.Session.CreativeMode;

                    if (!fast)
                    {
                        CleanupFriendlyDamage();
                    }

                    ServerTryWeldingGrindingCollecting();

                    if (State.Ready && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_PeriodicExtraChecksLast).TotalSeconds >= 2)
                    {
                        _PeriodicExtraChecksLast = MyAPIGateway.Session.ElapsedPlayTime;
                        try
                        {
                            SetSafeZoneAndShieldStates();
                            UpdateCustomInfo(true);
                        }
                        catch { }
                    }

                    if (!fast)
                    {
                        if (State.Ready && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_UpdatePowerSinkLast).TotalSeconds >= 2)
                        {
                            _UpdatePowerSinkLast = MyAPIGateway.Session.ElapsedPlayTime;
                            var resourceSink = _Welder.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
                            if (resourceSink != null)
                            {
                                resourceSink.Update();
                            }
                        }

                        Settings.TrySave(Entity, Mod.ModGuid);

                        if (State.IsTransmitNeeded() && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_UpdateStateTransmitLast).TotalSeconds >= _UpdateStateTransmitInterval)
                        {
                            _UpdateStateTransmitLast = MyAPIGateway.Session.ElapsedPlayTime;
                            _UpdateStateTransmitInterval = _RandomDelay.Next(TransmitStateMinIntervalSeconds, TransmitStateMaxIntervalSeconds + 1);
                            NetworkMessagingHandler.MsgBlockStateSend(0, this);
                        }
                    }
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
                    _UpdateSettingsTransmitLast = MyAPIGateway.Session.ElapsedPlayTime;
                    NetworkMessagingHandler.MsgBlockSettingsSend(0, this);
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
                    Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: UpdateBeforeSimulation10/100 Exception:{1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                }
            }
            finally
            {
                MethodProfiler.StopAndLog("UpdateBeforeSimulation10_100", profilerTs, () =>
                    string.Format("entityId={0};fast={1};ready={2};delay={3}",
                        _Welder != null ? _Welder.EntityId : 0, fast, _IsInit, _Delay));
            }
        }
    }
}
