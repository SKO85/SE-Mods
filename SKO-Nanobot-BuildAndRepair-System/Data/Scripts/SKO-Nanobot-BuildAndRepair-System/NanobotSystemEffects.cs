using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Lights;
using Sandbox.ModAPI;
using System;
using System.Threading;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using static SKONanobotBuildAndRepairSystem.NanobotSystem;

namespace SKONanobotBuildAndRepairSystem
{
    public class NanobotSystemEffects
    {
        public static MySoundPair[] _Sounds = new[] { null, null, null, new MySoundPair("ToolLrgWeldMetal"), new MySoundPair("BlockModuleProductivity"), new MySoundPair("BaRUnable"), new MySoundPair("ToolLrgGrindMetal"), new MySoundPair("BlockModuleProductivity"), new MySoundPair("BaRUnable"), new MySoundPair("BaRUnable") };
        public static float[] _SoundLevels = new[] { 0f, 0f, 0f, 1f, 0.5f, 0.4f, 1f, 0.5f, 0.4f, 0.4f };

        public const string PARTICLE_EFFECT_WELDING1 = MyParticleEffectsNameEnum.WelderContactPoint;
        public const string PARTICLE_EFFECT_GRINDING1 = MyParticleEffectsNameEnum.ShipGrinder;
        public const string PARTICLE_EFFECT_TRANSPORT1_PICK = "GrindNanobotTrace1";
        public const string PARTICLE_EFFECT_TRANSPORT1_DELIVER = "WeldNanobotTrace1";

        public static readonly int MaxTransportEffects = 50;
        public static int _ActiveTransportEffects = 0;
        public static readonly int MaxWorkingEffects = 80;
        public static int _ActiveWorkingEffects = 0;

        public Vector3 EmitterPosition;
        public WorkingState WorkingStateSet = WorkingState.Invalid;
        public float SoundVolumeSet;

        private MyEntity3DSoundEmitter _SoundEmitter;
        private MyEntity3DSoundEmitter _SoundEmitterWorking;
        private Vector3D? _SoundEmitterWorkingPosition;
        private MyParticleEffect _ParticleEffectWorking1;
        private MyParticleEffect _ParticleEffectTransport1;
        private MyLight _LightEffect;
        private MyFlareDefinition _LightEffectFlareWelding;
        private MyFlareDefinition _LightEffectFlareGrinding;
        private bool _TransportStateSet;

        public void StopSoundEffects()
        {
            if (_SoundEmitter != null)
            {
                _SoundEmitter.StopSound(false);
            }

            if (_SoundEmitterWorking != null)
            {
                _SoundEmitterWorking.StopSound(false);
                _SoundEmitterWorking.SetPosition(null);
                _SoundEmitterWorkingPosition = null;
            }
        }

        /// <summary>
        /// Set actual state and position of visual effects
        /// </summary>
        public void UpdateEffects(NanobotSystem system)
        {
            var transportState = system.State.Transporting && system.State.CurrentTransportTarget != null;
            if (transportState != _TransportStateSet)
            {
                SetTransportEffects(system, transportState);
            }
            else
            {
                UpdateTransportEffectPosition(system);
            }

            // Welding/Grinding state
            var workingState = system.GetWorkingState();
            if (workingState != WorkingStateSet || system.Settings.SoundVolume != SoundVolumeSet)
            {
                SetWorkingEffects(system, workingState);
                WorkingStateSet = workingState;
                SoundVolumeSet = system.Settings.SoundVolume;
            }
            else
            {
                UpdateWorkingEffectPosition(system, workingState);
            }
        }

        /// <summary>
        /// Start visual effects for welding/grinding
        /// </summary>
        private void SetWorkingEffects(NanobotSystem system, WorkingState workingState)
        {
            if (_ParticleEffectWorking1 != null)
            {
                Interlocked.Decrement(ref _ActiveWorkingEffects);
                _ParticleEffectWorking1.Stop();
                _ParticleEffectWorking1 = null;
            }

            if (_LightEffect != null)
            {
                MyLights.RemoveLight(_LightEffect);
                _LightEffect = null;
            }

            switch (workingState)
            {
                case WorkingState.Welding:
                case WorkingState.Grinding:
                    if ((_ActiveWorkingEffects < MaxWorkingEffects) &&
                        ((workingState == WorkingState.Welding && ((Mod.Settings.Welder.AllowedEffects & VisualAndSoundEffects.WeldingVisualEffect) != 0)) ||
                         (workingState == WorkingState.Grinding && ((Mod.Settings.Welder.AllowedEffects & VisualAndSoundEffects.GrindingVisualEffect) != 0))))
                    {
                        Interlocked.Increment(ref _ActiveWorkingEffects);

                        MyParticlesManager.TryCreateParticleEffect(workingState == WorkingState.Welding ? PARTICLE_EFFECT_WELDING1 : PARTICLE_EFFECT_GRINDING1, ref MatrixD.Identity, ref Vector3D.Zero, uint.MaxValue, out _ParticleEffectWorking1);
                        if (_ParticleEffectWorking1 != null) _ParticleEffectWorking1.UserRadiusMultiplier = workingState == WorkingState.Welding ? 4f : 2f;// 0.5f;

                        if (workingState == WorkingState.Welding && _LightEffectFlareWelding == null)
                        {
                            MyDefinitionId myDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_FlareDefinition), "ShipWelder");
                            _LightEffectFlareWelding = MyDefinitionManager.Static.GetDefinition(myDefinitionId) as MyFlareDefinition;
                        }
                        else if (workingState == WorkingState.Grinding && _LightEffectFlareGrinding == null)
                        {
                            MyDefinitionId myDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_FlareDefinition), "ShipGrinder");
                            _LightEffectFlareGrinding = MyDefinitionManager.Static.GetDefinition(myDefinitionId) as MyFlareDefinition;
                        }

