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
    public readonly record struct PreviewSegment(int LayerIndex, int CellStart, int Count, int ColorIndex, int Footprint = 1);

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
            /// <summary>
            /// Fill up to level (user request 2026-09-07). A filled layer's columns have DIFFERENT cell
            /// counts, so the Height stride above does not apply: column j's cells are
            /// cells[CellStart + ColStart[j] .. CellStart + ColStart[j+1]) and ColBase[j] is the base the
            /// column was resolved with (null = unloaded), kept so the fluid re-sample can tell whether
            /// anything moved. Any change under a filled layer is a full cell rebuild — the runs can
            /// change length, which no in-place patch can express.
            /// </summary>
            public bool Fill;
            public int Level;
            public int[]? ColStart;
            public int?[]? ColBase;
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
        /// <summary>
        /// Raised when ONLY the surroundings model was rebuilt (2026-09-08): the figure cells and the
        /// world mesh are untouched, so the hologram re-meshes the model alone. Before this, a
        /// surroundings rescan rebuilt the whole projector — figures, world mesh and all — every
        /// terrainModelMinIntervalMs on a busy server: the "freezing every few seconds".
        /// </summary>
        public event Action? PreviewTerrainChanged;
        private readonly HashSet<Geometry.BlockXZ> dirtyColumns = new HashSet<Geometry.BlockXZ>();
        // System.Func fully qualified: Vintagestory.API.Common declares its own Func<T1,T2,TResult>
        // (DECOMP/api/Vintagestory.API.Common/Func.cs), ambiguous under these usings.
        private System.Func<int, int, int?>? surfaceHeightDel;   // cached delegate: zero allocation per column/rebuild
        private BlockPos? scratchPos;                            // reused for fluid-layer lookups (no per-column allocation)
        private bool resolveFluidAsSurface;               // per-layer flag read by SurfaceHeightAt
        private bool anyDrapeLayer;
        private bool anyFeedbackLayer;
        private bool anyFillLayer;
        private bool blockChangedSubscribed;
        private bool patchQueued;
        /// <summary>Set by a block change under a filled layer: the next patch tick rebuilds every cell instead of patching columns.</summary>
        private bool fullRebuildQueued;
        /// <summary>
        /// A block change inside the surroundings model's box remodels it — but a few hundred ms
        /// later, coalescing a run of placements into one rescan (at radius 256 a rescan is the
        /// whole sampled box; per placement it would hitch, user request "see 256 in action").
        /// </summary>
        private bool terrainRebuildQueued;
        private const int TerrainRebuildDelayMs = 300;
        /// <summary>
        /// The surroundings model, cached (2026-09-08): a layer rebuild that does not touch the ground
        /// reuses it, and block changes only mark it dirty — the rescan itself runs at most every
        /// terrainModelMinIntervalMs. terrainKey pins the radius/reach/centre it was built for.
        /// </summary>
        private readonly List<GhostCell> terrainCells = new List<GhostCell>();
        private int terrainStep = 1;
        private string terrainKey = "";
        private bool terrainDirty = true;
        private long terrainBuiltAtMs = long.MinValue;
        /// <summary>The model is centred on the figures' shared centre (2026-09-08, "centered on the projector's location, not the offset"): the cell nearest (Dx, Dz).</summary>
        private int TerrainCenterX => (int)Math.Floor(Params.Dx + 0.5);
        private int TerrainCenterZ => (int)Math.Floor(Params.Dz + 0.5);
        private long resampleListenerId;
        private static readonly List<GhostCell> NoCells = new List<GhostCell>();
        /// <summary>The surroundings model is built (user request 2026-09-07): projector on and the model switched on, whatever the world marks do.</summary>
        private bool TerrainModelActive => Params.Enabled && Params.TerrainMap;
        /// <summary>
        /// How many leading cells belong to the layers. The surroundings model is appended AFTER them,
        /// so the world renderer takes exactly this prefix and never draws the model in the world; the
        /// hologram reads the whole list. Column patches only touch layer indices, so the prefix stays
        /// valid between rebuilds.
        /// </summary>
        private int worldCellCount;
        /// <summary>The done tint at the opacity of the current build — the column patch must paint the same green the rebuild did.</summary>
        private int doneColorNow = GhostPalette.DoneColor;
        /// <summary>
        /// What the cell budget cut from the last rebuild, in the player's words, one line per layer
        /// (empty when nothing was cut). The dialog shows it after Apply (user, 2026-09-07: radius 256
        /// at thickness 250 "only allowed partial rendering" with nothing on screen saying why —
        /// until now the warning went to the log alone). Client-side only.
        /// </summary>
        public string BudgetReport { get; private set; } = "";

        /// <summary>The advisory threshold for this projector's style (the dial warns past it; nothing is trimmed).</summary>
        public int CellBudget => Params.Style == DrawStyle.Blocks ? Config.maxCubesPerProjector : Config.maxCellsPerProjector;
        /// <summary>The hard ceiling for this projector's style — the only point at which a figure is trimmed (2026-09-08).</summary>
        public int CellCeiling => Params.Style == DrawStyle.Blocks ? Config.hardMaxCubesPerProjector : Config.hardMaxCellsPerProjector;

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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            RebuildGeometryCore(key);
            LogSlow("full rebuild", sw, cells.Count);
        }

        /// <summary>Slow-path timing to client-main.log (2026-09-08): anything over this many ms is worth a line, so a freeze report comes with numbers.</summary>
        private const int SlowMs = 40;
        private void LogSlow(string what, System.Diagnostics.Stopwatch sw, int count)
        {
            if (sw.ElapsedMilliseconds >= SlowMs)
            {
                Api.Logger.Notification("[shapeprojector] {0} at {1}: {2} ms ({3} cells, style {4})", what, Pos, sw.ElapsedMilliseconds, count, Params.Style);
            }
        }

        private void RebuildGeometryCore(string key)
        {
            lastRenderKey = key;
            BudgetReport = "";

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
                PushCells();
                PreviewCellsChanged?.Invoke();   // §10c: preview follows the same (now empty) cache
                return;
            }

            // Ghost opacity (user request 2026-09-07): baked into every vertex colour of this build.
            int alpha = GhostPalette.AlphaFor(Params.GhostOpacity);
            int doneColor = GhostPalette.DoneColorAt(alpha);
            doneColorNow = doneColor;

            // Figures are computed only where something shows them: the world (world marks on) or
            // the hologram (figures switch on). With both off the projector is a pure survey of its
            // surroundings and pays nothing for its layers (user, 2026-09-07).
            bool needFigures = Params.ProjectionEnabled || Params.HologramFigures;
            for (int i = 0; needFigures && i < Params.Layers.Count; i++)
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
                    Color = GhostPalette.Pack(layer.ResolveColor(), alpha),   // colour picker (2026-09-07): any RGB
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
                int room = Math.Max(0, CellCeiling - cells.Count);
                int columns = geom.Positions.Count;

                if (layer.FillToLevel)
                {
                    AppendFilledLayer(i, layer, lr, room, doneColor);
                    continue;
                }

                if (columns > 0 && (long)columns * lr.Height > room)
                {
                    int fitHeight = Math.Max(1, room / columns);
                    int fitColumns = fitHeight > 0 ? Math.Min(columns, room / fitHeight) : 0;
                    Api.Logger.Warning(
                        "[shapeprojector] Layer {0} at {1} exceeds the {2}-cell budget ({3} columns x height {4}); drawing height {5} over {6} columns.",
                        i, Pos, CellCeiling, columns, lr.Height, fitHeight, fitColumns);
                    AddBudgetLine(i, fitColumns, columns, fitHeight, lr.Height);
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
                        int cellColor = lr.Feedback && IsCellOccupied(r.X, y, r.Z) ? doneColor : lr.Color;
                        cells.Add(new GhostCell(r.X, y, r.Z, cellColor));
                    }
                }

                // §10c: the preview's per-layer run over the SAME cells list — no second geometry path.
                lr.Columns = budgetColumns;
                previewSegments.Add(new PreviewSegment(i, lr.CellStart, budgetColumns * lr.Height, GhostPalette.ClampIndex(layer.ColorIndex)));
            }

            anyDrapeLayer = false;
            anyFeedbackLayer = false;
            anyFillLayer = false;
            foreach (LayerRender lr in layerRenders)
            {
                if (lr.Mode == VerticalMode.Drape) anyDrapeLayer = true;
                if (lr.Feedback) anyFeedbackLayer = true;
                if (lr.Fill) anyFillLayer = true;
            }
            UpdateResampleListener();

            // Surroundings model (user request 2026-09-07): appended after every layer's cells into the
            // same cache the hologram reads; the world renderer gets only the prefix before it
            // (PushCells), so the model shows in the hologram with the world marks on or off.
            worldCellCount = cells.Count;
            AppendTerrainCached();

            // Resolved fractional centre (projector position + dx/dz, spec §3). No world-space marker
            // cube any more (user ruling 10, docs/STATUS.md — it collided visually with the hologram);
            // these accessors feed the hologram's internal offset dot and the GUI's centre readout only.
            PreviewMarkerVisible = Params.EnabledLayerCount() > 0;
            PreviewMarkerX = 0.5 + Params.Dx;
            PreviewMarkerY = 1.5;
            PreviewMarkerZ = 0.5 + Params.Dz;

            PushCells();
            PreviewCellsChanged?.Invoke();   // §10c: same cache, same moment, event-driven
        }

        private void AddBudgetLine(int layerIndex, int drawnColumns, int columns, int drawnHeight, int height)
        {
            // Name only what changed (user, 2026-09-07: "7784 of 7784 drawn (so nothing was cut)" — the
            // height clause that carried the cut had overflowed the line).
            string what = "";
            if (drawnHeight != height) what = Lang.Get("shapeprojector:gui-budget-height", drawnHeight, height);
            if (drawnColumns != columns) what += (what.Length > 0 ? ", " : "") + Lang.Get("shapeprojector:gui-budget-columns", drawnColumns, columns);
            if (what.Length == 0) return;
            string line = Lang.Get("shapeprojector:gui-budget-line", layerIndex + 1, what);
            BudgetReport = BudgetReport.Length == 0 ? line : BudgetReport + " " + line;
        }

        /// <summary>Appends the surroundings model after the figure cells: rescanned when dirty or re-parameterised, from the cache otherwise.</summary>
        private void AppendTerrainCached()
        {
            if (!TerrainModelActive) return;
            string tkey = Params.TerrainMapRadius + "," + Params.TerrainMapHeight + "," + TerrainCenterX + "," + TerrainCenterZ;
            if (terrainDirty || tkey != terrainKey)
            {
                int start = cells.Count;
                AppendTerrainModel();
                terrainCells.Clear();
                for (int i = start; i < cells.Count; i++) terrainCells.Add(cells[i]);
                terrainKey = tkey;
                terrainDirty = false;
                terrainBuiltAtMs = Api.World.ElapsedMilliseconds;
            }
            else if (terrainCells.Count > 0)
            {
                // Reuse: the ground did not change, only the figures did.
                int start = cells.Count;
                cells.AddRange(terrainCells);
                previewSegments.Add(new PreviewSegment(-1, start, terrainCells.Count, 4, terrainStep));
            }
        }

        /// <summary>
        /// Rescans the surroundings model alone (2026-09-08): the figure cells stay, the world mesh is
        /// not touched, and the hologram is told to re-mesh the model only (PreviewTerrainChanged).
        /// </summary>
        private void RebuildTerrainOnly()
        {
            if (renderer == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (cells.Count > worldCellCount) cells.RemoveRange(worldCellCount, cells.Count - worldCellCount);
            previewSegments.RemoveAll(s => s.LayerIndex < 0);
            AppendTerrainCached();
            LogSlow("surroundings rescan", sw, cells.Count - worldCellCount);
            PreviewTerrainChanged?.Invoke();
        }

        /// <summary>
        /// Hands the cell cache to the world renderer — or nothing at all while the per-projector
        /// world-marks switch is off (user request 2026-09-07, "hologram only"): the hologram keeps
        /// reading PreviewCells, the world draws no ghost cubes and uploads no mesh.
        /// </summary>
        private void PushCells()
        {
            if (renderer == null) return;
            bool blocks = Params.Style == DrawStyle.Blocks;
            if (!Params.ProjectionEnabled) { renderer.SetCells(NoCells, blocks); return; }
            // The layer prefix only — the surroundings model behind it is hologram-only geometry.
            renderer.SetCells(worldCellCount == cells.Count ? cells : cells.GetRange(0, worldCellCount), blocks);
        }

        /// <summary>
        /// Fill up to level (user request 2026-09-07) for one enabled layer: every outline column is
        /// resolved to its ground (the same callback and fluid rule as draping), the level is the
        /// layer's Y (Fixed Y) or the highest ground under the figure (Follow terrain), and each column
        /// gets the LevelFill run from its ground up to the level plus the layer's Height on top. The
        /// arithmetic and the budget fit are the Geometry library's (LevelFill.cs); this only reads
        /// the world and emits cells. Done tint applies as for any Fixed-Y layer — the fill cells sit
        /// above the ground by construction, so they turn green one by one as the pit is filled and
        /// the ground rises under them (each placement is a full rebuild, see OnClientBlockChanged).
        /// </summary>
        private void AppendFilledLayer(int index, LayerParams layer, LayerRender lr, int room, int doneColor)
        {
            IReadOnlyList<BlockXZ> positions = lr.Geom.Positions;
            int columns = positions.Count;
            bool drape = lr.Mode == VerticalMode.Drape;

            resolveFluidAsSurface = lr.FluidSurface;
            int?[] bases = new int?[columns];
            for (int j = 0; j < columns; j++)
            {
                int? surface = SurfaceHeightAt(positions[j].X, positions[j].Z);
                // Same base as DrapeResolver.ResolveColumn: surface + 1, plus the layer offset when draping.
                bases[j] = surface.HasValue ? surface.Value + 1 + (drape ? lr.YOffset : 0) : null;
            }
            int level = drape ? (LevelFill.HighestBase(bases) ?? lr.YOffset) : lr.YOffset;

            var (fitHeight, fitColumns) = LevelFill.Fit(bases, level, lr.Height, room);
            if (fitHeight != lr.Height || fitColumns != columns)
            {
                Api.Logger.Warning(
                    "[shapeprojector] Filled layer {0} at {1} exceeds the {2}-cell budget ({3} columns, level {4}, height {5}); drawing height {6} over {7} columns.",
                    index, Pos, CellCeiling, columns, level, lr.Height, fitHeight, fitColumns);
                AddBudgetLine(index, fitColumns, columns, fitHeight, lr.Height);
            }

            lr.Fill = true;
            lr.Level = level;
            lr.Height = fitHeight;
            lr.ColBase = bases;
            lr.ColStart = new int[fitColumns + 1];
            for (int j = 0; j < fitColumns; j++)
            {
                lr.ColStart[j] = cells.Count - lr.CellStart;
                var (start, count) = LevelFill.ColumnRun(bases[j], level, fitHeight);
                int x = positions[j].X, z = positions[j].Z;
                for (int k = 0; k < count; k++)
                {
                    int y = start + k;
                    int cellColor = lr.Feedback && IsCellOccupied(x, y, z) ? doneColor : lr.Color;
                    cells.Add(new GhostCell(x, y, z, cellColor));
                }
            }
            lr.ColStart[fitColumns] = cells.Count - lr.CellStart;
            lr.Columns = fitColumns;
            previewSegments.Add(new PreviewSegment(index, lr.CellStart, cells.Count - lr.CellStart, GhostPalette.ClampIndex(layer.ColorIndex)));
        }

        /// <summary>
        /// Surroundings model (user request 2026-09-07, "a model of your home and work"): the standing
        /// blocks around the projector as grey cells in the hologram's cache — one segment with
        /// LayerIndex -1, after every layer's cells so the world renderer's prefix excludes it. Per column within TerrainMapRadius the topmost solid block (the same "solid"
        /// as the done tint: non-fluid with a collision box, IsCellOccupied) inside the ±TerrainMapHeight
        /// window, plus the exposed face of any drop to a lower neighbour column, so walls, cliffs and
        /// pits show their sides and not just their rims; then (block-exact models) every solid block the
        /// OUTSIDE AIR below the rain map touches — walls under eaves and overhangs, the underside of a
        /// porch, a cliff's undercut — found by flood-filling that air from where it meets a lower
        /// neighbour's sky. Sealed interiors are never reached, so they are never drawn: it is a model
        /// seen from outside. Column scans start at the rain map height (GetRainMapHeightAt,
        /// api-notes §g.3 — nothing solid stands above it), so open ground costs one read per column.
        /// Water surfaces (user request 2026-09-07): the rain map counts water as surface, so when the
        /// fluid-layer block at the rain height IsLiquid() (the §g.3 lookup SurfaceHeightAt uses) that
        /// cell is the column's top, drawn in the projector's water colour; the bed beneath is not
        /// modelled — a lake reads as its surface, in blue. Land is drawn in the land colour.
        /// Capped by the projector's cell budget like any layer; the hologram decimates past its own.
        /// </summary>
        private void AppendTerrainModel()
        {
            int r = Params.TerrainMapRadius;
            int h = Params.TerrainMapHeight;
            // Sampling (config terrainModelMaxSpan): past that many columns per axis the model reads
            // every step-th column and draws each as a step-wide tile (PreviewSegment.Footprint), so
            // cost is flat in the radius. Radius 64 → step 1 (block-exact); radius 256 → step 4.
            int span = 2 * r + 1;
            int step = Math.Max(1, (span + Config.terrainModelMaxSpan - 1) / Config.terrainModelMaxSpan);
            int size = (span + step - 1) / step;   // sampled columns per axis; sample i sits at c - r + i*step
            int cx0 = TerrainCenterX, cz0 = TerrainCenterZ;   // the figures' shared centre, not the block
            terrainStep = step;
            int levels = 2 * h + 1;
            const int None = int.MinValue;
            int[] top = new int[size * size];       // topmost solid (or water surface) inside the window
            int[] rainTop = new int[size * size];   // rain-map top: everything above it in the column is sky
            bool[] water = new bool[size * size];

            IBlockAccessor ba = Api.World.BlockAccessor;
            BlockPos fp = scratchPos ??= Pos.Copy();   // keeps Pos.dimension; mutated in place
            for (int iz = 0; iz < size; iz++)
            {
                for (int ix = 0; ix < size; ix++)
                {
                    int dx = cx0 - r + ix * step, dz = cz0 - r + iz * step;
                    int found = None;
                    int rainY = None;
                    bool isWater = false;
                    int rain = ba.GetRainMapHeightAt(Pos.X + dx, Pos.Z + dz);
                    if (rain > 0)
                    {
                        rainY = rain - Pos.Y;
                        if (rainY <= h && rainY >= -h)
                        {
                            // Block GetBlock(BlockPos, int layer) — IBlockAccessor.cs:119; BlockLayersAccess.Fluid = 2;
                            // CollectibleObject.IsLiquid() — CollectibleObject.cs:3291 (api-notes §g.3).
                            fp.X = Pos.X + dx; fp.Y = rain; fp.Z = Pos.Z + dz;
                            if (ba.GetBlock(fp, BlockLayersAccess.Fluid).IsLiquid()) { found = rainY; isWater = true; }
                        }
                        if (found == None)
                        {
                            int yTop = Math.Min(h, rainY);
                            for (int y = yTop; y >= -h; y--)
                            {
                                if (IsCellOccupied(dx, y, dz)) { found = y; break; }
                            }
                        }
                    }
                    top[iz * size + ix] = found;
                    rainTop[iz * size + ix] = rainY;
                    water[iz * size + ix] = isWater;
                }
            }

            // The model has its own budget (config terrainModelMaxCells): it never competes with the
            // figures for maxCellsPerProjector (user, 2026-09-07 — hiding the model gave the figures
            // nothing back, and a full-budget figure left the model nothing).
            int room = Config.terrainModelMaxCells;
            int start = cells.Count;
            bool truncated = false;
            // Real block colours (user, 2026-09-07; the only colouring since the land/water pickers were
            // retired the same day): Block.GetColor(ICoreClientAPI, BlockPos) — Block.cs:2632 — the atlas
            // average of the block's texture (TextureAtlasManager.cs:397: ColorAverage then
            // ReverseColorBytes, so red sits in the low byte exactly as ColorFromRgba packs it) with the
            // climate/season tint applied for that position. -1 (no texture) reads as white. Only the
            // alpha is replaced (the hologram sets its own anyway).
            ICoreClientAPI tcapi = (ICoreClientAPI)Api;   // the model is built on the client only
            // One colour per block type per rebuild, tinted at the projector's own position (2026-09-08:
            // per-cell tinting made neighbouring cells of one material differ and the model shimmer on
            // every rescan; one GetColor per material is also far cheaper than one per cell).
            Dictionary<int, int> colourByBlock = new Dictionary<int, int>();
            int BlockColor(int cx, int cy, int cz, int layerAccess)
            {
                BlockPos bp = scratchPos ??= Pos.Copy();
                bp.X = Pos.X + cx; bp.Y = Pos.Y + cy; bp.Z = Pos.Z + cz;
                Block block = ba.GetBlock(bp, layerAccess);
                if (!colourByBlock.TryGetValue(block.Id, out int c))
                {
                    c = (block.GetColor(tcapi, Pos) & 0xFFFFFF) | (GhostPalette.Alpha << 24);
                    colourByBlock[block.Id] = c;
                }
                return c;
            }
            // One bit per cell of the (sampled) box: emitted once, whichever pass reaches it first.
            // System.Collections.BitArray: at reach 256 the box is 129 x 129 x 513 = 8.5M cells; a bool per
            // cell would be 8.5 MB per rebuild, a bit per cell is ~1 MB.
            System.Collections.BitArray emitted = new System.Collections.BitArray(size * size * levels);
            int Index(int ix, int iz, int y) => ((y + h) * size + iz) * size + ix;
            bool Emit(int ix, int iz, int y, bool isWater)
            {
                int idx = Index(ix, iz, y);
                if (emitted[idx]) return true;
                if (cells.Count - start >= room) { truncated = true; return false; }
                emitted[idx] = true;
                int dx = cx0 - r + ix * step, dz = cz0 - r + iz * step;
                cells.Add(new GhostCell(dx, y, dz, BlockColor(dx, y, dz, isWater ? BlockLayersAccess.Fluid : BlockLayersAccess.SolidBlocks)));
                return true;
            }

            // ---- Pass 1: what the sky sees. Each column's top, and the wall down to where its lower
            // neighbours' tops are — the faces that look up at open sky.
            for (int iz = 0; iz < size && !truncated; iz++)
            {
                for (int ix = 0; ix < size; ix++)
                {
                    int t = top[iz * size + ix];
                    if (t == None) continue;
                    // A water column is its surface only: one cell, no walls down (the shore's own
                    // drop is drawn by the land column beside it).
                    if (water[iz * size + ix])
                    {
                        if (!Emit(ix, iz, t, isWater: true)) break;
                        continue;
                    }

                    // Lowest cell to show: one above the lowest of the four neighbour tops (a neighbour
                    // with nothing solid in the window counts as the window floor); outside the box
                    // a neighbour is taken as level with this column, so the box edge grows no wall.
                    int floor = t;
                    Neighbour(ix - 1, iz, ref floor);
                    Neighbour(ix + 1, iz, ref floor);
                    Neighbour(ix, iz - 1, ref floor);
                    Neighbour(ix, iz + 1, ref floor);

                    int dx = cx0 - r + ix * step, dz = cz0 - r + iz * step;
                    for (int y = t; y >= floor; y--)
                    {
                        // Below the top, read the block rather than assume a wall (user: "existing
                        // buildings ... in the holographic model"): a doorway, a window or the open
                        // space under a porch roof stays open in the model instead of filling in.
                        if (y != t && !IsCellOccupied(dx, y, dz)) continue;
                        if (!Emit(ix, iz, y, isWater: false)) break;
                    }
                    if (truncated) break;
                }
            }

            // ---- Pass 2 (block-exact models only): what the sky does NOT see but the outside does.
            // Under an eave, a porch roof, an overhang or a cliff lip the wall beneath is hidden from
            // pass 1 because its neighbour's top IS the eave (user, 2026-09-07: "a tower doesn't show
            // the sides"). So: flood-fill the OUTSIDE AIR that lies below the rain map — seeded where
            // a column's below-rain air is side by side with a lower neighbour's sky — and draw every
            // solid block that air touches. Sky itself is never walked (pass 1 already drew what it
            // touches), sealed rooms are never entered (nothing connects them to outside air), and an
            // open doorway is entered exactly as far as one could see in through it. Water blocks the
            // fill; underwater cave walls are not modelled.
            if (step == 1 && !truncated)
            {
                System.Collections.BitArray visited = new System.Collections.BitArray(size * size * levels);
                Queue<(int ix, int iz, int y)> queue = new Queue<(int, int, int)>();

                bool IsAir(int ix, int iz, int y)
                {
                    int dx = cx0 - r + ix, dz = cz0 - r + iz;
                    if (IsCellOccupied(dx, y, dz)) return false;
                    BlockPos bp = scratchPos ??= Pos.Copy();
                    bp.X = Pos.X + dx; bp.Y = Pos.Y + y; bp.Z = Pos.Z + dz;
                    return !ba.GetBlock(bp, BlockLayersAccess.Fluid).IsLiquid();
                }
                void Seed(int ix, int iz, int y)
                {
                    int idx = Index(ix, iz, y);
                    if (visited[idx]) return;
                    if (!IsAir(ix, iz, y)) return;
                    visited[idx] = true;
                    queue.Enqueue((ix, iz, y));
                }
                // Seeds: in column c, the air cells between a lower neighbour's rain top and c's own
                // — they see that neighbour's sky sideways, so they are outside air.
                for (int iz = 0; iz < size; iz++)
                {
                    for (int ix = 0; ix < size; ix++)
                    {
                        int rc = rainTop[iz * size + ix];
                        if (rc == None) continue;
                        SeedFrom(ix - 1, iz); SeedFrom(ix + 1, iz); SeedFrom(ix, iz - 1); SeedFrom(ix, iz + 1);
                        void SeedFrom(int nix, int niz)
                        {
                            if (nix < 0 || nix >= size || niz < 0 || niz >= size) return;
                            int rn = rainTop[niz * size + nix];
                            if (rn == None || rn >= rc) return;
                            int yLo = Math.Max(-h, rn + 1), yHi = Math.Min(h, rc);
                            for (int y = yLo; y <= yHi; y++) Seed(ix, iz, y);
                        }
                    }
                }

                while (queue.Count > 0 && !truncated)
                {
                    var (ix, iz, y) = queue.Dequeue();
                    Step(ix - 1, iz, y); Step(ix + 1, iz, y); Step(ix, iz - 1, y); Step(ix, iz + 1, y); Step(ix, iz, y - 1); Step(ix, iz, y + 1);
                    void Step(int nix, int niz, int ny)
                    {
                        if (truncated) return;
                        if (nix < 0 || nix >= size || niz < 0 || niz >= size || ny < -h || ny > h) return;
                        int rn = rainTop[niz * size + nix];
                        if (rn == None || ny > rn) return;   // unloaded, or sky: pass 1's business
                        int dx = cx0 - r + nix, dz = cz0 - r + niz;
                        if (IsCellOccupied(dx, ny, dz))
                        {
                            Emit(nix, niz, ny, isWater: false);
                            return;
                        }
                        int idx = Index(nix, niz, ny);
                        if (visited[idx]) return;
                        BlockPos bp = scratchPos ??= Pos.Copy();
                        bp.X = Pos.X + dx; bp.Y = Pos.Y + ny; bp.Z = Pos.Z + dz;
                        if (ba.GetBlock(bp, BlockLayersAccess.Fluid).IsLiquid()) return;
                        visited[idx] = true;
                        queue.Enqueue((nix, niz, ny));
                    }
                }
            }

            if (truncated)
            {
                Api.Logger.Warning("[shapeprojector] Surroundings model at {0} truncated at its {1}-cell budget (radius {2}, reach {3}).", Pos, Config.terrainModelMaxCells, r, h);
            }
            if (cells.Count > start)
            {
                previewSegments.Add(new PreviewSegment(-1, start, cells.Count - start, 4, step));
            }

            // Sampled-grid neighbour: outside the grid a neighbour is taken as level with this column,
            // so the box edge grows no wall.
            void Neighbour(int nix, int niz, ref int floor)
            {
                if (nix < 0 || nix >= size || niz < 0 || niz >= size) return;
                int nt = top[niz * size + nix];
                int bottom = nt == None ? -h : nt + 1;
                if (bottom < floor) floor = bottom;
            }
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
            if (Params.Frozen) return;   // freeze (2026-09-08): the marks stay as last built
            bool terrain = TerrainModelActive;
            if ((!anyDrapeLayer && !anyFeedbackLayer && !anyFillLayer && !terrain) || renderer == null) return;
            // BlockPos.dimension is a public field (BlockPos.cs:29).
            if (changedPos.dimension != Pos.dimension) return;

            int rx = changedPos.X - Pos.X;
            int rz = changedPos.Z - Pos.Z;

            // Surroundings model (2026-09-07): any change inside its box (one block of slack above,
            // for a block placed on the window's top face) remodels it — a full cell rebuild on the
            // next tick, coalesced with everything else that tick.
            if (terrain)
            {
                int ry = changedPos.Y - Pos.Y;
                int r = Params.TerrainMapRadius, h = Params.TerrainMapHeight;
                int tx = rx - TerrainCenterX, tz = rz - TerrainCenterZ;
                if (tx >= -r && tx <= r && tz >= -r && tz <= r && ry >= -h - 1 && ry <= h + 1)
                {
                    terrainDirty = true;
                    QueueTerrainRebuild();
                    return;
                }
            }
            // Outline positions are clipped to |x|,|z| ≤ maxRadius around the projector (Geometry README,
            // maxRadius rule 2), so anything outside that window cannot be an outline column.
            if (rx > Config.maxRadius || rx < -Config.maxRadius || rz > Config.maxRadius || rz < -Config.maxRadius) return;

            BlockXZ col = new BlockXZ(rx, rz);
            for (int i = 0; i < layerRenders.Count; i++)
            {
                LayerRender lr = layerRenders[i];
                // Filled layer (2026-09-07): any block in the column can move its ground or fill a
                // cell, and a run can change length — full rebuild, never a column patch.
                if (lr.Fill && lr.Geom.PositionSet.Contains(col))
                {
                    fullRebuildQueued = true;
                    QueuePatch();
                    return;
                }
                // Drape: any block in the column can move the surface. Fixed-Y feedback: only a change at
                // one of the column's own Height levels can change an occupancy (the tint reads exactly
                // those blocks).
                int ry0 = changedPos.Y - Pos.Y - lr.YOffset;
                bool relevant = lr.Mode == VerticalMode.Drape
                    ? lr.Geom.PositionSet.Contains(col)
                    : lr.Feedback && ry0 >= 0 && ry0 < lr.Height && lr.Geom.PositionSet.Contains(col);
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
        /// <summary>Debounced remodel of the surroundings (see <see cref="terrainRebuildQueued"/>). RegisterDelayedCallback — BlockEntity.cs:260-269.</summary>
        private void QueueTerrainRebuild()
        {
            if (terrainRebuildQueued) return;
            terrainRebuildQueued = true;
            // Debounce, and never sooner than terrainModelMinIntervalMs after the last rescan: a busy
            // farm, spreading grass or falling snow inside the box then costs one rescan per interval.
            long since = Api.World.ElapsedMilliseconds - terrainBuiltAtMs;
            int wait = (int)Math.Max(TerrainRebuildDelayMs, Config.terrainModelMinIntervalMs - since);
            RegisterDelayedCallback(OnTerrainRebuild, wait);
        }

        private void OnTerrainRebuild(float dt)
        {
            terrainRebuildQueued = false;
            if (renderer == null || Params.Frozen) return;
            RebuildTerrainOnly();
        }

        private void QueuePatch()
        {
            if (patchQueued) return;
            patchQueued = true;
            // A column patch re-meshes the whole layer set; with the merged-face mesher that is tens
            // of ms for a 200,000-cell figure, so past LargeCellCount the patches are coalesced over
            // a few hundred ms instead of per tick (2026-09-07, full-detail figures).
            RegisterDelayedCallback(OnPatchCallback, cells.Count > LargeCellCount ? TerrainRebuildDelayMs : 0);
        }

        /// <summary>Cell count past which live patches are debounced rather than applied on the next tick.</summary>
        private const int LargeCellCount = 50000;

        private void OnPatchCallback(float dt)
        {
            patchQueued = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            OnPatchCallbackCore();
            LogSlow("column patch", sw, cells.Count);
        }

        private void OnPatchCallbackCore()
        {
            if (renderer == null)
            {
                dirtyColumns.Clear();
                fullRebuildQueued = false;
                return;
            }
            if (fullRebuildQueued)
            {
                // Filled layer or surroundings model touched: the geometry cache makes this a cell
                // loop plus a mesh upload — the same mesh upload a column patch pays anyway.
                fullRebuildQueued = false;
                dirtyColumns.Clear();
                RebuildGeometry(force: true);
                return;
            }
            if (dirtyColumns.Count == 0) return;

            bool changed = false;
            foreach (BlockXZ col in dirtyColumns)
            {
                for (int i = 0; i < layerRenders.Count; i++)
                {
                    LayerRender lr = layerRenders[i];
                    if (lr.Fill || !lr.Geom.PositionSet.Contains(col)) continue;   // filled layers rebuild whole (above)

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
                            int newColor = IsCellOccupied(col.X, y, col.Z) ? doneColorNow : lr.Color;
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
                PushCells();
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
            // Filled layers (2026-09-07) resolve their ground in both vertical modes, so a fluid-only
            // change moves their fill too — they keep the listener alive like a draped layer.
            bool wanted = anyDrapeLayer || anyFillLayer;
            if (wanted && resampleListenerId == 0)
            {
                resampleListenerId = RegisterGameTickListener(OnResampleTick, 2000);
            }
            else if (!wanted && resampleListenerId != 0)
            {
                UnregisterGameTickListener(resampleListenerId);
                resampleListenerId = 0;
            }
        }

        /// <summary>Where the time-sliced re-sample resumes: layer index and column index within it.</summary>
        private int resampleLayer, resampleColumn;

        private void OnResampleTick(float dt)
        {
            if (renderer == null || layerRenders.Count == 0 || Params.Frozen) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            OnResampleTickCore();
            LogSlow("water re-check", sw, cells.Count);
        }

        private void OnResampleTickCore()
        {

            // Time-sliced (2026-09-08): at most resampleColumnsPerTick columns per tick, resuming where
            // the last tick stopped, round-robin over the layers. A 250,000-column figure is covered in
            // a dozen ticks instead of stalling every one of them.
            int budget = Config.resampleColumnsPerTick;
            bool changed = false;
            int visitedLayers = 0;
            while (budget > 0 && visitedLayers < layerRenders.Count)
            {
                if (resampleLayer >= layerRenders.Count) { resampleLayer = 0; resampleColumn = 0; }
                LayerRender lr = layerRenders[resampleLayer];
                bool drapeLayer = lr.Mode == VerticalMode.Drape;
                if (!lr.Fill && !drapeLayer)
                {
                    resampleLayer++; resampleColumn = 0; visitedLayers++;
                    continue;
                }

                resolveFluidAsSurface = lr.FluidSurface;
                IReadOnlyList<BlockXZ> positions = lr.Geom.Positions;
                int limit = lr.Fill ? positions.Count : lr.Columns;
                int end = Math.Min(limit, resampleColumn + budget);
                for (int j = resampleColumn; j < end; j++)
                {
                    if (lr.Fill)
                    {
                        // A filled layer's runs change length when its ground moves: compare the bases
                        // it was built with and rebuild whole on the first difference (both vertical
                        // modes — the fill part always rests on the ground).
                        int? surface = SurfaceHeightAt(positions[j].X, positions[j].Z);
                        int? bse = surface.HasValue ? surface.Value + 1 + (drapeLayer ? lr.YOffset : 0) : null;
                        if (bse != lr.ColBase![j])
                        {
                            resampleLayer = 0; resampleColumn = 0;
                            RebuildGeometry(force: true);
                            return;
                        }
                    }
                    else
                    {
                        // Every level of the column (Height stride), only the columns that got cells.
                        BlockXYZ r = DrapeResolver.ResolveColumn(positions[j], lr.YOffset, VerticalMode.Drape, lr.YOffset, surfaceHeightDel!);
                        int ci = lr.CellStart + j * lr.Height;
                        if (cells[ci].Y != r.Y)
                        {
                            for (int k = 0; k < lr.Height; k++) cells[ci + k] = new GhostCell(r.X, r.Y + k, r.Z, lr.Color);
                            changed = true;
                        }
                    }
                }
                budget -= end - resampleColumn;
                resampleColumn = end;
                if (resampleColumn >= limit)
                {
                    resampleLayer++; resampleColumn = 0; visitedLayers++;
                }
            }
            if (changed)
            {
                PushCells();
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

        /// <summary>
        /// Upper bound on layer lines in the block-info HUD (user bug 2026-09-07): the HUD is a
        /// GuiDialog, and GuiDialog.OnMouseDown marks any click inside an opened dialog's composer
        /// bounds as Handled (GuiDialog.cs — the PointInside loop after the composer pass), so a
        /// panel tall enough to reach the screen centre swallows every click at the crosshair:
        /// right-click could not open the dialog and no tool worked while looking at a 48-layer
        /// tower. Eight lines plus the title and centre line stay well above the centre even at
        /// GUI scale 2. Runs of identical layers one block apart collapse into one line first, so
        /// a plain tower needs only one.
        /// </summary>
        internal const int MaxInfoLayerLines = 8;

        // public virtual void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) — api-notes §c.3 (BlockEntity.cs:481);
        // shown in the block-info HUD when looking at the projector.
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
            // Map coordinates, as the game's coordinate HUD shows them: block position minus the default
            // spawn position (HudElementCoordinates.cs:65; the spawn is "usually the map middle").
            BlockPos spawn = Api.World.DefaultSpawnPosition.AsBlockPos;
            dsc.AppendLine(Lang.Get("shapeprojector:info-center",
                Params.Dx.ToString("0.#", ci), Params.Dz.ToString("0.#", ci),
                (Pos.X - spawn.X + Params.Dx).ToString("0.#", ci), (Pos.Z - spawn.Z + Params.Dz).ToString("0.#", ci)));
            AppendLayerInfo(Params.Layers, dsc);
        }

        /// <summary>
        /// The layer lines of the block-info HUD, a pure function of the layer list (it still needs
        /// Lang, so it is not in the Geometry test suite): consecutive layers with the same shape,
        /// size, extent and switch whose Y offsets climb by exactly one (what Add Layer Up produces)
        /// print as a single "Layers a–b" line; after <see cref="MaxInfoLayerLines"/> lines the
        /// rest is one "… and N more" line. Layer numbers are 1-based, matching the dialog's list.
        /// </summary>
        internal static void AppendLayerInfo(List<LayerParams> layers, StringBuilder dsc)
        {
            int lines = 0;
            int i = 0;
            while (i < layers.Count)
            {
                LayerParams first = layers[i];
                string shape = Lang.Get("shapeprojector:gui-shape-" + first.Shape.ToString().ToLowerInvariant());
                string size = first.SizeSummary() + first.ExtentSummary();
                string state = first.Enabled ? "" : Lang.Get("shapeprojector:gui-layer-disabled");

                int j = i + 1;
                while (j < layers.Count && SameRun(first, layers[j], size) && layers[j].YOffset == layers[j - 1].YOffset + 1) j++;
                // layers[i..j-1] is one run

                if (lines >= MaxInfoLayerLines)
                {
                    dsc.AppendLine(Lang.Get("shapeprojector:info-layer-more", layers.Count - i, layers.Count));
                    return;
                }

                if (j - i == 1)
                {
                    dsc.AppendLine(Lang.Get("shapeprojector:info-layer", i + 1, shape, size, first.YOffset, state));
                }
                else
                {
                    dsc.AppendLine(Lang.Get("shapeprojector:info-layer-run",
                        i + 1, j, shape, size, first.YOffset, layers[j - 1].YOffset, state));
                }
                lines++;
                i = j;
            }
        }

        private static bool SameRun(LayerParams a, LayerParams b, string aSize)
        {
            return a.Shape == b.Shape
                && a.Enabled == b.Enabled
                && aSize == b.SizeSummary() + b.ExtentSummary();
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
