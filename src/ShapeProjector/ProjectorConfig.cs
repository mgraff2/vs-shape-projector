using System;
using Vintagestory.API.Common;

namespace ShapeProjector
{
    /// <summary>
    /// ModConfig/shapeprojector.json (spec §7). Public fields with the spec's defaults, as the
    /// API doc prescribes for LoadModConfig/StoreModConfig ("For T just make a class with all
    /// fields public" — api-notes.md §d.11, ICoreAPICommon.cs:115-122).
    /// Not synchronised between client and server (same doc); the server's copy is authoritative
    /// for validation, the client's copy only shapes what the GUI offers.
    /// </summary>
    public class ProjectorConfig
    {
        public const string FileName = "shapeprojector.json";

        /// <summary>
        /// Largest radius (and largest centre offset) any layer may use, in blocks. Raised from 128 to
        /// 256 on user request 2026-09-02. Nothing in the rasterizers is bounded by this number itself:
        /// it is the clip window (Clip.ToWindow) and the validation ceiling, and cost is held by
        /// maxCellsPerProjector, which sees the columns x height product this cannot.
        /// </summary>
        public int maxRadius = 256;
        // Spec §10d: default raised 8 → 48 to accommodate Add-Layer-Up towers.
        public int maxLayersPerProjector = 48;
        public int renderDistance = 160;
        /// <summary>Thickness a NEW layer starts at. Each layer carries its own thickness from there
        /// (user request 2026-09-02); this is only the starting value, not a global override.</summary>
        public int outlineThickness = 1;
        /// <summary>Cap on a layer's own outline thickness (user request 2026-09-02). Thickness grows
        /// INWARD from the shape's outer extent, so the real ceiling is the shape's own radius
        /// (LayerParams.MaxThickness) — at that value the figure is solid. Raised 32 → 256 = maxRadius
        /// on user request 2026-09-07 ("thickness maximum up to the radius") so no figure is stopped
        /// short of solid; cost is held by maxCellsPerProjector, which sees the columns x height product.</summary>
        public int maxOutlineThickness = 256;
        /// <summary>Cap on a layer's own vertical extent in blocks (user request 2026-09-02): one layer
        /// can stand N blocks tall instead of needing N stacked layers. Guards the cell count, which
        /// grows linearly with it.</summary>
        public int maxLayerHeight = 256;
        /// <summary>
        /// Hard ceiling on the ghost cells ONE projector may hold in the world, across every layer.
        /// Before per-layer thickness and height existed the worst case was bounded by the outline
        /// perimeter (a few hundred cells a layer); a layer's cost is columns x height. maxRadius and
        /// maxLayersPerProjector cannot see that product — only this can. Over budget, a layer loses
        /// vertical extent first (the figure stays closed and recognisable) and columns only as a last
        /// resort, with a warning naming the layer and a line in the dial. Raised 60,000 → 300,000 on
        /// 2026-09-07 with the merged-face mesher: drawing cost no longer scales with cells (a solid
        /// radius-256 disc, 206,000 cells, is a few hundred quads), so this now guards CPU work —
        /// column resolution, occupancy scans and the merge itself — not the GPU. The hologram has
        /// its own separate previewMaxBlocks.
        /// </summary>
        public int maxCellsPerProjector = 300000;
        /// <summary>
        /// 2026-09-08 (user: "I didn't want to cap anything, just ... warn that there may be
        /// rendering issues beyond these thresholds"): maxCellsPerProjector and maxCubesPerProjector
        /// are now ADVISORY — the dial's Marks line turns red past them with a performance warning and
        /// nothing is trimmed. These two are the only hard ceilings, and they exist to keep the client
        /// alive, not to keep it smooth: a Faces figure past 4,000,000 cells or a Blocks one past
        /// 600,000 (14 million vertices) is trimmed, with the trim line saying so.
        /// </summary>
        public int hardMaxCellsPerProjector = 4000000;
        public int hardMaxCubesPerProjector = 600000;
        /// <summary>
        /// The same ceiling for a projector drawn in the Blocks style (ProjectorParams.Style, user
        /// request 2026-09-08): one inset cube per mark, six faces each, so the GPU cost is the cell
        /// count again and the pre-mesher figure applies. The dial shows a projector's marks against
        /// whichever of the two budgets its style uses.
        /// </summary>
        public int maxCubesPerProjector = 60000;
        /// <summary>
        /// Cell-boundary grid lines over the merged ghost faces (2026-09-07: the world mesh is now
        /// exposed, colour-merged rectangles instead of one inset cube per cell — a radius-256 solid
        /// figure is a few hundred quads — and the grid is what still lets a mark be counted block by
        /// block). Off = smooth translucent sheets. Client-side.
        /// </summary>
        public bool ghostGridLines = true;
        /// <summary>
        /// Past this many marks the world grid draws each merged rectangle's outline only, not every
        /// cell boundary (2026-09-08 — the per-cell grid of a 250,000-mark figure is half a million
        /// segments, a fill-rate stall at glancing angles); the miniature has its own, smaller figure.
        /// </summary>
        public int gridLineMaxCells = 60000;
        public int holoGridMaxCells = 20000;
        public bool showBuildFeedback = true;
        /// <summary>
        /// Ceilings for the surroundings model (user request 2026-09-07). The radius ceiling is the
        /// figure radius's (256 — "I want to see 256 in action"); cost is held by
        /// <see cref="terrainModelMaxSpan"/>, not by the radius: past that span the model samples
        /// every 2nd, 3rd, 4th... column and draws each sample as a tile that wide, so a 513-column
        /// box costs what a 129-column box costs and reads as a coarser survey rather than a slower
        /// one. The height window bounds how deep a column scan can go. Both per-projector values
        /// (ProjectorParams.TerrainMapRadius/Height) are clamped to these on the server.
        /// </summary>
        public int maxTerrainMapRadius = 256;
        /// <summary>Raised 32 → 256 on user request 2026-09-07 ("let's increase it to 256") so a whole
        /// tall build or deep pit fits the model; the scan per column still stops at the first solid
        /// block, and the box bitmaps are BitArrays, so the cost of a tall window is in the walls it
        /// finds, not the window itself.</summary>
        public int maxTerrainMapHeight = 256;
        /// <summary>
        /// Most sampled columns per axis in the surroundings model (client-side cost knob): the
        /// sampling step is ceil((2r+1) / this), so radius 64 is still block-exact (129 columns) and
        /// radius 256 samples every 4th column. Also bounds the rescan a block change inside the
        /// box triggers (debounced to a few hundred ms in the block entity).
        /// </summary>
        public int terrainModelMaxSpan = 129;
        /// <summary>
        /// The surroundings model's own cell budget, separate from maxCellsPerProjector so figures and
        /// model never compete (user, 2026-09-07). Hologram-only geometry: the hologram then tiles it
        /// down to previewMaxBlocks on top.
        /// </summary>
        public int terrainModelMaxCells = 60000;
        /// <summary>
        /// Least time between two rescans of the surroundings model, in ms (2026-09-08, "can it update
        /// more efficiently and possibly less often"): block changes inside the box mark the model
        /// dirty and it is rebuilt at most this often, from a cache otherwise. Client-side.
        /// </summary>
        public int terrainModelMinIntervalMs = 3000;
        /// <summary>
        /// Columns the fluid re-sample visits per 2 s tick (2026-09-08, "freezing as I pivot ... at
        /// 250k blocks"): the tick used to walk EVERY column of every Follow-terrain and filled layer,
        /// a quarter-million ground lookups every two seconds on a big figure. It now walks this many
        /// and carries on from there next tick, so a full pass over a huge figure takes a few ticks
        /// and no single tick stalls the frame. Client-side.
        /// </summary>
        public int resampleColumnsPerTick = 20000;
        /// <summary>
        /// Spec section 6 see-through: marks buried up to this many blocks behind solid blocks are drawn
        /// dimmed. OFF by default since 2026-09-08 (user: blocks placed in front of a mark "show
        /// through the solid block ... something wrong there") - the reveal had never compiled before
        /// 1.1.0 fixed its shader, so this is the first time anyone saw it, and the verdict was that a
        /// placed block should hide a mark. Set it to 1-6 to turn the reveal on for yourself.
        /// </summary>
        public int seeThroughDepth = 0;
        /// <summary>"shader" (default): the api-notes §f.4 depth-compare pass at AfterBlit;
        /// "off": no through-terrain reveal (outline is plain depth-tested only).</summary>
        public string seeThroughMode = "shader";
        public string clientHideHotkey = "O";
        /// <summary>Spec §10c (amended): cube-count budget for the holographic miniature; beyond it
        /// the hologram decimates uniformly and silently. Client-side only.</summary>
        public int previewMaxBlocks = 20000;

