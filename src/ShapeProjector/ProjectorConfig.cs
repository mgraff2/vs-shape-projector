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
        /// Hard ceiling on the ghost cubes ONE projector may draw in the world, across every layer.
        /// Before per-layer thickness and height existed the worst case was bounded by the outline
        /// perimeter (a few hundred cubes a layer); now a layer's cost is columns x height and a big
        /// thick tall layer can ask for millions, which no client survives. maxRadius and
        /// maxLayersPerProjector cannot see that product — only this can. Over budget, a layer loses
        /// vertical extent first (the figure stays closed and recognisable) and columns only as a last
        /// resort, with a warning naming the layer. The hologram has its own separate previewMaxBlocks.
        /// </summary>
        public int maxCellsPerProjector = 60000;
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
        public int maxTerrainMapHeight = 32;
        /// <summary>
        /// Most sampled columns per axis in the surroundings model (client-side cost knob): the
        /// sampling step is ceil((2r+1) / this), so radius 64 is still block-exact (129 columns) and
        /// radius 256 samples every 4th column. Also bounds the rescan a block change inside the
        /// box triggers (debounced to a few hundred ms in the block entity).
        /// </summary>
        public int terrainModelMaxSpan = 129;
        public int seeThroughDepth = 6;
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
            if (maxCellsPerProjector < 1000) maxCellsPerProjector = 1000;
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
