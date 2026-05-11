using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Extensions;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Diagnostics;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

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

        // World-meters line thickness used by the cluster-area debug overlay. Kept
        // small and constant so the wireframe reads as a thin border at any working-
        // area size, instead of looking chunky on small OBBs.
        private const float ClusterAreaBorderThickness = 0.02f;

        // Vertical (local-Y) inflation applied to the gold coordinator marker so it
        // stands out from above and below the welder block — a 1×1×1 small-grid
        // block alone is too small to spot from working-area distances.
        private const double CoordinatorMarkerHeightExtra = 2.5;

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

                    if (HudHandler.LocalClusterAreaVisible)
                    {
                        DrawClusterAreaIfLead();
                    }

                    if (HudHandler.LocalTargetsVisible)
                    {
                        DrawMyTargets();
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

        /// <summary>
        /// Fixed palette for the cluster-area overlay. One colour per cluster, picked
        /// by hash modulo length so it's deterministic. ClusterPaletteNames is
        /// kept in lock-step so chat output can label each cluster by colour.
        /// </summary>
        public static readonly Color[] ClusterPalette = new[]
        {
            new Color((byte)255, (byte)215, (byte)0,   (byte)230),  // yellow
            new Color((byte)255, (byte)105, (byte)180, (byte)230),  // pink
            new Color((byte)60,  (byte)220, (byte)80,  (byte)230),  // green
            new Color((byte)180, (byte)80,  (byte)220, (byte)230),  // purple
            new Color((byte)0,   (byte)200, (byte)255, (byte)230),  // cyan
            new Color((byte)255, (byte)140, (byte)0,   (byte)230),  // orange
            new Color((byte)220, (byte)40,  (byte)40,  (byte)230),  // red
            new Color((byte)240, (byte)240, (byte)240, (byte)230),  // white
        };

        public static readonly string[] ClusterPaletteNames = new[]
        {
            "yellow", "pink", "green", "purple",
            "cyan", "orange", "red", "white",
        };

        /// <summary>
        /// Returns the human-readable colour name for the cluster identified by hash,
        /// matching the ClusterPalette index used in the cluster-area overlay.
        /// </summary>
        public static string GetClusterColorName(int hash)
        {
            var idx = (int)((uint)hash % (uint)ClusterPaletteNames.Length);
            return ClusterPaletteNames[idx];
        }

        /// <summary>
        /// Local-only debug visualization. Each multi-member cluster is drawn once
        /// by its "lead" BaR (the member with the lowest EntityId among those sharing
        /// the same scan-relevant cluster key on this client — same rule the server
        /// uses for coordinator election, so the lead IS the coordinator).
        ///
        /// For each cluster: one box per enabled member, sized to the welder block
        /// itself (no working-area OBB). All members share the same cluster colour.
        /// The coordinator's box is additionally extended upward by
        /// CoordinatorMarkerHeightExtra along the grid's up direction so it sticks
        /// out as a flag pole and is easy to find.
        ///
        /// All boxes render with BlendTypeEnum.PostPP so they show through other
        /// blocks — admin can see them from any vantage point on the ship.
        /// </summary>
        private void DrawClusterAreaIfLead()
        {
            if (!ScanClusterCoordinator.IsClusterEligible(this)) return;

            var myHash = ScanClusterCoordinator.ComputeClusterKeyHash(this);
            var myEid = _Welder.EntityId;

            // Lead check + member count in one pass.
            var memberCount = 1;
            foreach (var pair in Mod.NanobotSystems)
            {
                var other = pair.Value;
                if (other == this) continue;
                if (!ScanClusterCoordinator.IsClusterEligible(other)) continue;
                if (ScanClusterCoordinator.ComputeClusterKeyHash(other) != myHash) continue;
                if (pair.Key < myEid) return; // not lead — someone else will draw
                memberCount++;
            }

            if (memberCount < 2) return; // solo cluster — nothing useful to draw

            var color = ClusterColorFromHash(myHash);

            // Coordinator: block marker (with upward pillar) + working-area OBB.
            DrawBlockMarker(this, color, extendUpward: true);
            DrawWorkingAreaOBB(this, color);

            foreach (var pair in Mod.NanobotSystems)
            {
                var other = pair.Value;
                if (other == this) continue;
                if (!ScanClusterCoordinator.IsClusterEligible(other)) continue;
                if (ScanClusterCoordinator.ComputeClusterKeyHash(other) != myHash) continue;
                DrawBlockMarker(other, color, extendUpward: false);
                DrawWorkingAreaOBB(other, color);
            }
        }

        /// <summary>
        /// Draws the BaR's working-area OBB (oriented to the welder's WorldMatrix,
        /// shape from Settings.CorrectedAreaBoundingBox + Settings.CorrectedAreaOffset).
        /// This is the per-member scan-coverage volume — the actual region in which
        /// the BaR can weld/grind blocks. Depth-tested (no PostPP) so it doesn't
        /// clutter the view when many large OBBs overlap from the far side of the ship.
        /// </summary>
        private static void DrawWorkingAreaOBB(NanobotSystem system, Color color)
        {
            var welder = system.Welder;
            if (welder == null) return;

            var emitter = welder.WorldMatrix;
            emitter.Translation = Vector3D.Transform(system.Settings.CorrectedAreaOffset, emitter);
            var localBox = system.Settings.CorrectedAreaBoundingBox;
            MySimpleObjectDraw.DrawTransparentBox(
                ref emitter,
                ref localBox,
                ref color,
                MySimpleObjectRasterizer.Wireframe,
                1,
                ClusterAreaBorderThickness,
                RangeGridResourceId,
                null,
                false);
        }

        /// <summary>
        /// Draws a wireframe box around the welder block's full extents, oriented
        /// to the parent grid (so the marker's Y axis is grid-up, not welder-local
        /// Y). Block size is derived from SlimBlock.Min/Max so multi-cube blocks
        /// are outlined correctly. When extendUpward is true the top face is raised
        /// by CoordinatorMarkerHeightExtra so the coordinator reads as a tall pillar.
        /// The block's world centre comes from SlimBlock.ComputeWorldCenter, not the
        /// welder's WorldMatrix.Translation, so the marker doesn't get vertically
        /// offset on blocks whose model pivot isn't at the geometric centre.
        /// </summary>
        private static void DrawBlockMarker(NanobotSystem system, Color color, bool extendUpward)
        {
            var welder = system.Welder;
            if (welder == null || welder.CubeGrid == null || welder.SlimBlock == null) return;

            Vector3D blockCenter;
            welder.SlimBlock.ComputeWorldCenter(out blockCenter);

            var slim = welder.SlimBlock;
            var gridSize = (double)welder.CubeGrid.GridSize;
            // Slim block Min/Max are inclusive cube coordinates in grid-local axes.
            // Size in cubes = Max - Min + 1, world size = sizeInCubes × gridSize.
            var sizeInCubes = slim.Max - slim.Min + Vector3I.One;
            var halfX = sizeInCubes.X * gridSize * 0.5;
            var halfY = sizeInCubes.Y * gridSize * 0.5;
            var halfZ = sizeInCubes.Z * gridSize * 0.5;
            var topExtra = extendUpward ? CoordinatorMarkerHeightExtra : 0.0;

            var localBox = new BoundingBoxD(
                new Vector3D(-halfX, -halfY, -halfZ),
                new Vector3D(halfX, halfY + topExtra, halfZ));

            var matrix = welder.CubeGrid.WorldMatrix;
            matrix.Translation = blockCenter;

            MySimpleObjectDraw.DrawTransparentBox(
                ref matrix,
                ref localBox,
                ref color,
                ref color,
                MySimpleObjectRasterizer.Wireframe,
                new Vector3I(1, 1, 1),
                ClusterAreaBorderThickness,
                null,
                RangeGridResourceId,
                false,
                -1,
                BlendTypeEnum.PostPP);
        }

        /// <summary>
        /// Pick a colour for a cluster from the fixed 4-colour palette using the
        /// cluster's hash. Deterministic per cluster across frames.
        /// </summary>
        private static Color ClusterColorFromHash(int hash)
        {
            var idx = (int)((uint)hash % (uint)ClusterPalette.Length);
            return ClusterPalette[idx];
        }

        // Semi-transparent red fill used inside target outlines for blocks that
        // are currently assigned to some BaR. Unassigned targets get no fill —
        // they show only the cluster-coloured wireframe — so the eye can quickly
        // tell unclaimed (border only) from claimed (red + border).
        private static readonly Color TargetAssignedFillColor = new Color((byte)220, (byte)40, (byte)40, (byte)120);

        // Per-frame dedup for the target overlay: many BaRs can have the same block
        // in their PossibleWeldTargets / PossibleGrindTargets lists (the scanner
        // adds it to every BaR whose AABB sees the block), so without dedup the
        // same block ends up drawn N times per frame — both wasted draws and
        // alpha-stacking that makes the fill look near-opaque. Keyed by grid id +
        // local position (BlockSystemAssigningHandler.BlockKey), cleared on the
        // first call of each gameplay frame.
        private static readonly System.Collections.Generic.HashSet<Handlers.BlockSystemAssigningHandler.BlockKey> _drawnTargetsThisFrame
            = new System.Collections.Generic.HashSet<Handlers.BlockSystemAssigningHandler.BlockKey>();
        private static int _drawnTargetsFrame = -1;

        /// <summary>
        /// Local-only debug visualization: paint every block in this BaR's current
        /// weld / grind target list with a solid red fill and a cluster-colour
        /// wireframe border. Reads server-side State.Possible*Targets, so on a
        /// dedicated client (where those lists are empty) this draws nothing —
        /// the toggle is meaningful on listen-server / single-player sessions.
        /// </summary>
        private void DrawMyTargets()
        {
            if (!ScanClusterCoordinator.IsClusterEligible(this)) return;

            // Reset the per-frame dedup set on the first call this gameplay frame.
            // GameplayFrameCounter monotonically increases on the main thread; a
            // mismatch with our cached value means we crossed a frame boundary.
            var currentFrame = MyAPIGateway.Session.GameplayFrameCounter;
            if (_drawnTargetsFrame != currentFrame)
            {
                _drawnTargetsThisFrame.Clear();
                _drawnTargetsFrame = currentFrame;
            }

            var borderColor = ClusterColorFromHash(ScanClusterCoordinator.ComputeClusterKeyHash(this));

            // Target lists are refreshed every TargetsUpdateInterval (~10s), not per
            // weld/grind operation. Between scans they contain blocks that have
            // already been completed: welded blocks at full integrity, grinded
            // blocks already destroyed, blocks whose fat-block has closed. Filter
            // those out at draw time so the visualization tracks live work, not
            // historical entries from the last scan.
            lock (State.PossibleWeldTargets)
            {
                foreach (var target in State.PossibleWeldTargets)
                {
                    if (target == null || target.Block == null) continue;
                    if (!IsLiveWeldTarget(target.Block)) continue;
                    if (!ClaimTargetForDraw(target.Block)) continue;
                    DrawTargetOutline(target.Block, GetTargetFillColor(target.Block), borderColor);
                }
            }

            lock (State.PossibleGrindTargets)
            {
                foreach (var target in State.PossibleGrindTargets)
                {
                    if (target == null || target.Block == null) continue;
                    if (!IsLiveGrindTarget(target.Block)) continue;
                    if (!ClaimTargetForDraw(target.Block)) continue;
                    DrawTargetOutline(target.Block, GetTargetFillColor(target.Block), borderColor);
                }
            }
        }

        /// <summary>
        /// Fill colour for a target block: red when the block has been claimed via
        /// BlockSystemAssigningHandler, transparent (alpha = 0) when not. The
        /// transparent value tells DrawTargetOutline to skip the solid-fill pass
        /// entirely, so unassigned targets are drawn as wireframe-only outlines.
        /// </summary>
        private static Color GetTargetFillColor(IMySlimBlock block)
        {
            long systemId;
            return block.TryGetAssignedSystem(out systemId)
                ? TargetAssignedFillColor
                : default(Color);
        }

        /// <summary>
        /// Records that this block was drawn this frame. Returns true if this is the
        /// first BaR drawing the block this frame (caller should render), false if
        /// another BaR has already drawn it (caller should skip).
        /// </summary>
        private static bool ClaimTargetForDraw(IMySlimBlock block)
        {
            var key = new Handlers.BlockSystemAssigningHandler.BlockKey(block.CubeGrid.EntityId, block.Position);
            return _drawnTargetsThisFrame.Add(key);
        }

        /// <summary>
        /// A weld target is "live" when there is still welding work left on it:
        /// either it is a projection that hasn't been built yet, or it is a real
        /// block that has not reached full integrity. Blocks whose fat-block has
        /// closed (e.g., destroyed mid-cycle) are also filtered out.
        /// </summary>
        private static bool IsLiveWeldTarget(IMySlimBlock block)
        {
            if (block.CubeGrid == null) return false;
            if (block.FatBlock != null && block.FatBlock.Closed) return false;
            if (block.IsProjected()) return true; // projected, still needs to be built
            return !block.IsFullIntegrity;
        }

        /// <summary>
        /// A grind target is "live" until the block has been destroyed (integrity 0)
        /// or its fat-block has closed.
        /// </summary>
        private static bool IsLiveGrindTarget(IMySlimBlock block)
        {
            if (block.CubeGrid == null) return false;
            if (block.IsDestroyed) return false;
            if (block.FatBlock != null && block.FatBlock.Closed) return false;
            return true;
        }

        /// <summary>
        /// Draws a target block: solid red interior (depth-tested so it doesn't
        /// flood the view through walls) + cluster-colour wireframe border drawn
        /// with PostPP so the outline is always visible. Block size is taken from
        /// SlimBlock.Min/Max so multi-cube targets are outlined correctly; matrix
        /// uses the parent grid's WorldMatrix with translation set to the block's
        /// computed world centre, matching the BaR block-marker logic.
        /// </summary>
        private static void DrawTargetOutline(IMySlimBlock block, Color fillColor, Color borderColor)
        {
            var cubeGrid = block.CubeGrid;
            if (cubeGrid == null) return;

            Vector3D blockCenter;
            block.ComputeWorldCenter(out blockCenter);

            var gridSize = (double)cubeGrid.GridSize;
            var sizeInCubes = block.Max - block.Min + Vector3I.One;
            var halfX = sizeInCubes.X * gridSize * 0.5;
            var halfY = sizeInCubes.Y * gridSize * 0.5;
            var halfZ = sizeInCubes.Z * gridSize * 0.5;

            var localBox = new BoundingBoxD(
                new Vector3D(-halfX, -halfY, -halfZ),
                new Vector3D(halfX, halfY, halfZ));

            var matrix = cubeGrid.WorldMatrix;
            matrix.Translation = blockCenter;

            // Solid fill — only when there's something to fill. Unassigned targets
            // pass a transparent (alpha = 0) colour so this block is skipped,
            // leaving only the cluster-coloured wireframe. Drawn with
            // BlendTypeEnum.PostPP so the fill paints OVER the block's own mesh
            // instead of Z-fighting against it (the block's geometry would
            // otherwise occlude a depth-tested coincident solid box).
            if (fillColor.A > 0)
            {
                MySimpleObjectDraw.DrawTransparentBox(
                    ref matrix,
                    ref localBox,
                    ref fillColor,
                    ref fillColor,
                    MySimpleObjectRasterizer.Solid,
                    new Vector3I(1, 1, 1),
                    0f,
                    RangeGridResourceId,
                    null,
                    false,
                    -1,
                    BlendTypeEnum.PostPP);
            }

            // Cluster-colour wireframe border — PostPP so it shows through walls
            // and the admin can locate targeted blocks from any angle.
            MySimpleObjectDraw.DrawTransparentBox(
                ref matrix,
                ref localBox,
                ref borderColor,
                ref borderColor,
                MySimpleObjectRasterizer.Wireframe,
                new Vector3I(1, 1, 1),
                ClusterAreaBorderThickness,
                null,
                RangeGridResourceId,
                false,
                -1,
                BlendTypeEnum.PostPP);
        }
    }
}
