using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Lights;
using Sandbox.ModAPI;
using System;
using System.Threading;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using static SKONanobotBuildAndRepairSystem.NanobotBuildAndRepairSystemBlock;

namespace SKONanobotBuildAndRepairSystem
{
    public static class EffectManager
    {
        private static readonly int MaxTransportEffects = 16;
        private static int _ActiveTransportEffects = 0;
        private static readonly int MaxWorkingEffects = 16;
        private static int _ActiveWorkingEffects = 0;
        internal static readonly MySoundPair[] _Sounds = new[] { null, null, null, new MySoundPair("ToolLrgWeldMetal"), new MySoundPair("BlockModuleProductivity"), new MySoundPair("BaRUnable"), new MySoundPair("ToolLrgGrindMetal"), new MySoundPair("BlockModuleProductivity"), new MySoundPair("BaRUnable"), new MySoundPair("BaRUnable") };
        private static readonly float[] _SoundLevels = new[] { 0f, 0f, 0f, 1f, 0.5f, 0.4f, 1f, 0.5f, 0.4f, 0.4f };

        public static void UpdateEffects(NanobotBuildAndRepairSystemBlock block)
        {
            var transportState = block.State.Transporting && block.State.CurrentTransportTarget != null;
            if (transportState != block._TransportStateSet)
            {
                SetTransportEffects(block, transportState);
            }
            else
            {
                UpdateTransportEffectPosition(block);
            }

            //Welding/Grinding state
            var workingState = block.GetWorkingState();
            if (workingState != block._WorkingStateSet || block.Settings.SoundVolume != block._SoundVolumeSet)
            {
                SetWorkingEffects(block, workingState);
                block._WorkingStateSet = workingState;
                block._SoundVolumeSet = block.Settings.SoundVolume;
            }
            else
            {
                UpdateWorkingEffectPosition(block, workingState);
            }
        }

