using System;
using System.Collections.Generic;
using System.Text;
using ShapeProjector.Geometry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ShapeProjector
{
    /// <summary>
    /// One contiguous run of <see cref="BEShapeProjector.PreviewCells"/> belonging to one Params layer
    /// (spec §10c): the preview pane colors and highlights per layer without ever recomputing geometry.
    /// LayerIndex is the index into Params.Layers (disabled layers have no segment); ColorIndex is the
    /// layer's GhostPalette index, so the preview derives its bright/dim line colors from the palette
    /// instead of unpacking the world renderer's packed RGBA.
    /// </summary>
    public readonly record struct PreviewSegment(int LayerIndex, int CellStart, int Count, int ColorIndex);

    /// <summary>
    /// Block entity: the projector's parameter store (spec §2, §8). The server owns the parameters;
    /// clients receive them through the normal block-entity sync (api-notes.md §c.4), derive the
    /// outline geometry with the Geometer's library (spec §5) and hand ghost cells to the renderer.
    /// All layers share the centre offset (spec §4) and go into ONE mesh (spec §6).
    /// Lifecycle/renderer pattern copied from vanilla BlockEntityResonator — api-notes §d.1.
    /// Base type: Vintagestory.API.Common.BlockEntity — api-notes §c.3.
    /// </summary>
    public class BEShapeProjector : BlockEntity
    {
        /// <summary>
        /// §10c hologram ↔ dialog contract: the layer index selected in the OPEN config dialog, or
        /// null while no dialog is open on this BE. The dialog writes it (every recompose + on close);
        /// the hologram renderer reads it for selected-layer brightening and for holoMode "guiOpen".
        /// Client-side only, never persisted or synced.
        /// </summary>
        public int? GuiSelectedLayer;

        /// <summary>Client→server "apply parameters" packet; payload = TreeAttribute bytes (api-notes §h.4).</summary>
        public const int PacketApply = 1001;

        public ProjectorParams Params { get; private set; } = ProjectorParams.Default();
        public ProjectorConfig Config { get; private set; } = new ProjectorConfig();

        private ShapeProjectorModSystem modSystem = null!;   // set in Initialize (both sides)

        private ProjectorRenderer? renderer;
        private HologramRenderer? holoRenderer;
        private GuiDialogProjector? dialog;
        private readonly List<LayerGeometry> geometries = new List<LayerGeometry>();
        private string lastRenderKey = "";

        // ---- Drape state (spec §5a, step 5). Client-only; touched exclusively from event handlers and
        // tick listeners, never from the render loop (the Renderer's rule: no geometry per frame).
        private sealed class LayerRender
        {
            /// <summary>Index into Params.Layers (spec §10c: lets the preview highlight the selected layer).</summary>
            public int LayerIndex;
            public LayerGeometry Geom = null!;
            public VerticalMode Mode;
            public bool FluidSurface;
            public int YOffset;
            public int Color;
            /// <summary>
            /// Vertical extent in blocks (LayerParams.Height, user request 2026-09-02); 1 is the flat outline.
            /// </summary>
            public int Height = 1;
            /// <summary>
            /// cells[CellStart + i*Height + k] is the cube for Geom.Positions[i] at level k, k in [0, Height)
            /// going UP from that column's resolved Y. Positions order is stable (LayerGeometry.cs:19), so
            /// the index arithmetic is the only thing that has to know about Height.
            /// </summary>
            public int CellStart;
            /// <summary>
            /// How many of Geom.Positions actually got cells. Equal to Geom.Positions.Count except when the
            /// cell budget truncated this layer; the live-patch path must not index past it.
            /// </summary>
            public int Columns;
            /// <summary>Done tint active for this layer (spec §6, step 6): Fixed-Y only, config + per-layer gated.</summary>
            public bool Feedback;
        }

        private readonly List<LayerRender> layerRenders = new List<LayerRender>();
        private readonly List<GhostCell> cells = new List<GhostCell>();
        private readonly List<PreviewSegment> previewSegments = new List<PreviewSegment>();

        // ---- §10c preview-pane contract (client only). The preview consumes THE SAME cached cells the
        // world renderer consumes — SetCells and this surface are fed from the same list in the same
        // call sites, so the preview cannot disagree with the world (spec §10c "Data source").
        /// <summary>The world renderer's cell list, block-local to the projector. Live view; main thread only.</summary>
        public IReadOnlyList<GhostCell> PreviewCells => cells;
        /// <summary>Per-layer runs inside <see cref="PreviewCells"/> (enabled layers only).</summary>
        public IReadOnlyList<PreviewSegment> PreviewSegments => previewSegments;
        /// <summary>Centre-marker state exactly as handed to the world renderer (block-local).</summary>
        public bool PreviewMarkerVisible { get; private set; }
        public double PreviewMarkerX { get; private set; }
        public double PreviewMarkerY { get; private set; }
        public double PreviewMarkerZ { get; private set; }
        /// <summary>Raised right after the world renderer received new/patched cells — the §10c
        /// "the recompute is already event-driven; the preview just subscribes" hook.</summary>
        public event Action? PreviewCellsChanged;
        private readonly HashSet<Geometry.BlockXZ> dirtyColumns = new HashSet<Geometry.BlockXZ>();
        // System.Func fully qualified: Vintagestory.API.Common declares its own Func<T1,T2,TResult>
        // (DECOMP/api/Vintagestory.API.Common/Func.cs), ambiguous under these usings.
        private System.Func<int, int, int?>? surfaceHeightDel;   // cached delegate: zero allocation per column/rebuild
        private BlockPos? scratchPos;                            // reused for fluid-layer lookups (no per-column allocation)
        private bool resolveFluidAsSurface;               // per-layer flag read by SurfaceHeightAt
        private bool anyDrapeLayer;
        private bool anyFeedbackLayer;
        private bool blockChangedSubscribed;
        private bool patchQueued;
        private long resampleListenerId;

        // public virtual void Initialize(ICoreAPI api) — api-notes §c.3 (BlockEntity.cs:132).
        // "called right after the block entity was spawned or right after it was loaded from a
        // newly loaded chunk ... You should still call the base method" (doc 127-130).
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            // T GetModSystem<T>(bool withInheritance = true) — IModLoader.cs:39, via ICoreAPI.ModLoader (ICoreAPI.cs:56);
            // same access pattern as RiftRenderer.cs:47 (api-notes §d.2).
            modSystem = api.ModLoader.GetModSystem<ShapeProjectorModSystem>();
            Config = modSystem.Config;

            // Client side only: ICoreClientAPI is the client flavour of ICoreAPI (api-notes §d.1).
            if (api is ICoreClientAPI capi)
            {
                // this.Pos: public BlockPos Pos — api-notes §c.3 (BlockEntity.cs:91).
                renderer = new ProjectorRenderer(capi, Pos, modSystem);

                // void RegisterRenderer(IRenderer renderer, EnumRenderStage renderStage, string profilingName = null)
                // — api-notes §d.1 (IClientEventAPI.cs:189). Stage OIT because that is where the
                // engine draws its own translucent block highlights — api-notes §d.2
                // (SystemHighlightBlocks.cs:20).
                capi.Event.RegisterRenderer(renderer, EnumRenderStage.OIT, "shapeprojector");

                // Second registration, same instance, for the see-through reveal at AfterBlit
                // (spec §6 seeThroughDepth; api-notes §f.4 step 1; stage precedent RiftRenderer.cs:41).
                // The renderer branches on the stage argument; Dispose unregisters both.
                capi.Event.RegisterRenderer(renderer, EnumRenderStage.AfterBlit, "shapeprojector-seethrough");

                // §10c hologram (amended spec): a third registration, its own IRenderer instance at
                // OIT, consuming the SAME cached cells via PreviewCells/PreviewSegments +
                // PreviewCellsChanged (it subscribes in its ctor — BEFORE the RebuildGeometry below,
                // so it receives the initial cell set). holoMode "off" skips construction entirely:
                // zero per-frame cost. Same stage/citation as the primary registration (api-notes
                // §d.1 IClientEventAPI.cs:189; §d.2 SystemHighlightBlocks.cs:20).
                if (modSystem.Config.holoMode != "off")
                {
                    holoRenderer = new HologramRenderer(capi, Pos, modSystem, this);
                    capi.Event.RegisterRenderer(holoRenderer, EnumRenderStage.OIT, "shapeprojector-holo");
                }

                surfaceHeightDel = SurfaceHeightAt;

                // event BlockChangedDelegate BlockChanged — api-notes §g.1 (IClientEventAPI.cs:69);
                // delegate (BlockPos pos, Block oldBlock), oldBlock may be null. Coverage (§g.2): local
                // place/break, server single + bulk solid-layer SetBlocks, ExchangeBlock — but NOT
                // fluid-only changes (and SetBlocksMinimal is unverified); those are caught by the
                // 2 s re-sample tick below. Unsubscribed in OnBlockRemoved/OnBlockUnloaded.
                capi.Event.BlockChanged += OnClientBlockChanged;
                blockChangedSubscribed = true;

                RebuildGeometry(force: true);
            }
            else
            {
                // Server: make the -off/-on variant agree with the stored parameters on load/placement (spec §7).
                SyncEmissiveVariant();
            }
        }

        // public virtual void ToTreeAttributes(ITreeAttribute tree) — api-notes §c.3 (BlockEntity.cs:387):
        // "Called when saving the world or when sending the block entity data to the client"; call base.
        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            Params.ToTree(tree);
        }

        // public virtual void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        // — api-notes §c.3 (BlockEntity.cs:417): "always called before Initialize() ... so the this.api
        // field is not yet set!" On the client it is ALSO the hook that fires when the server resyncs
        // the BE after MarkDirty — api-notes §c.4 (ClientChunk.cs:322-345).
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            Params = ProjectorParams.FromTree(tree);

            // Api is null before Initialize (doc above); after that, this is a live update.
            if (Api != null && renderer != null)
            {
                RebuildGeometry(force: false);
                dialog?.RefreshFrom(Params);
            }
        }

        // public virtual void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
        // — api-notes §c.3 (BlockEntity.cs:436). Runs on the server. Pattern: BlockEntityTicker.cs:156-175
        // and BlockEntitySign.cs:132-160 (api-notes §h.3, §h.5).
        public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
        {
            base.OnReceivedClientPacket(fromPlayer, packetid, data);
            if (packetid != PacketApply || data == null) return;

            // Reach + claim check, the same two calls vanilla's BlockEntity.CachedAccessPerms makes
            // (BlockEntity.cs:48 player.IsInInteractionRangeOf(pos); BlockEntity.cs:61 world.Claims.TryAccess(player, pos, flags);
            // audit lines at BlockEntity.cs:51,67) — api-notes §h.5. The CachedAccessPerms wrapper itself is
            // marked [Obsolete("This signature will change in 1.23")] by the 1.22.7 compiler, so it is not used.
            // bool TryAccess(IPlayer, BlockPos, EnumBlockAccessFlags) — ILandClaimAPI.cs:29 (also sends the player an error message);
            // EnumBlockAccessFlags.Use = 2 (EnumBlockAccessFlags.cs). Spec §2: vanilla claims govern edits.
            if (!IsWithinReach(fromPlayer))
            {
                Api.World.Logger.Audit("Player {0} sent a packet to shapeprojector at {1} but is too far away. Rejected.", fromPlayer.PlayerName, Pos);
                return;
            }
            if (!Api.World.Claims.TryAccess(fromPlayer, Pos, EnumBlockAccessFlags.Use))
            {
                Api.World.Logger.Audit("Player {0} sent a packet to shapeprojector at {1} but has no claim access. Rejected.", fromPlayer.PlayerName, Pos);
                return;   // client keeps the old state; TryAccess already told the player
            }

            ProjectorParams incoming;
            try
            {
                // TreeAttribute.CreateFromBytes(byte[]) — api-notes §c.5 (TreeAttribute.cs:84).
                incoming = ProjectorParams.FromTree(TreeAttribute.CreateFromBytes(data));
            }
            catch (Exception e)
            {
                Api.Logger.Warning("[shapeprojector] Bad apply packet from {0} at {1}: {2}", fromPlayer.PlayerName, Pos, e.Message);
                return;
            }

            // Server is authoritative: clamp to the config caps (spec §7) before storing.
            incoming.Clamp(Config);
            if (incoming.Layers.Count == 0)
            {
                LayerParams l = new LayerParams();
                l.ApplyShapeDefaults();
                incoming.Layers.Add(l);
            }
            Params = incoming;

            SyncEmissiveVariant();

            // MarkDirty(redrawOnClient: true) — "When called on Server: Will resync the block entity with all
            // its TreeAttribute to the client" — api-notes §c.3 (BlockEntity.cs:458-470).
            MarkDirty(true);
        }

        /// <summary>
        /// Server: swaps between shapeprojector:projector-off and projector-on so the emissive ring shows
        /// when ≥1 layer is enabled (spec §7; api-notes §j Option A). ExchangeBlock keeps the block entity:
        /// "without calling OnBlockRemoved or OnBlockPlaced, which prevents any block entity from being removed
        /// or placed" (IBlockAccessor.cs:262-266); the engine then calls BlockEntity.OnExchanged, which updates
        /// this.Block and marks it dirty (BlockAccessorRelaxed.cs:97-101; BlockEntity.cs:314-321), and sends the
        /// ExchangeBlock packet to clients (BlockAccessorRelaxed.cs:102-103 → GeneralPacketHandler.cs:106-113).
        /// Both variants share the same entityClass (assets/shapeprojector/blocktypes/projector.json).
        /// </summary>
        private void SyncEmissiveVariant()
        {
            if (Api == null || Api.Side != EnumAppSide.Server || Block == null) return;

            // RegistryObject.Variant: "Will not throw ... but return null instead" when the key is missing (RegistryObject.cs:25-27).
            string? current = Block.Variant["state"];
            if (current == null) return;   // not the variant-bearing blocktype (should not happen)

            string wanted = Params.EnabledLayerCount() > 0 ? "on" : "off";
            if (current == wanted) return;

            // AssetLocation CodeWithVariant(string type, string value) — RegistryObject.cs:121;
            // Block GetBlock(AssetLocation code) — IBlockAccessor.cs:297 (vanilla pattern BlockCokeOvenDoor.cs:21).
            Block? target = Api.World.BlockAccessor.GetBlock(Block.CodeWithVariant("state", wanted));
            if (target == null)
            {
                Api.Logger.Warning("[shapeprojector] Variant projector-{0} not found; emissive swap skipped at {1}", wanted, Pos);
                return;
            }

            // void ExchangeBlock(int blockId, BlockPos pos) — IBlockAccessor.cs:266; Block.BlockId — Block.cs:43.
            Api.World.BlockAccessor.ExchangeBlock(target.BlockId, Pos);
        }

        /// <summary>
        /// Server-side reach test. IPlayer.IsInInteractionRangeOf(BlockPos, float) is `internal`
        /// (IPlayer.cs:100) so mods cannot call it; this replicates the engine's
        /// ServerPlayer.isInInteractionRangeOf(IPlayer, IBlockAccessor, BlockPos, float) (ServerPlayer.cs:309-330):
        /// same dimension, eye position = Entity.Pos + Entity.LocalEyePos (ServerPlayer.cs:316),
        /// range = WorldData.PickingRange + slack (IWorldPlayerData.cs:62; ServerPlayer.cs:318).
        /// Simplification: the engine measures to the block's selection boxes; here the distance to
        /// the block centre is used with half a block diagonal (0.87) of extra slack, which is never
        /// stricter than the engine's test.
        /// </summary>
        private bool IsWithinReach(IPlayer player, float slack = 0.25f)
        {
            // EntityPos.Dimension vs BlockPos.dimension — ServerPlayer.cs:311; BlockPos.dimension is a public field (BlockPos.cs:29).
            if (player.Entity.Pos.Dimension != Pos.dimension) return false;

            double ex = player.Entity.Pos.X + player.Entity.LocalEyePos.X;
            double ey = player.Entity.Pos.Y + player.Entity.LocalEyePos.Y;
            double ez = player.Entity.Pos.Z + player.Entity.LocalEyePos.Z;
            double dx = ex - (Pos.X + 0.5), dy = ey - (Pos.Y + 0.5), dz = ez - (Pos.Z + 0.5);
            double range = player.WorldData.PickingRange + slack + 0.87;
            return dx * dx + dy * dy + dz * dz <= range * range;
        }

        /// <summary>Attribute key under which a dropped/picked projector item carries its configuration (api-notes §i).</summary>
        public const string StackConfigKey = "projectorcfg";

        /// <summary>The full parameter tree for a config-carrying item stack (Block.OnPickBlock / GetDrops).</summary>
        public ITreeAttribute BuildConfigTree()
        {
            TreeAttribute tree = new TreeAttribute();   // TreeAttribute — TreeAttribute.cs:19 (api-notes §c.5)
            Params.ToTree(tree);
            return tree;
        }

        // public virtual void OnBlockPlaced(ItemStack byItemStack = null) — api-notes §i.1: "Always called
        // after Initialize()" (BlockEntity.cs:373-375); invoked with the placing stack by
        // ServerWorldMap.SpawnBlockEntity (ServerWorldMap.cs:471-489; client mirror ClientWorldMap.cs:971-986).
        // Restore pattern: BEBehaviorShapeFromAttributes.OnBlockPlaced reads byItemStack.Attributes
        // (BEBehaviorShapeFromAttributes.cs:137-145; api-notes §i.2). The round-trip covers everything
        // LayerParams.ToTree writes: all layers with shape parameters, Y offset, vertical mode, fluid rule,
        // build feedback, colour, enabled — plus the centre offset (ProjectorParams.ToTree).
        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            // ITreeAttribute.GetTreeAttribute returns null when absent (ITreeAttribute.cs:242, api-notes §i.1).
            ITreeAttribute? cfg = byItemStack?.Attributes?.GetTreeAttribute(StackConfigKey);
            if (cfg == null) return;

            ProjectorParams restored = ProjectorParams.FromTree(cfg);
            // Same validation as any edit packet — a foreign/oversized stack tree loads clamped (spec §4a rule).
            restored.Clamp(Config);
            if (restored.Layers.Count == 0) return;
            Params = restored;

            if (Api?.Side == EnumAppSide.Server)
            {
                SyncEmissiveVariant();
                // Resync the restored tree to all clients (BlockEntity.cs:458-470, api-notes §c.3).
                MarkDirty(true);
            }
            else if (renderer != null)
            {
                RebuildGeometry(force: true);
            }
        }

        /// <summary>Client: sends the edited parameters to the server (api-notes §h.4).</summary>
        public void SendApply(ProjectorParams edited)
        {
            if (Api is not ICoreClientAPI capi) return;

            // TreeAttribute (Vintagestory.API.Datastructures) — TreeAttribute.cs:19; ToBytes() — TreeAttribute.cs:115 (api-notes §c.5).
            TreeAttribute tree = new TreeAttribute();
            edited.ToTree(tree);

            // void SendBlockEntityPacket(BlockPos pos, int packetId, byte[] data = null) — api-notes §c.5 / §h.4
            // (IClientNetworkAPI.cs:66). Arrives in OnReceivedClientPacket above.
            capi.Network.SendBlockEntityPacket(Pos, PacketApply, tree.ToBytes());
        }

        /// <summary>Client: toggles the configuration dialog. Pattern: BlockEntityTicker.OnInteract (api-notes §h.3).</summary>
        public void OpenDialog()
        {
            if (Api is not ICoreClientAPI capi) return;

            if (dialog != null)
            {
                // Toggle closed through CloseDialog(), which nulls the field BEFORE TryClose().
                // GuiDialog.TryClose() invokes the OnClosed event synchronously (GuiDialog.cs:349-357,
                // api-notes §h.1), and our OnClosed handler below nulls this same field — so the
                // vanilla BlockEntityTicker.cs:132-136 shape (TryClose(); Dispose(); dialog = null)
                // dereferences a field the event already cleared and throws NullReferenceException
                // on the second interact.
                CloseDialog();
                return;
            }

            dialog = new GuiDialogProjector(this, capi);
            // GuiDialog.TryOpen() — api-notes §h.1 (GuiDialog.cs:319).
            dialog.TryOpen();
            // event Action OnClosed — api-notes §h.1 (GuiDialog.cs:251); same cleanup as BlockEntityTicker.cs:134-141.
            dialog.OnClosed += () =>
            {
                dialog?.Dispose();
                dialog = null;
            };
        }

        /// <summary>
        /// Client: recomputes every enabled layer's outline with the Geometer's cached LayerGeometry and rebuilds
        /// the single ghost mesh only when something render-relevant changed (spec §5 "recomputed only on
        /// parameter change", §6 "positions precomputed and cached").
        /// </summary>
        private void RebuildGeometry(bool force)
        {
            if (renderer == null) return;

            string key = Params.RenderKey();
            if (!force && key == lastRenderKey) return;
            lastRenderKey = key;

            while (geometries.Count < Params.Layers.Count) geometries.Add(new LayerGeometry());

            int budgetColumns = 0;
            cells.Clear();
            layerRenders.Clear();
            previewSegments.Clear();
            dirtyColumns.Clear();

            ShapeCenter center;
            try
            {
                // ShapeCenter validates 0.5 steps (ShapeCenter.cs:15-34); params are rounded, so this only guards corrupt data.
                center = new ShapeCenter(Params.Dx, Params.Dz);
            }
            catch (ArgumentException e)
            {
                Api.Logger.Warning("[shapeprojector] Invalid centre offset at {0}: {1}", Pos, e.Message);
                anyDrapeLayer = false;
                anyFeedbackLayer = false;
                UpdateResampleListener();
                PreviewMarkerVisible = false;
                renderer.SetCells(cells);
                PreviewCellsChanged?.Invoke();   // §10c: preview follows the same (now empty) cache
                return;
            }

            for (int i = 0; i < Params.Layers.Count; i++)
            {
                LayerParams layer = Params.Layers[i];
                // Master switch (user ruling 2026-09-01) gates every layer; per-layer Enabled unchanged.
                if (!Params.Enabled || !layer.Enabled) continue;

                ShapeSpec? spec = layer.ToShapeSpec();
                if (spec == null) continue;

                // LayerGeometry.Update(LayerParameters) rasterizes only when the value-equal parameters changed
                // and never throws (LayerGeometry.cs:35-58); LayerParameters(Shape, Center, MaxRadius, OutlineThickness) (LayerGeometry.cs:4).
                LayerGeometry geom = geometries[i];
                // Per-layer thickness (user request 2026-09-02): the config value is only a NEW layer's
                // starting thickness now, so what rasterizes is the layer's own. LayerParameters is a
                // record, so a changed thickness is a changed value and re-rasterizes by itself.
                geom.Update(new LayerParameters(spec, center, Config.maxRadius, layer.Thickness));
                if (geom.Error != null)
                {
                    Api.Logger.Warning("[shapeprojector] Layer {0} at {1} rejected by geometry: {2}", i, Pos, geom.Error);
                    continue;
                }

                LayerRender lr = new LayerRender
                {
                    LayerIndex = i,
                    Geom = geom,
                    Mode = layer.VerticalMode,
                    FluidSurface = layer.TreatFluidAsSurface,
                    YOffset = layer.YOffset,
                    Color = GhostPalette.Color(layer.ColorIndex),
                    Height = Math.Max(1, layer.Height),
                    CellStart = cells.Count,
                    // Done tint (spec §6 "Optional per-layer", step-6 ruling): Fixed-Y layers only — a
                    // draped ghost follows the surface and can never be "filled" — gated by the client
                    // config showBuildFeedback AND the layer's own switch.
                    Feedback = Config.showBuildFeedback && layer.ShowBuildFeedback && layer.VerticalMode == VerticalMode.FixedY,
                };
                layerRenders.Add(lr);

                // Vertical resolution per column (spec §5a) — the Geometer's DrapeResolver.ResolveColumn
                // (DrapeResolver.cs:31-37): FixedY → y = fixedY, callback never invoked; Drape →
                // y = surface + 1 + drapeOffset; unloaded column (callback null) → fixedY fallback.
                // YOffset is passed as BOTH fixedY and drapeOffset (spec §5a: one optional layer offset,
                // and the unloaded fallback is plain Fixed Y). No ad-hoc math here.
                resolveFluidAsSurface = lr.FluidSurface;
                // LayerGeometry.Positions: "Horizontal positions relative to the projector" (LayerGeometry.cs:18).
                // Cell budget (Config.maxCellsPerProjector). A layer costs columns x height, so the
                // product has to be checked here — no per-field clamp can. Height is surrendered before
                // columns are: a shorter closed figure still reads as the shape, a half-drawn ring does not.
                int room = Math.Max(0, Config.maxCellsPerProjector - cells.Count);
                int columns = geom.Positions.Count;
                if (columns > 0 && (long)columns * lr.Height > room)
                {
                    int fitHeight = Math.Max(1, room / columns);
                    int fitColumns = fitHeight > 0 ? Math.Min(columns, room / fitHeight) : 0;
                    Api.Logger.Warning(
                        "[shapeprojector] Layer {0} at {1} exceeds the {2}-cell budget ({3} columns x height {4}); drawing height {5} over {6} columns.",
                        i, Pos, Config.maxCellsPerProjector, columns, lr.Height, fitHeight, fitColumns);
                    lr.Height = fitHeight;
                    budgetColumns = fitColumns;
                }
                else
                {
                    budgetColumns = columns;
                }

                int emitted = 0;
                foreach (BlockXZ p in geom.Positions)
                {
                    if (emitted++ >= budgetColumns) break;
                    BlockXYZ r = DrapeResolver.ResolveColumn(p, lr.YOffset, lr.Mode, lr.YOffset, surfaceHeightDel!);
                    // Height (user request 2026-09-02): the same outline on Height levels going UP from the
                    // column's resolved Y. In Drape mode that base is this column's own surface, so a tall
                    // draped layer stands the same height everywhere instead of levelling out.
                    for (int k = 0; k < lr.Height; k++)
                    {
                        int y = r.Y + k;
                        // Initial occupancy scan (spec §6): a Fixed-Y cell already filled by a qualifying
                        // block starts green. Every level is tested on its own — a half-built wall shows
                        // exactly how far up it is done.
                        int cellColor = lr.Feedback && IsCellOccupied(r.X, y, r.Z) ? GhostPalette.DoneColor : lr.Color;
                        cells.Add(new GhostCell(r.X, y, r.Z, cellColor));
                    }
                }

                // §10c: the preview's per-layer run over the SAME cells list — no second geometry path.
                lr.Columns = budgetColumns;
                previewSegments.Add(new PreviewSegment(i, lr.CellStart, budgetColumns * lr.Height, GhostPalette.ClampIndex(layer.ColorIndex)));
            }

            anyDrapeLayer = false;
            anyFeedbackLayer = false;
            foreach (LayerRender lr in layerRenders)
            {
                if (lr.Mode == VerticalMode.Drape) anyDrapeLayer = true;
                if (lr.Feedback) anyFeedbackLayer = true;
            }
            UpdateResampleListener();

            // Resolved fractional centre (projector position + dx/dz, spec §3). No world-space marker
            // cube any more (user ruling 10, docs/STATUS.md — it collided visually with the hologram);
            // these accessors feed the hologram's internal offset dot and the GUI's centre readout only.
            PreviewMarkerVisible = Params.EnabledLayerCount() > 0;
            PreviewMarkerX = 0.5 + Params.Dx;
            PreviewMarkerY = 1.5;
            PreviewMarkerZ = 0.5 + Params.Dz;

            renderer.SetCells(cells);
            PreviewCellsChanged?.Invoke();   // §10c: same cache, same moment, event-driven
        }

        /// <summary>
        /// "Done" test for the build-feedback tint (spec §6; step-6 ruling: any NON-FLUID block with a
        /// collision box). Reads the solid layer only — BlockLayersAccess.SolidBlocks = 1 (api-notes §d.9,
        /// BlockLayersAccess.cs:22-27) — so fluids, which live in their own layer, never qualify.
        /// Collision boxes via Cuboidf[] GetCollisionBoxes(IBlockAccessor, BlockPos) — Block.cs:683-686
        /// (returns the block's CollisionBoxes field, Block.cs:303; stateful blocks like stairs/fences
        /// override the virtual). Uses the same scratch BlockPos as the drape descent (sequential,
        /// main-thread only) — zero allocation per cell.
        /// </summary>
        private bool IsCellOccupied(int relX, int relY, int relZ)
        {
            IBlockAccessor ba = Api.World.BlockAccessor;
            BlockPos p = scratchPos ??= Pos.Copy();
            p.X = Pos.X + relX;
            p.Y = Pos.Y + relY;
            p.Z = Pos.Z + relZ;
            Block b = ba.GetBlock(p, BlockLayersAccess.SolidBlocks);
            Cuboidf[]? boxes = b.GetCollisionBoxes(ba, p);
            return boxes != null && boxes.Length > 0;
        }

        /// <summary>
        /// Height callback for DrapeResolver (Geometry README "Callback contract"): column relative to the
        /// projector in, surface Y relative to the projector out (same frame as fixedY), null when unloaded.
        /// Zero allocations per column. Implemented exactly per api-notes §g.3:
        /// - int GetRainMapHeightAt(int posX, int posZ) — IBlockAccessor.cs:506; "topmost non-rain-permeable
        ///   position ... always updated after placing/removing blocks" (doc 488). Water and leaves are not
        ///   rainPermeable → both count as surface. Returns 0 when the map chunk is not loaded
        ///   (BlockAccessorBase.cs:725-745) → null → fixed-Y fallback (spec §5a).
        /// - treatFluidAsSurface off: descend while GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid() —
        ///   Block GetBlock(BlockPos pos, int layer) (IBlockAccessor.cs:119; the int-coordinate overload at
        ///   168 is marked obsolete "use BlockPos version instead, for dimension awareness" by the 1.22.7
        ///   compiler, so the BlockPos overload is used with a reused scratch BlockPos carrying Pos.dimension);
        ///   BlockLayersAccess.Fluid = 2 (BlockLayersAccess.cs:29-32); CollectibleObject.IsLiquid()
        ///   (CollectibleObject.cs:3291, whose doc prescribes exactly this Fluid-layer lookup). Ice sits in
        ///   the fluid layer but IsLiquid() is false, so ice counts as surface in both modes (§g.3).
        /// </summary>
        private int? SurfaceHeightAt(int relX, int relZ)
        {
            IBlockAccessor ba = Api.World.BlockAccessor;
            int wx = Pos.X + relX;
            int wz = Pos.Z + relZ;

            int y = ba.GetRainMapHeightAt(wx, wz);
            if (y <= 0) return null;   // 0 = map chunk not loaded (§g.3) → fixed-Y fallback (spec §5a)

            if (!resolveFluidAsSurface)
            {
                BlockPos p = scratchPos ??= Pos.Copy();   // keeps Pos.dimension; mutated in place, never allocated per column
                p.X = wx;
                p.Z = wz;
                while (y > 0)
                {
                    p.Y = y;
                    if (!ba.GetBlock(p, BlockLayersAccess.Fluid).IsLiquid()) break;
                    y--;
                }
                if (y <= 0) return null;
            }
            return y - Pos.Y;
        }

        /// <summary>
        /// Client BlockChanged handler (spec §5a live update). Per the Geometer's AffectedColumns contract,
        /// a block change at (x, z) can only invalidate that exact outline column — neighbouring columns
        /// never (DrapeResolver.cs:39-50); the O(1) test below is PositionSet.Contains, the very set
        /// AffectedColumns queries. No scanning, no per-event allocation.
        /// </summary>
        private void OnClientBlockChanged(BlockPos changedPos, Block oldBlock)
        {
            if ((!anyDrapeLayer && !anyFeedbackLayer) || renderer == null) return;
            // BlockPos.dimension is a public field (BlockPos.cs:29).
            if (changedPos.dimension != Pos.dimension) return;

            int rx = changedPos.X - Pos.X;
            int rz = changedPos.Z - Pos.Z;
            // Outline positions are clipped to |x|,|z| ≤ maxRadius around the projector (Geometry README,
            // maxRadius rule 2), so anything outside that window cannot be an outline column.
            if (rx > Config.maxRadius || rx < -Config.maxRadius || rz > Config.maxRadius || rz < -Config.maxRadius) return;

            BlockXZ col = new BlockXZ(rx, rz);
            for (int i = 0; i < layerRenders.Count; i++)
            {
                LayerRender lr = layerRenders[i];
                // Drape: any block in the column can move the surface. Fixed-Y feedback: only a change at
                // the cell's own Y can change its occupancy (the tint reads exactly that one block).
                bool relevant = lr.Mode == VerticalMode.Drape
                    ? lr.Geom.PositionSet.Contains(col)
                    : lr.Feedback && changedPos.Y == Pos.Y + lr.YOffset && lr.Geom.PositionSet.Contains(col);
                if (!relevant) continue;
                dirtyColumns.Add(col);
                QueuePatch();
                return;
            }
        }

        /// <summary>
        /// Coalesces any number of same-tick BlockChanged hits (bulk SetBlocks fires per position, §g.2)
        /// into ONE re-resolve + mesh rebuild, off the render loop: a 0 ms delayed callback runs on the
        /// next game tick. RegisterDelayedCallback — BlockEntity.cs:260-269 (wraps IEventAPI.RegisterCallback,
        /// IEventAPI.cs:158) and is auto-unregistered by the BlockEntity base on remove/unload
        /// (BlockEntity.cs:297-303, 352-358). Verified this session — api-notes "Renderer additions".
        /// </summary>
        private void QueuePatch()
        {
            if (patchQueued) return;
            patchQueued = true;
            RegisterDelayedCallback(OnPatchCallback, 0);
        }

        private void OnPatchCallback(float dt)
        {
            patchQueued = false;
            if (renderer == null || dirtyColumns.Count == 0)
            {
                dirtyColumns.Clear();
                return;
            }

            bool changed = false;
            foreach (BlockXZ col in dirtyColumns)
            {
                for (int i = 0; i < layerRenders.Count; i++)
                {
                    LayerRender lr = layerRenders[i];
                    if (!lr.Geom.PositionSet.Contains(col)) continue;

                    int idx = IndexOfColumn(lr.Geom.Positions, col);
                    if (idx < 0 || idx >= lr.Columns) continue;   // budget-truncated columns have no cells
                    // A column occupies Height consecutive cells from here (LayerRender.CellStart).
                    int ci = lr.CellStart + idx * lr.Height;

                    if (lr.Mode == VerticalMode.Drape)
                    {
                        // Re-resolve ONLY this column (spec §5a "recompute ... for affected columns").
                        resolveFluidAsSurface = lr.FluidSurface;
                        BlockXYZ r = DrapeResolver.ResolveColumn(col, lr.YOffset, VerticalMode.Drape, lr.YOffset, surfaceHeightDel!);
                        // The whole stack moves together, so the base level decides whether anything moved.
                        if (cells[ci].Y != r.Y)
                        {
                            for (int k = 0; k < lr.Height; k++) cells[ci + k] = new GhostCell(r.X, r.Y + k, r.Z, lr.Color);
                            changed = true;
                        }
                    }
                    else if (lr.Feedback)
                    {
                        // Done tint (spec §6): re-read occupancy of every level of this column — filling a
                        // wall one course at a time must green one course at a time.
                        for (int k = 0; k < lr.Height; k++)
                        {
                            int y = lr.YOffset + k;
                            int newColor = IsCellOccupied(col.X, y, col.Z) ? GhostPalette.DoneColor : lr.Color;
                            if (cells[ci + k].Color != newColor)
                            {
                                cells[ci + k] = new GhostCell(col.X, y, col.Z, newColor);
                                changed = true;
                            }
                        }
                    }
                }
            }
            dirtyColumns.Clear();

            // Mesh patch strategy: rebuild the whole MeshData + upload. Measured (docs/step5-notes.md):
            // full CPU build for the spec's reference scene (ring 20–23 + circles 11/30 = 476 cubes,
            // 11,424 verts) is 221 µs; a one-column patch including that rebuild is ~0.2 ms — far below a
            // frame, so partial UpdateMesh bookkeeping is not worth its complexity at this size.
            if (changed)
            {
                renderer.SetCells(cells);
                PreviewCellsChanged?.Invoke();   // §10c: drape live update reaches the preview too
            }
        }

        /// <summary>Binary search in a layer's Positions ("strictly ascending by Z then X" — LayerGeometry.cs:19; BlockXZ.CompareTo is that order, BlockXZ.cs:12-16).</summary>
        private static int IndexOfColumn(IReadOnlyList<BlockXZ> positions, BlockXZ col)
        {
            int lo = 0, hi = positions.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int cmp = positions[mid].CompareTo(col);
                if (cmp == 0) return mid;
                if (cmp < 0) lo = mid + 1;
                else hi = mid - 1;
            }
            return -1;
        }

        /// <summary>
        /// The 2 s re-sample exists because fluid-only changes never fire BlockChanged (api-notes §g.2:
        /// the fluid-layer SetBlock paths skip the event; SetBlocksMinimal coverage is unverified).
        /// Registered only while a drape layer exists; all drape layers are re-sampled — not just
        /// treatFluidAsSurface ones — because fluid-layer changes move the result under BOTH rules
        /// (the rain map is updated by the fluid-layer setter, BlockAccessorBase.cs:198 → UpdateRainHeightMap
        /// §g.3, and ice forming/melting changes where the fluid descent stops).
        /// long RegisterGameTickListener(Action&lt;float&gt;, int millisecondInterval, int initialDelayOffsetMs = 0)
        /// — BlockEntity.cs:195-208, wrapping IEventAPI.RegisterGameTickListener (IEventAPI.cs:150);
        /// auto-unregistered by the base in OnBlockRemoved (BlockEntity.cs:294-296) and OnBlockUnloaded
        /// (345-351); UnregisterGameTickListener — BlockEntity.cs:236-240. Verified this session —
        /// api-notes "Renderer additions".
        /// </summary>
        private void UpdateResampleListener()
        {
            if (anyDrapeLayer && resampleListenerId == 0)
            {
                resampleListenerId = RegisterGameTickListener(OnResampleTick, 2000);
            }
            else if (!anyDrapeLayer && resampleListenerId != 0)
            {
                UnregisterGameTickListener(resampleListenerId);
                resampleListenerId = 0;
            }
        }

        private void OnResampleTick(float dt)
        {
            if (renderer == null) return;

            bool changed = false;
            for (int i = 0; i < layerRenders.Count; i++)
            {
                LayerRender lr = layerRenders[i];
                if (lr.Mode != VerticalMode.Drape) continue;

                resolveFluidAsSurface = lr.FluidSurface;
                IReadOnlyList<BlockXZ> positions = lr.Geom.Positions;
                for (int j = 0; j < positions.Count; j++)
                {
                    BlockXYZ r = DrapeResolver.ResolveColumn(positions[j], lr.YOffset, VerticalMode.Drape, lr.YOffset, surfaceHeightDel!);
                    int ci = lr.CellStart + j;
                    if (cells[ci].Y != r.Y)
                    {
                        cells[ci] = new GhostCell(r.X, r.Y, r.Z, lr.Color);
                        changed = true;
                    }
                }
            }
            if (changed)
            {
                renderer.SetCells(cells);
                PreviewCellsChanged?.Invoke();   // §10c: fluid re-sample reaches the preview too
            }
        }

        /// <summary>
        /// Client-side event teardown. The tick listener and any pending delayed callback are unregistered
        /// by the BlockEntity base (OnBlockRemoved: BlockEntity.cs:294-303; OnBlockUnloaded: 345-359);
        /// only the plain C# event subscription (§g.1) is ours to remove.
        /// </summary>
        private void ClientDrapeTeardown()
        {
            if (blockChangedSubscribed && Api is ICoreClientAPI capi)
            {
                capi.Event.BlockChanged -= OnClientBlockChanged;
                blockChangedSubscribed = false;
            }
            resampleListenerId = 0;
            patchQueued = false;
        }

        // public virtual void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) — api-notes §c.3 (BlockEntity.cs:481);
        // shown in the block-info HUD when looking at the projector.
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
            dsc.AppendLine(Lang.Get("shapeprojector:info-center",
                Params.Dx.ToString("0.#", ci), Params.Dz.ToString("0.#", ci),
                (Pos.X + Params.Dx).ToString("0.#", ci), (Pos.Z + Params.Dz).ToString("0.#", ci)));
            for (int i = 0; i < Params.Layers.Count; i++)
            {
                LayerParams l = Params.Layers[i];
                dsc.AppendLine(Lang.Get("shapeprojector:info-layer",
                    i + 1,
                    Lang.Get("shapeprojector:gui-shape-" + l.Shape.ToString().ToLowerInvariant()),
                    l.SizeSummary() + l.ExtentSummary(),
                    l.YOffset,
                    l.Enabled ? "" : Lang.Get("shapeprojector:gui-layer-disabled")));
            }
        }

        // public virtual void OnBlockRemoved() — api-notes §c.3 (BlockEntity.cs:294); call base.
        // Vanilla disposes its renderer here: BlockEntityResonator.cs:334-337 (api-notes §d.1).
        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            CloseDialog();
            ClientDrapeTeardown();
            holoRenderer?.Dispose();   // unregisters, unsubscribes PreviewCellsChanged, frees meshes
            holoRenderer = null;
            renderer?.Dispose();
            renderer = null;
        }

        // public virtual void OnBlockUnloaded() — api-notes §c.3 (BlockEntity.cs:345); call base.
        // Vanilla disposes its renderer here too: BlockEntityResonator.cs:317-320.
        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            CloseDialog();
            ClientDrapeTeardown();
            holoRenderer?.Dispose();   // unregisters, unsubscribes PreviewCellsChanged, frees meshes
            holoRenderer = null;
            renderer?.Dispose();
            renderer = null;
        }

        private void CloseDialog()
        {
            if (dialog == null) return;
            GuiDialogProjector d = dialog;
            dialog = null;
            d.TryClose();
            d.Dispose();
        }
    }
}