        /// <summary>Spec §10c: edge length (blocks) of the hologram volume the enabled layers' bbox
        /// is scaled into. Sanitized to 0.25..8.</summary>
        public double holoSize = 1.5;
        /// <summary>User ruling 10 (docs/STATUS.md): vertical gap (blocks) between the block cell's
        /// top face (block-local Y = 1.0) and the BASE of the hologram volume — the mini floats just
        /// above the projector's lens, tunable. Sanitized to -0.5..8. Lowered from 0.5 to 0.0 on
        /// 2026-09-02 (in-game feedback: the mini sat too high); the volume now rests on the lens.</summary>
        public double holoOffsetY = 0.0;
        /// <summary>Spec §10c visibility: "always" (default; while ≥1 layer effectively enabled) |
        /// "guiOpen" (only while this projector's dialog is open) | "off".</summary>
        public string holoMode = "always";
        /// <summary>Ruling 9 (docs/STATUS.md): "layered" (default; treated layer hues) | "mono"
        /// (every layer a single cyan for maximum separation; done tint stays green).</summary>
        public string holoStyle = "layered";
        /// <summary>
        /// Whether the hologram marks out the layer the open dialog is editing (spec §10c "selected
        /// layer renders brighter"). OFF by user request 2026-09-02: the highlight did not track
        /// reliably while building a stack up, so it misinformed more than it informed. The mechanism
        /// is kept behind this switch rather than deleted, so re-enabling it is a config edit and the
        /// tracking problem can be diagnosed with it on. Client-side only.
        /// </summary>
        public bool holoHighlightSelected = false;