                        var flare = workingState == WorkingState.Welding ? _LightEffectFlareWelding : _LightEffectFlareGrinding;

                        if (flare != null)
                        {
                            _LightEffect = MyLights.AddLight();
                            _LightEffect.Start(Vector3.Zero, new Vector4(0.7f, 0.85f, 1f, 1f), 5f, string.Concat(system.Welder.DisplayNameText, " EffectLight"));
                            _LightEffect.Falloff = 2f;
                            _LightEffect.LightOn = true;
                            _LightEffect.GlareOn = true;
                            _LightEffect.GlareQuerySize = 0.8f;
                            _LightEffect.PointLightOffset = 0.1f;
                            _LightEffect.GlareType = VRageRender.Lights.MyGlareTypeEnum.Normal;
                            _LightEffect.SubGlares = flare.SubGlares;
                            _LightEffect.Intensity = flare.Intensity;
                            _LightEffect.GlareSize = flare.Size;
                        }
                    }
                    system.Welder.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveWorking", workingState == WorkingState.Welding ? Color.Yellow : Color.Blue, 1.0f);
                    break;

                case WorkingState.MissingComponents:
                    system.Welder.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveReady", Color.Red, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveWorking", Color.Yellow, 1.0f);
                    break;

                case WorkingState.InventoryFull:
                    system.Welder.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveReady", Color.Red, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveWorking", Color.Blue, 1.0f);
                    break;

                case WorkingState.NeedWelding:
                    system.Welder.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveWorking", Color.Yellow, 1.0f);
                    break;

                case WorkingState.NeedGrinding:
                    system.Welder.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveWorking", Color.Blue, 1.0f);
                    break;

                case WorkingState.Idle:
                    system.Welder.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveWorking", Color.Black, 1.0f);
                    break;

                case WorkingState.Invalid:
                case WorkingState.NotReady:
                    system.Welder.SetEmissiveParts("Emissive", Color.White, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveReady", Color.Black, 1.0f);
                    system.Welder.SetEmissiveParts("EmissiveWorking", Color.Black, 1.0f);
                    break;
            }

            var sound = _Sounds[(int)workingState];
            if (sound != null)
            {
                if (_SoundEmitter == null)
                {
                    _SoundEmitter = new MyEntity3DSoundEmitter((VRage.Game.Entity.MyEntity)system.Welder);
                    _SoundEmitter.CustomMaxDistance = 30f;
                    _SoundEmitter.CustomVolume = _SoundLevels[(int)workingState] * system.Settings.SoundVolume;
                }
                if (_SoundEmitterWorking == null)
                {
                    _SoundEmitterWorking = new MyEntity3DSoundEmitter((VRage.Game.Entity.MyEntity)system.Welder, true, 1f);
                    _SoundEmitterWorking.CustomMaxDistance = 30f;
                    _SoundEmitterWorking.CustomVolume = _SoundLevels[(int)workingState] * system.Settings.SoundVolume;
                    _SoundEmitterWorkingPosition = null;
                }

                if (_SoundEmitter != null)
                {
                    _SoundEmitter.StopSound(true);
                    _SoundEmitter.CustomVolume = _SoundLevels[(int)workingState] * system.Settings.SoundVolume;
                    _SoundEmitter.PlaySound(sound, true);
                }

                if (_SoundEmitterWorking != null)
                {
                    _SoundEmitterWorking.StopSound(true);
                    _SoundEmitterWorking.CustomVolume = _SoundLevels[(int)workingState] * system.Settings.SoundVolume;
                    _SoundEmitterWorking.SetPosition(null); //Reset
                    _SoundEmitterWorkingPosition = null;
                    //_SoundEmitterWorking.PlaySound(sound, true); done after position is set
                }
            }
            else
            {
                if (_SoundEmitter != null)
                {
                    _SoundEmitter.StopSound(true);
                }

                if (_SoundEmitterWorking != null)
                {
                    _SoundEmitterWorking.StopSound(true);
                    _SoundEmitterWorking.SetPosition(null); //Reset
                    _SoundEmitterWorkingPosition = null;
                }
            }
            UpdateWorkingEffectPosition(system, workingState);
        }

        /// <summary>
        /// Set the position of the visual and sound effects
        /// </summary>
        private void UpdateWorkingEffectPosition(NanobotSystem system, WorkingState workingState)
        {
            if (_ParticleEffectWorking1 == null && _SoundEmitterWorking == null) return;

            Vector3D position;
            MatrixD matrix;
            if (system.State.CurrentWeldingBlock != null)
            {
                BoundingBoxD box;
                system.State.CurrentWeldingBlock.GetWorldBoundingBox(out box, false);
                matrix = box.Matrix;
                position = matrix.Translation;
            }
            else if (system.State.CurrentGrindingBlock != null)
            {
                BoundingBoxD box;
                system.State.CurrentGrindingBlock.GetWorldBoundingBox(out box, false);
                matrix = box.Matrix;
                position = matrix.Translation;
            }
            else
            {
                matrix = system.Welder.WorldMatrix;
                position = matrix.Translation;
            }

            if (_LightEffect != null)
            {
                _LightEffect.Position = position;
                _LightEffect.Intensity = MyUtils.GetRandomFloat(0.1f, 0.6f);
                _LightEffect.UpdateLight();
            }

            if (_ParticleEffectWorking1 != null)
            {
                _ParticleEffectWorking1.WorldMatrix = matrix;
            }

            var sound = _Sounds[(int)workingState];
            if ((_SoundEmitterWorking != null) && (sound != null))
            {
                if (!_SoundEmitterWorking.IsPlaying || _SoundEmitterWorkingPosition == null || Math.Abs((_SoundEmitterWorkingPosition.Value - position).Length()) > 2)
                {
                    _SoundEmitterWorking.SetPosition(position);
                    _SoundEmitterWorkingPosition = position;
                    _SoundEmitterWorking.PlaySound(sound, true);
                }
            }
        }

        /// <summary>
        /// Start visual effects for transport
        /// </summary>
        private void SetTransportEffects(NanobotSystem system, bool active)
        {
            if ((Mod.Settings.Welder.AllowedEffects & VisualAndSoundEffects.TransportVisualEffect) != 0)
            {
                if (active)
                {
                    if (_ParticleEffectTransport1 != null)
                    {
                        Interlocked.Decrement(ref _ActiveTransportEffects);
                        _ParticleEffectTransport1.Stop();
                        _ParticleEffectTransport1 = null;
                    }

                    if (_ActiveTransportEffects < MaxTransportEffects)
                    {
                        MyParticlesManager.TryCreateParticleEffect(system.State.CurrentTransportIsPick ? PARTICLE_EFFECT_TRANSPORT1_PICK : PARTICLE_EFFECT_TRANSPORT1_DELIVER, ref MatrixD.Identity, ref Vector3D.Zero, uint.MaxValue, out _ParticleEffectTransport1);
                        if (_ParticleEffectTransport1 != null)
                        {
                            Interlocked.Increment(ref _ActiveTransportEffects);
                            _ParticleEffectTransport1.UserScale = 0.1f;
                            UpdateTransportEffectPosition(system);
                        }
                    }
                }
                else
                {
                    if (_ParticleEffectTransport1 != null)
                    {
                        Interlocked.Decrement(ref _ActiveTransportEffects);
                        _ParticleEffectTransport1.Stop();
                        _ParticleEffectTransport1 = null;
                    }
                }
            }
            _TransportStateSet = active;
        }

        /// <summary>
        /// Set the position of the visual effects for transport
        /// </summary>
        private void UpdateTransportEffectPosition(NanobotSystem system)
        {
            if (_ParticleEffectTransport1 == null) return;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var elapsed = system.State.CurrentTransportTime.Ticks != 0 ? (double)playTime.Subtract(system.State.CurrentTransportStartTime).Ticks / system.State.CurrentTransportTime.Ticks : 0d;
            elapsed = elapsed < 1 ? elapsed : 1;
            elapsed = (elapsed > 0.5 ? 1 - elapsed : elapsed) * 2;

            MatrixD startMatrix;
            var target = system.State.CurrentTransportTarget;
            startMatrix = system.Welder.WorldMatrix;
            startMatrix.Translation = Vector3D.Transform(EmitterPosition, system.Welder.WorldMatrix);

            var direction = target.Value - startMatrix.Translation;
            startMatrix.Translation += direction * elapsed;
            _ParticleEffectTransport1.WorldMatrix = startMatrix;
        }

        public void Close(NanobotSystem system)
        {
            if (system == null) return;

            StopSoundEffects();

            _SoundEmitter?.Cleanup();
            _SoundEmitter = null;

            _SoundEmitterWorking?.Cleanup();
            _SoundEmitterWorking = null;

            // Stop and dispose particle effects
            if (_ParticleEffectWorking1 != null)
            {
                _ParticleEffectWorking1.Stop();
                _ParticleEffectWorking1.Clear();
                _ParticleEffectWorking1 = null;
            }

            if (_ParticleEffectTransport1 != null)
            {
                _ParticleEffectTransport1.Stop();
                _ParticleEffectTransport1.Clear();
                _ParticleEffectTransport1 = null;
            }

            // Turn off light and remove reference
            if (_LightEffect != null)
            {
                _LightEffect.Clear();
                _LightEffect.LightOn = false;
                _LightEffect = null;
            }

            WorkingStateSet = WorkingState.Invalid;
        }
    }
}