        /// <summary>
        /// Start visual effects for transport
        /// </summary>
        private static void SetTransportEffects(NanobotBuildAndRepairSystemBlock block, bool active)
        {
            if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedEffects & VisualAndSoundEffects.TransportVisualEffect) != 0)
            {
                if (active)
                {
                    if (block._ParticleEffectTransport1 != null)
                    {
                        Interlocked.Decrement(ref _ActiveTransportEffects);
                        block._ParticleEffectTransport1.Stop();
                        block._ParticleEffectTransport1 = null;
                    }

                    if (_ActiveTransportEffects < MaxTransportEffects)
                    {
                        MyParticlesManager.TryCreateParticleEffect(block.State.CurrentTransportIsPick ? Constants.PARTICLE_EFFECT_TRANSPORT1_PICK : Constants.PARTICLE_EFFECT_TRANSPORT1_DELIVER, ref MatrixD.Identity, ref Vector3D.Zero, uint.MaxValue, out block._ParticleEffectTransport1);
                        if (block._ParticleEffectTransport1 != null)
                        {
                            Interlocked.Increment(ref _ActiveTransportEffects);
                            block._ParticleEffectTransport1.UserScale = 0.1f;
                            UpdateTransportEffectPosition(block);
                        }
                    }
                }
                else
                {
                    if (block._ParticleEffectTransport1 != null)
                    {
                        Interlocked.Decrement(ref _ActiveTransportEffects);
                        block._ParticleEffectTransport1.Stop();
                        block._ParticleEffectTransport1 = null;
                    }
                }
            }
            block._TransportStateSet = active;
        }

        /// <summary>
        /// Set the position of the visual effects for transport
        /// </summary>
        private static void UpdateTransportEffectPosition(NanobotBuildAndRepairSystemBlock block)
        {
            if (block._ParticleEffectTransport1 == null) return;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var elapsed = block.State.CurrentTransportTime.Ticks != 0 ? (double)playTime.Subtract(block.State.CurrentTransportStartTime).Ticks / block.State.CurrentTransportTime.Ticks : 0d;
            elapsed = elapsed < 1 ? elapsed : 1;
            elapsed = (elapsed > 0.5 ? 1 - elapsed : elapsed) * 2;

            MatrixD startMatrix;
            var target = block.State.CurrentTransportTarget;
            startMatrix = block._Welder.WorldMatrix;
            startMatrix.Translation = Vector3D.Transform(block._EmitterPosition, block._Welder.WorldMatrix);

            var direction = target.Value - startMatrix.Translation;
            startMatrix.Translation += direction * elapsed;
            block._ParticleEffectTransport1.WorldMatrix = startMatrix;
        }

        /// <summary>
        /// Start visual effects for welding/grinding
        /// </summary>
        private static void SetWorkingEffects(NanobotBuildAndRepairSystemBlock block, WorkingState workingState)
        {
            if (block._ParticleEffectWorking1 != null)
            {
                Interlocked.Decrement(ref _ActiveWorkingEffects);
                block._ParticleEffectWorking1.Stop();
                block._ParticleEffectWorking1 = null;
            }

            if (block._LightEffect != null)
            {
                MyLights.RemoveLight(block._LightEffect);
                block._LightEffect = null;
            }

            switch (workingState)
            {
                case WorkingState.Welding:
                case WorkingState.Grinding:
                    if (_ActiveWorkingEffects < MaxWorkingEffects &&
                        ((workingState == WorkingState.Welding && (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedEffects & VisualAndSoundEffects.WeldingVisualEffect) != 0) ||
                         (workingState == WorkingState.Grinding && (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedEffects & VisualAndSoundEffects.GrindingVisualEffect) != 0)))
                    {
                        Interlocked.Increment(ref _ActiveWorkingEffects);

                        MyParticlesManager.TryCreateParticleEffect(workingState == WorkingState.Welding ? Constants.PARTICLE_EFFECT_WELDING1 : Constants.PARTICLE_EFFECT_GRINDING1, ref MatrixD.Identity, ref Vector3D.Zero, uint.MaxValue, out block._ParticleEffectWorking1);
                        if (block._ParticleEffectWorking1 != null) block._ParticleEffectWorking1.UserRadiusMultiplier = workingState == WorkingState.Welding ? 4f : 2f;// 0.5f;

                        if (workingState == WorkingState.Welding && block._LightEffectFlareWelding == null)
                        {
                            var myDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_FlareDefinition), "ShipWelder");
                            block._LightEffectFlareWelding = MyDefinitionManager.Static.GetDefinition(myDefinitionId) as MyFlareDefinition;
                        }
                        else if (workingState == WorkingState.Grinding && block._LightEffectFlareGrinding == null)
                        {
                            var myDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_FlareDefinition), "ShipGrinder");
                            block._LightEffectFlareGrinding = MyDefinitionManager.Static.GetDefinition(myDefinitionId) as MyFlareDefinition;
                        }

                        var flare = workingState == WorkingState.Welding ? block._LightEffectFlareWelding : block._LightEffectFlareGrinding;

                        if (flare != null)
                        {
                            block._LightEffect = MyLights.AddLight();
                            block._LightEffect.Start(Vector3.Zero, new Vector4(0.7f, 0.85f, 1f, 1f), 5f, string.Concat(block._Welder.DisplayNameText, " EffectLight"));
                            block._LightEffect.Falloff = 2f;
                            block._LightEffect.LightOn = true;
                            block._LightEffect.GlareOn = true;
                            block._LightEffect.GlareQuerySize = 0.8f;
                            block._LightEffect.PointLightOffset = 0.1f;
                            block._LightEffect.GlareType = VRageRender.Lights.MyGlareTypeEnum.Normal;
                            block._LightEffect.SubGlares = flare.SubGlares;
                            block._LightEffect.Intensity = flare.Intensity;
                            block._LightEffect.GlareSize = flare.Size;
                        }
                    }
                    block._Welder.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveWorking", workingState == WorkingState.Welding ? Color.Yellow : Color.Blue, 1.0f);
                    break;

                case WorkingState.MissingComponents:
                    block._Welder.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveReady", Color.Red, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveWorking", Color.Yellow, 1.0f);
                    break;

                case WorkingState.InventoryFull:
                    block._Welder.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveReady", Color.Red, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveWorking", Color.Blue, 1.0f);
                    break;

                case WorkingState.NeedWelding:
                    block._Welder.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveWorking", Color.Yellow, 1.0f);
                    break;

                case WorkingState.NeedGrinding:
                    block._Welder.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveWorking", Color.Blue, 1.0f);
                    break;

                case WorkingState.Idle:
                    block._Welder.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveWorking", Color.Black, 1.0f);
                    break;

                case WorkingState.Invalid:
                case WorkingState.NotReady:
                    block._Welder.SetEmissiveParts("Emissive", Color.White, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveReady", Color.Black, 1.0f);
                    block._Welder.SetEmissiveParts("EmissiveWorking", Color.Black, 1.0f);
                    break;
            }

            var sound = _Sounds[(int)workingState];
            if (sound != null)
            {
                if (block._SoundEmitter == null)
                {
                    block._SoundEmitter = new MyEntity3DSoundEmitter((VRage.Game.Entity.MyEntity)block._Welder);
                    block._SoundEmitter.CustomMaxDistance = 30f;
                    block._SoundEmitter.CustomVolume = _SoundLevels[(int)workingState] * block.Settings.SoundVolume;
                }
                if (block._SoundEmitterWorking == null)
                {
                    block._SoundEmitterWorking = new MyEntity3DSoundEmitter((VRage.Game.Entity.MyEntity)block._Welder, true, 1f);
                    block._SoundEmitterWorking.CustomMaxDistance = 30f;
                    block._SoundEmitterWorking.CustomVolume = _SoundLevels[(int)workingState] * block.Settings.SoundVolume;
                    block._SoundEmitterWorkingPosition = null;
                }

                if (block._SoundEmitter != null)
                {
                    block._SoundEmitter.StopSound(true);
                    block._SoundEmitter.CustomVolume = _SoundLevels[(int)workingState] * block.Settings.SoundVolume;
                    block._SoundEmitter.PlaySound(sound, true);
                }

                if (block._SoundEmitterWorking != null)
                {
                    block._SoundEmitterWorking.StopSound(true);
                    block._SoundEmitterWorking.CustomVolume = _SoundLevels[(int)workingState] * block.Settings.SoundVolume;
                    block._SoundEmitterWorking.SetPosition(null); //Reset
                    block._SoundEmitterWorkingPosition = null;
                    //_SoundEmitterWorking.PlaySound(sound, true); done after position is set
                }
            }
            else
            {
                block._SoundEmitter?.StopSound(true);

                if (block._SoundEmitterWorking != null)
                {
                    block._SoundEmitterWorking.StopSound(true);
                    block._SoundEmitterWorking.SetPosition(null); //Reset
                    block._SoundEmitterWorkingPosition = null;
                }
            }

            UpdateWorkingEffectPosition(block, workingState);
        }

        /// <summary>
        /// Set the position of the visual and sound effects
        /// </summary>
        private static void UpdateWorkingEffectPosition(NanobotBuildAndRepairSystemBlock block, WorkingState workingState)
        {
            if (block._ParticleEffectWorking1 == null && block._SoundEmitterWorking == null) return;

            Vector3D position;
            MatrixD matrix;
            if (block.State.CurrentWeldingBlock != null)
            {
                BoundingBoxD box;
                block.State.CurrentWeldingBlock.GetWorldBoundingBox(out box, false);
                matrix = box.Matrix;
                position = matrix.Translation;
            }
            else if (block.State.CurrentGrindingBlock != null)
            {
                BoundingBoxD box;
                block.State.CurrentGrindingBlock.GetWorldBoundingBox(out box, false);
                matrix = box.Matrix;
                position = matrix.Translation;
            }
            else
            {
                matrix = block._Welder.WorldMatrix;
                position = matrix.Translation;
            }

            if (block._LightEffect != null)
            {
                block._LightEffect.Position = position;
                block._LightEffect.Intensity = MyUtils.GetRandomFloat(0.1f, 0.6f);
                block._LightEffect.UpdateLight();
            }

            if (block._ParticleEffectWorking1 != null)
            {
                block._ParticleEffectWorking1.WorldMatrix = matrix;
            }

            var sound = _Sounds[(int)workingState];
            if (block._SoundEmitterWorking != null && sound != null)
            {
                if (!block._SoundEmitterWorking.IsPlaying || block._SoundEmitterWorkingPosition == null || Math.Abs((block._SoundEmitterWorkingPosition.Value - position).Length()) > 2)
                {
                    block._SoundEmitterWorking.SetPosition(position);
                    block._SoundEmitterWorkingPosition = position;
                    block._SoundEmitterWorking.PlaySound(sound, true);
                }
            }
        }

        public static void StopSoundEffects(NanobotBuildAndRepairSystemBlock block)
        {
            block._SoundEmitter?.StopSound(false);

            if (block._SoundEmitterWorking != null)
            {
                block._SoundEmitterWorking.StopSound(false);
                block._SoundEmitterWorking.SetPosition(null); //Reset
                block._SoundEmitterWorkingPosition = null;
            }
        }

        public static void Unregister(NanobotBuildAndRepairSystemBlock block)
        {
            if (block == null)
                return;

            // Stop and dispose sound effects
            StopSoundEffects(block);
            block._SoundEmitter?.Cleanup();
            block._SoundEmitter = null;

            block._SoundEmitterWorking?.Cleanup();
            block._SoundEmitterWorking = null;

            // Stop and dispose particle effects
            if (block._ParticleEffectWorking1 != null)
            {
                block._ParticleEffectWorking1.Stop();
                block._ParticleEffectWorking1 = null;
            }

            if (block._ParticleEffectTransport1 != null)
            {
                block._ParticleEffectTransport1.Stop();
                block._ParticleEffectTransport1 = null;
            }

            // Turn off light and remove reference
            if (block._LightEffect != null)
            {
                block._LightEffect.LightOn = false;
                block._LightEffect = null;
            }
        }
    }
}