        /// <summary>Loads the config, creating it with defaults when absent or unreadable.</summary>
        public static ProjectorConfig Load(ICoreAPI api)
        {
            try
            {
                // T LoadModConfig<T>(string filename) — "Returns null if the file does not exist";
                // doc recommends a try/catch for user typos — api-notes §d.11 (ICoreAPICommon.cs:132-140).
                ProjectorConfig? cfg = api.LoadModConfig<ProjectorConfig>(FileName);
                if (cfg != null)
                {
                    cfg.Sanitize();
                    return cfg;
                }
            }
            catch (Exception e)
            {
                // ICoreAPI.Logger — used the same way by BlockEntity.Initialize (BlockEntity.cs:159), api-notes §c.3.
                api.Logger.Error("[shapeprojector] Could not read ModConfig/" + FileName + ", using defaults:");
                api.Logger.Error(e);
            }

            ProjectorConfig defaults = new ProjectorConfig();
            // void StoreModConfig<T>(T jsonSerializeableData, string filename) — api-notes §d.11 (ICoreAPICommon.cs:122).
            api.StoreModConfig(defaults, FileName);
            return defaults;
        }

        private void Sanitize()
        {
            if (maxRadius < 1) maxRadius = 1;
            if (maxLayersPerProjector < 1) maxLayersPerProjector = 1;
            if (renderDistance < 1) renderDistance = 1;
            if (outlineThickness < 1) outlineThickness = 1;
            if (maxOutlineThickness < 1) maxOutlineThickness = 1;
            if (outlineThickness > maxOutlineThickness) outlineThickness = maxOutlineThickness;
            if (maxLayerHeight < 1) maxLayerHeight = 1;
            if (maxTerrainMapRadius < 1) maxTerrainMapRadius = 1;
            if (maxTerrainMapHeight < 1) maxTerrainMapHeight = 1;
            if (terrainModelMaxSpan < 9) terrainModelMaxSpan = 9;
            if (terrainModelMaxCells < 1000) terrainModelMaxCells = 1000;
            if (terrainModelMinIntervalMs < 300) terrainModelMinIntervalMs = 300;
            if (resampleColumnsPerTick < 1000) resampleColumnsPerTick = 1000;
            if (gridLineMaxCells < 0) gridLineMaxCells = 0;
            if (holoGridMaxCells < 0) holoGridMaxCells = 0;
            if (maxCellsPerProjector < 1000) maxCellsPerProjector = 1000;
            if (maxCubesPerProjector < 1000) maxCubesPerProjector = 1000;
            if (hardMaxCellsPerProjector < maxCellsPerProjector) hardMaxCellsPerProjector = maxCellsPerProjector;
            if (hardMaxCubesPerProjector < maxCubesPerProjector) hardMaxCubesPerProjector = maxCubesPerProjector;
            if (seeThroughDepth < 0) seeThroughDepth = 0;
            if (seeThroughMode != "off") seeThroughMode = "shader";
            if (previewMaxBlocks < 100) previewMaxBlocks = 100;
            if (holoSize < 0.25) holoSize = 0.25;
            if (holoSize > 8) holoSize = 8;
            if (holoOffsetY < -0.5) holoOffsetY = -0.5;
            if (holoOffsetY > 8) holoOffsetY = 8;
            if (holoMode != "guiOpen" && holoMode != "off") holoMode = "always";
            if (holoStyle != "mono") holoStyle = "layered";
        }
    }
}
