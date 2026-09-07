using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace ShapeProjector
{
    /// <summary>
    /// §10c holographic miniature: a live, auto-scaled model of all enabled layers floating above the
    /// projector block, rendered in-world by the same IRenderer path as the full-size outlines.
    ///
    /// Data source (spec §10c "Data source"): EXACTLY the block entity's cached cell list
    /// (BEShapeProjector.PreviewCells / PreviewSegments) — the same cells the world renderer draws,
    /// including drape-sampled terrain Y (so the mini hugs the hillside in miniature) and the
    /// build-feedback done tint. This class NEVER computes geometry; it only maps cached cells into a
    /// small volume above the block.
    ///
    /// World-aligned, never rotating (spec §10c): the transform is translate+scale only — north in
    /// the mini is north in the world; the player orbits by walking.
    ///
    /// Contrast treatment (user ruling 9, docs/STATUS.md): layer hues preserved but brightened,
    /// saturated and far more opaque than the world ghosts; a thin dark edge outline on every mini
    /// cube (Lines mesh); a faint dark translucent backdrop box enclosing the volume so the mini
    /// separates from same-colored world outlines behind it. holoStyle "mono" maps every layer hue
    /// to a single cyan (done tint kept green). Selected-layer marking (§10c) is gated on the
    /// holoHighlightSelected config and is OFF by default — user request 2026-09-02, it did not track
    /// reliably while a stack was being built up. With it on, the selected layer drops its own hue for
    /// the palette green on top of the brightening.
    ///
    /// Discipline: meshes are rebuilt ONLY on PreviewCellsChanged (the BE fires it at every cell-cache
    /// update: param apply, drape patch, fluid re-sample) and on GuiSelectedLayer changes (detected by
    /// one nullable-int compare per frame). The per-frame path is: visibility checks, bind, two draws.
    ///
    /// Rendering path per docs/api-notes.md: IRenderer at EnumRenderStage.OIT with the engine
    /// Blockhighlights shader — identical to ProjectorRenderer's primary pass (§d.1/§d.2). No
    /// see-through pass: the hologram sits in open air above its block; ordinary occlusion is correct.
    /// </summary>
    public class HologramRenderer : IRenderer   // IRenderer : IDisposable — api-notes §d.1 (IRenderer.cs:8)
    {
        // ---- Contrast constants (ruling 9). World ghosts use GhostPalette.Alpha = 110; the mini is
        // deliberately much more opaque and brighter.
        private const int FillAlpha = 235;              // unselected mini cube alpha
        private const int SelectedAlpha = 255;          // selected-layer mini cube alpha
        private const int BrightenNum = 13, BrightenDen = 10;   // ×1.3 RGB boost, clamped
        /// <summary>Hue the selected (currently configured) layer is drawn in, whatever its own colour:
        /// the palette green, GhostPalette entry 3 — the same green the build-feedback done tint uses,
        /// told apart by the selected treatment in <see cref="TreatColor"/>. Only ever reached when
        /// holoHighlightSelected is on, which it is not by default.</summary>
        private static readonly int SelectedHue = GhostPalette.Color(3);
        private static readonly int EdgeColor = ColorUtil.ColorFromRgba(8, 10, 16, 210);   // thin dark edges
        private static readonly int HaloColor = ColorUtil.ColorFromRgba(5, 8, 12, 100);    // faint dark backdrop
        // ColorUtil.ColorFromRgba(r,g,b,a) — "true RGBA order" — api-notes §d.3 (ColorUtil.cs:276-281).

        /// <summary>Mini cube fill fraction of one scaled cell — gaps make single cells readable and leave room for edges.</summary>
        private const float CellFill = 0.82f;
        /// <summary>Projector-position indicator (center honesty, user ruling 10): a small bright dot,
        /// deliberately smaller than a mini cube so it never reads as cube-like at any scale.</summary>
        private const float MarkerFill = 0.45f;

        private readonly ICoreClientAPI capi;
        private readonly BlockPos pos;
        private readonly ShapeProjectorModSystem modSystem;
        private readonly BEShapeProjector be;

        private readonly float holoSize;
        private readonly float holoOffsetY;     // config holoOffsetY (ruling 10): volume BASE = block-local Y 1.0 + this
        private readonly bool guiOpenOnly;      // config holoMode == "guiOpen" ("off" never constructs this class)
        private readonly bool mono;             // config holoStyle == "mono"
        private readonly int maxBlocks;         // previewMaxBlocks now budgets the hologram (spec §10c amendment)
        private readonly double renderDistanceSq;

        private MeshRef? fillRef;               // triangles: backdrop halo + mini cubes + marker
        private MeshRef? lineRef;               // lines: dark cube edges (+ marker edges)

        // Cached transform state, computed once per cell-change rebuild and reused by the
        // selection-only fill rebuild: holo = (cellCenter - bboxCenter) * scale + holoCenter.
        private float scale;
        private float bboxCx, bboxCy, bboxCz;   // block-local bbox center of the framed content
        private float holoCy;                   // holo volume center Y (block-local; X/Z are 0.5)
        private int stride = 1;                 // uniform decimation stride (silent — no GUI note anymore)
        private bool markerShown;

        private int? lastSelected;
        /// <summary>Config holoHighlightSelected: mark the layer the open dialog is editing. Off by default.</summary>
        private readonly bool highlightSelected;
        private double cullRadius = 1.0;

        // Matrixf (namespace Vintagestory.API.Client) — api-notes §d.5 (Matrixf.cs:8,39,87,201).
        private readonly Matrixf modelViewMat = new Matrixf();

        // double RenderOrder — api-notes §d.1 (IRenderer.cs:74); same 0.85 slot as ProjectorRenderer
        // (before the engine's own block highlights at 0.89, SystemHighlightBlocks.cs:20).
        public double RenderOrder => 0.85;
        public int RenderRange => 24;           // "currently not used!" — api-notes §d.1 (IRenderer.cs:79)

        public HologramRenderer(ICoreClientAPI capi, BlockPos pos, ShapeProjectorModSystem modSystem, BEShapeProjector be)
        {
            this.capi = capi;
            this.modSystem = modSystem;
            this.be = be;
            // BlockPos.Copy() so a later mutation of the BE's Pos cannot move the mesh (same rule as ProjectorRenderer).
            this.pos = pos.Copy();

            ProjectorConfig cfg = modSystem.Config;
            holoSize = (float)cfg.holoSize;
            holoOffsetY = (float)cfg.holoOffsetY;
            guiOpenOnly = cfg.holoMode == "guiOpen";
            mono = cfg.holoStyle == "mono";
            highlightSelected = cfg.holoHighlightSelected;
            maxBlocks = cfg.previewMaxBlocks;
            renderDistanceSq = (double)cfg.renderDistance * cfg.renderDistance;

            // Event-driven rebuild (spec §10c): the BE raises PreviewCellsChanged at every cell-cache
            // update — the same moments it hands cells to the world renderer. Plain C# event;
            // unsubscribed in Dispose.
            be.PreviewCellsChanged += OnCellsChanged;
        }

        private void OnCellsChanged()
        {
            lastSelected = be.GuiSelectedLayer;
            RebuildAll();
        }

        /// <summary>
        /// Full rebuild: recomputes framing/decimation from the BE's cached cells, then both meshes.
        /// Runs only on PreviewCellsChanged (never per frame).
        /// </summary>
        private void RebuildAll()
        {
            IReadOnlyList<GhostCell> cells = be.PreviewCells;

            if (cells.Count == 0)
            {
                // Nothing effectively enabled (incl. the projector master switch, which zeroes the
                // cells upstream) — drop both meshes; OnRenderFrame then costs two null checks.
                DeleteMeshes();
                return;
            }

            // ---- Framing: block-local bbox of every SHOWN segment's cells (spec §10c "Scale & framing";
            // the figures can be hidden from the mini, user request 2026-09-07 — then only the
            // surroundings model frames it). Cells are unit cubes at (X..X+1, Y..Y+1, Z..Z+1)
            // relative to the projector block corner.
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            int shown = 0;
            foreach (PreviewSegment seg in be.PreviewSegments)
            {
                if (!ShowSegment(seg)) continue;
                int fpX = seg.Footprint - 1;   // a sampled surroundings tile spans Footprint cells in X and Z
                for (int i = seg.CellStart; i < seg.CellStart + seg.Count; i++)
                {
                    GhostCell c = cells[i];
                    if (c.X < minX) minX = c.X;
                    if (c.Y < minY) minY = c.Y;
                    if (c.Z < minZ) minZ = c.Z;
                    if (c.X + fpX > maxX) maxX = c.X + fpX;
                    if (c.Y > maxY) maxY = c.Y;
                    if (c.Z + fpX > maxZ) maxZ = c.Z + fpX;
                    shown++;
                }
            }
            if (shown == 0)
            {
                DeleteMeshes();
                return;
            }

            // Center honesty (spec §10c): when the shape center offset ≠ 0 the projector's OWN cell
            // (0..1)³ joins the framed model, so the marker showing "you are here" is always inside
            // the mini. Offset read off the BE's marker accessors (world marker = 0.5+dx / 0.5+dz).
            double offDx = be.PreviewMarkerX - 0.5;
            double offDz = be.PreviewMarkerZ - 0.5;
            markerShown = offDx != 0 || offDz != 0;
            if (markerShown)
            {
                if (0 < minX) minX = 0;
                if (0 < minY) minY = 0;
                if (0 < minZ) minZ = 0;
                if (0 > maxX) maxX = 0;
                if (0 > maxY) maxY = 0;
                if (0 > maxZ) maxZ = 0;
            }

            // Surroundings model present (user question 2026-09-07, "why doesn't the environment
            // view center on the projector"): the model box is symmetric about the projector, but
            // figures reaching past its radius on one side, a chunk edge not yet loaded on one side,
            // or the cell budget cutting the far edge all skew the bbox and with it the miniature.
            // With the model on, the horizontal frame is forced symmetric about the projector cell,
            // so the projector is always the middle of the model; vertical framing stays by content
            // (a symmetric height window would waste the volume on empty air).
            bool hasTerrain = false;
            foreach (PreviewSegment seg in be.PreviewSegments) if (seg.LayerIndex < 0) { hasTerrain = true; break; }
            if (hasTerrain)
            {
                int extX = Math.Max(-minX, maxX);
                int extZ = Math.Max(-minZ, maxZ);
                minX = -extX; maxX = extX;
                minZ = -extZ; maxZ = extZ;
            }

            sizeXf = maxX + 1 - minX;
            sizeYf = maxY + 1 - minY;
            sizeZf = maxZ + 1 - minZ;
            bboxCx = minX + sizeXf * 0.5f;
            bboxCy = minY + sizeYf * 0.5f;
            bboxCz = minZ + sizeZf * 0.5f;

            // Auto-scale so the bbox fits the holoSize³ volume; capped at holoSize/6 so a tiny shape
            // (one cell) still reads as a miniature, never as a giant floating cube.
            float extent = Math.Max(sizeXf, Math.Max(sizeYf, sizeZf));
            scale = Math.Min(holoSize / extent, holoSize / 6f);

            // Base-anchored placement (user ruling 10): the volume's BASE sits at block-local
            // Y = 1.0 (the block cell's top face) + holoOffsetY; height is fixed regardless of
            // content, so the volume never bobs on edits.
            holoCy = 1.0f + holoOffsetY + holoSize * 0.5f;

            // Budget (spec §10c amendment): previewMaxBlocks caps the mini's cube count; uniform
            // decimation past it, silent.
            stride = cells.Count <= maxBlocks ? 1 : (cells.Count + maxBlocks - 1) / maxBlocks;

            cullRadius = holoSize + 1.0;

            RebuildStatic(cells);
            RebuildFill(cells);
        }

        /// <summary>Which segments the mini draws: the surroundings model always; the figures only while the projector's HologramFigures switch is on (user request 2026-09-07).</summary>
        private bool ShowSegment(PreviewSegment seg) => seg.LayerIndex < 0 || be.Params.HologramFigures;

        /// <summary>Maps a block-local point into holo space (block-local, around the volume center). Translate+scale only — never rotates (spec §10c).</summary>
        private void MapPoint(float x, float y, float z, out float hx, out float hy, out float hz)
        {
            hx = 0.5f + (x - bboxCx) * scale;
            hy = holoCy + (y - bboxCy) * scale;
            hz = 0.5f + (z - bboxCz) * scale;
        }

        /// <summary>
        /// Selection-independent geometry: the dark edge Lines mesh and (folded into the fill mesh's
        /// rebuild, see RebuildFill) nothing else — edges never change color with selection, so a
        /// GUI selection click does not pay for this mesh.
        /// </summary>
        private void RebuildStatic(IReadOnlyList<GhostCell> cells)
        {
            if (lineRef != null)
            {
                // void DeleteMesh(MeshRef vao) — api-notes §d.3 (IRenderAPI.cs:552).
                capi.Render.DeleteMesh(lineRef);
                lineRef = null;
            }

            int n = cells.Count / stride + be.PreviewSegments.Count + 2;   // upper bound incl. per-segment remainder + marker
            // MeshData(capacityVertices, capacityIndices, withNormals, withUv, withRgba, withFlags)
            // — api-notes §d.3/§o.1 (MeshData.cs:665). Uv null ⇒ rgba lands at VBO location 1 =
            // blockhighlights.vsh "vertexColor" (attribute shift rule, api-notes §d.2, IRenderAPI.cs:519-521).
            MeshData mesh = new MeshData(n * 8, n * 24, withNormals: false, withUv: false, withRgba: true, withFlags: false);
            // public EnumDrawMode mode — api-notes §o.1 (MeshData.cs:198); EnumDrawMode.Lines —
            // api-notes §l.3 (EnumDrawMode.cs:3-8). UploadMesh stores the draw mode on the VAO and
            // RenderMesh draws GL_LINES with it (ClientPlatformWindows.cs:3222, 1004-1023 — §o.1).
            mesh.mode = EnumDrawMode.Lines;

            float half = scale * CellFill * 0.5f;
            foreach (PreviewSegment seg in be.PreviewSegments)
            {
                if (!ShowSegment(seg)) continue;
                float fp = seg.Footprint;   // sampled surroundings tiles are fp cells wide (X/Z), one tall
                float halfXZ = half * fp;
                for (int j = 0; j < seg.Count; j += stride)
                {
                    GhostCell c = cells[seg.CellStart + j];
                    MapPoint(c.X + fp * 0.5f, c.Y + 0.5f, c.Z + fp * 0.5f, out float hx, out float hy, out float hz);
                    AddCubeEdges(mesh, hx, hy, hz, halfXZ, half, halfXZ);
                }
            }
            // No edge outline on the offset indicator (ruling 10): dark cube edges are what make a
            // shape read as a cube — the indicator is a plain bright dot, fill mesh only.

            // MeshRef UploadMesh(MeshData data) — api-notes §d.3 (IRenderAPI.cs:525).
            lineRef = capi.Render.UploadMesh(mesh);
        }

        /// <summary>
        /// Selection-dependent geometry: backdrop halo + mini cubes + marker cube (one triangles VBO).
        /// Rebuilt on cell changes AND on GuiSelectedLayer changes (the cheap trigger a GUI click pays for).
        /// </summary>
        private void RebuildFill(IReadOnlyList<GhostCell> cells)
        {
            if (fillRef != null)
            {
                capi.Render.DeleteMesh(fillRef);
                fillRef = null;
            }

            int n = cells.Count / stride + be.PreviewSegments.Count + 3;   // cubes + remainders + marker + halo
            MeshData mesh = new MeshData(n * 24, n * 36, withNormals: false, withUv: false, withRgba: true, withFlags: false);

            // Backdrop halo (ruling 9): a faint dark translucent box enclosing the content, slightly
            // inflated per axis (flat shapes get a slab, not an empty cube). In the OIT stage its
            // blending is order-independent, so it darkens the scene behind the mini cubes as well —
            // exactly the separation the ruling asks for.
            {
                MapPoint(bboxCx, bboxCy, bboxCz, out float cx, out float cy, out float cz);
                float sx = Math.Max(sizeXf * scale * 1.15f + 0.08f, 0.15f);
                float sy = Math.Max(sizeYf * scale * 1.15f + 0.08f, 0.15f);
                float sz = Math.Max(sizeZf * scale * 1.15f + 0.08f, 0.15f);
                AddCubeFaces(mesh, cx, cy, cz, sx, sy, sz, HaloColor, shaded: false);
            }

            // Selected-layer marking is config-gated and off by default (holoHighlightSelected):
            // with it off every layer renders in its own treated hue and nothing tracks the dialog.
            int? selected = highlightSelected ? lastSelected : null;
            float size = scale * CellFill;
            int doneColor = GhostPalette.DoneColor;
            int monoBase = GhostPalette.Color(0);   // cyan palette entry — holoStyle "mono" (ruling 9)

            foreach (PreviewSegment seg in be.PreviewSegments)
            {
                bool isSelected = selected.HasValue && seg.LayerIndex == selected.Value;
                if (!ShowSegment(seg)) continue;
                // Surroundings model (user request 2026-09-07): LayerIndex -1, always its own colour —
                // never re-hued by mono, never selected. Sampled tiles are Footprint cells wide.
                bool terrain = seg.LayerIndex < 0;
                float fp = seg.Footprint;
                float sizeXZ = size * fp;
                for (int j = 0; j < seg.Count; j += stride)
                {
                    GhostCell c = cells[seg.CellStart + j];
                    // Layer hue from the cached cell itself — done-tinted cells stay green in the mini
                    // (build feedback in miniature); mono maps layer hues to cyan but keeps done green.
                    // User request 2026-09-02: the layer being CONFIGURED overrides its own hue with
                    // SelectedHue so "what did Apply just change" is answerable at a glance, whatever
                    // colour that layer happens to carry. The selected treatment on top (x1.3 brighten,
                    // 35% toward white, full alpha) is what keeps it apart from the build-feedback done
                    // tint, which is the same palette green at the dimmer unselected alpha.
                    // Done cells are told by RGB alone: the world alpha follows the projector's
                    // GhostOpacity (2026-09-07) and TreatColor replaces it anyway.
                    bool isDone = (c.Color & 0xFFFFFF) == (doneColor & 0xFFFFFF);
                    int baseColor = isSelected
                        ? SelectedHue
                        : (mono && !terrain && !isDone ? monoBase : c.Color);
                    int color = TreatColor(baseColor, isSelected);
                    MapPoint(c.X + fp * 0.5f, c.Y + 0.5f, c.Z + fp * 0.5f, out float hx, out float hy, out float hz);
                    AddCubeFaces(mesh, hx, hy, hz, sizeXZ, size, sizeXZ, color, shaded: true);
                }
            }

            if (markerShown)
            {
                // Center honesty (ruling 10): the projector's own position within the model — a small
                // bright white DOT (0.45× a mini cube, unshaded, no edge lines) so it never reads as
                // a cube at any scale; drawn only while the center offset ≠ 0 (Q48 framing unchanged).
                MapPoint(0.5f, 0.5f, 0.5f, out float mx, out float my, out float mz);
                float ms = scale * MarkerFill;
                AddCubeFaces(mesh, mx, my, mz, ms, ms, ms, ColorUtil.ColorFromRgba(250, 250, 250, 255), shaded: false);
            }

            fillRef = capi.Render.UploadMesh(mesh);
        }

        /// <summary>Framed bbox sizes in block units (set in RebuildAll; consumed by the halo).</summary>
        private float sizeXf, sizeYf, sizeZf;

        /// <summary>
        /// Ruling-9 hue treatment: preserve the layer hue, boost brightness/saturation ×1.3 (clamped)
        /// and raise opacity well above the world ghosts' alpha 110; the selected layer is additionally
        /// mixed 35% toward white at full alpha (spec §10c "selected layer renders brighter"). The
        /// caller passes <see cref="SelectedHue"/> as the packed colour for a selected layer, so this
        /// brightening is what separates the selection green from the dimmer done-tint green.
        /// Packed layout is r | g&lt;&lt;8 | b&lt;&lt;16 | a&lt;&lt;24 — ColorFromRgba, api-notes §d.3 (ColorUtil.cs:276-281).
        /// </summary>
        private static int TreatColor(int packed, bool selected)
        {
            int r = packed & 0xFF, g = (packed >> 8) & 0xFF, b = (packed >> 16) & 0xFF;
            r = Math.Min(255, r * BrightenNum / BrightenDen);
            g = Math.Min(255, g * BrightenNum / BrightenDen);
            b = Math.Min(255, b * BrightenNum / BrightenDen);
            if (selected)
            {
                r += (255 - r) * 7 / 20;
                g += (255 - g) * 7 / 20;
                b += (255 - b) * 7 / 20;
                return ColorUtil.ColorFromRgba(r, g, b, SelectedAlpha);
            }
            return ColorUtil.ColorFromRgba(r, g, b, FillAlpha);
        }

        /// <summary>Six faces of an axis-aligned box, per-face shading as the world ghosts (or flat for the halo).</summary>
        private static void AddCubeFaces(MeshData mesh, float cx, float cy, float cz, float sx, float sy, float sz, int color, bool shaded)
        {
            Vec3f center = new Vec3f(cx, cy, cz);
            Vec3f sizeXyz = new Vec3f(sx, sy, sz);
            for (int i = 0; i < 6; i++)
            {
                // BlockFacing.ALLFACES — api-notes §d.3 (BlockFacing.cs:76).
                BlockFacing face = BlockFacing.ALLFACES[i];
                // ModelCubeUtilExt.AddFaceSkipTex(MeshData, BlockFacing, Vec3f centerXyz, Vec3f sizeXyz, int color, float brightness)
                // — api-notes §d.3 (ModelCubeUtilExt.cs:92); shading table CubeMeshUtil.DefaultBlockSideShadingsByFacing
                // (CubeMeshUtil.cs:22), the BlockHighlight.cs:254-267 pattern (§d.2).
                float brightness = shaded ? CubeMeshUtil.DefaultBlockSideShadingsByFacing[face.Index] : 1f;
                ModelCubeUtilExt.AddFaceSkipTex(mesh, face, center, sizeXyz, color, brightness);
            }
        }

        /// <summary>12 dark edges of a cube: 8 vertices + 24 line indices (corner index bits: x=1, y=2, z=4).</summary>
        private static readonly int[] EdgePairs =
        {
            0,1, 2,3, 4,5, 6,7,   // X-parallel
            0,2, 1,3, 4,6, 5,7,   // Y-parallel
            0,4, 1,5, 2,6, 3,7,   // Z-parallel
        };

        private static void AddCubeEdges(MeshData mesh, float cx, float cy, float cz, float halfX, float halfY, float halfZ)
        {
            int baseVert = mesh.VerticesCount;   // public int VerticesCount — api-notes §d.3 (MeshData.cs:208)
            for (int corner = 0; corner < 8; corner++)
            {
                float x = cx + (((corner & 1) != 0) ? halfX : -halfX);
                float y = cy + (((corner & 2) != 0) ? halfY : -halfY);
                float z = cz + (((corner & 4) != 0) ? halfZ : -halfZ);
                // AddVertexSkipTex(float x, float y, float z, int color) writes xyz + packed rgba only
                // — api-notes §o.1 (MeshData.cs:1158-1176).
                mesh.AddVertexSkipTex(x, y, z, EdgeColor);
            }
            for (int i = 0; i < EdgePairs.Length; i++)
            {
                // void AddIndex(int index) — api-notes §o.1 (MeshData.cs:1418-1425); 2 indices per line segment.
                mesh.AddIndex(baseVert + EdgePairs[i]);
            }
        }

        private void DeleteMeshes()
        {
            if (fillRef != null) { capi.Render.DeleteMesh(fillRef); fillRef = null; }
            if (lineRef != null) { capi.Render.DeleteMesh(lineRef); lineRef = null; }
        }

        // void OnRenderFrame(float deltaTime, EnumRenderStage stage) — api-notes §d.1 (IRenderer.cs:86).
        // OIT stage: engine has already set cull-face OFF, depth-mask OFF, depth-test ON and OIT
        // blending (ClientPlatformWindows.cs:1681-1699 — api-notes §d.2); no GL state touched here.
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (fillRef == null) return;

            // Client hide hotkey suppresses the hologram too (spec §10c "Visibility").
            if (modSystem.ProjectionsHidden) return;

            // Per-projector hologram switch (user request 2026-09-02): the mini goes dark on its own
            // while the world outlines keep projecting. Read straight from the synced params every
            // frame — one bool test, and no mesh depends on it, so it is deliberately absent from
            // ProjectorParams.RenderKey and toggles with no rebuild at all.
            if (!be.Params.HologramEnabled) return;

            // holoMode "guiOpen": visible only while a dialog is open on THIS block entity
            // (BEShapeProjector.GuiSelectedLayer is non-null exactly then).
            int? selected = be.GuiSelectedLayer;
            if (guiOpenOnly && !selected.HasValue) return;

            // Selection-change trigger (one nullable-int compare per frame): rebuild the fill mesh so
            // the selected layer brightens. Not per-frame work — it runs only on an actual GUI click.
            // Skipped entirely when the highlight is off: the rebuild would produce an identical mesh.
            if (highlightSelected && selected != lastSelected)
            {
                lastSelected = selected;
                RebuildFill(be.PreviewCells);
                if (fillRef == null) return;
            }

            // EntityPlayer.CameraPos — api-notes §d.5 (EntityPlayer.cs:41) via capi.World.Player.Entity.
            Vec3d cameraPos = capi.World.Player.Entity.CameraPos;

            // renderDistance enforcement (spec §6 rule, applied to the hologram too): cheapest test first.
            double distX = pos.X + 0.5 - cameraPos.X;
            double distY = pos.InternalY + holoCy - cameraPos.Y;
            double distZ = pos.Z + 0.5 - cameraPos.Z;
            if (distX * distX + distY * distY + distZ * distZ > renderDistanceSq) return;

            // FrustumCulling.SphereInFrustum(x, y, z, radius) in WORLD coordinates — api-notes §d.7
            // (FrustumCulling.cs:31; usage precedent BlockEntitySignRenderer.cs:204);
            // BlockPos.InternalY — api-notes §d.5 (BlockPos.cs:35-45).
            if (!capi.Render.DefaultFrustumCuller.SphereInFrustum(pos.X + 0.5, pos.InternalY + holoCy, pos.Z + 0.5, cullRadius))
            {
                return;
            }

            // Engine Blockhighlights program — api-notes §d.2 (IRenderAPI.cs:480; EnumShaderProgram.cs:92);
            // stop/restore the active shader first (ShaderProgramBase.cs:265-268; pattern
            // ModSystemSupportBeamPlacer.cs:344-347,357-360).
            IShaderProgram prog = capi.Render.GetEngineShader(EnumShaderProgram.Blockhighlights);
            IShaderProgram? previous = capi.Render.CurrentActiveShader;
            previous?.Stop();
            prog.Use();

            // Uniforms per blockhighlights.vsh:8-9 (api-notes §d.2); UniformMatrix — IShaderProgram.cs:92;
            // CurrentProjectionMatrix — IRenderAPI.cs:126; CameraMatrixOriginf — IRenderAPI.cs:121;
            // Matrixf Identity/Translate/ReverseMul — api-notes §d.5 (Matrixf.cs:39,87,201).
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            modelViewMat.Identity()
                .Translate(pos.X - cameraPos.X, pos.InternalY - cameraPos.Y, pos.Z - cameraPos.Z)
                .ReverseMul(capi.Render.CameraMatrixOriginf);
            prog.UniformMatrix("modelViewMatrix", modelViewMat.Values);

            // void RenderMesh(MeshRef) — api-notes §d.3 (IRenderAPI.cs:560). Two draws: translucent
            // fill (halo + cubes), then the dark edge lines (same shader; the VAO carries GL_LINES —
            // api-notes §o.1, ClientPlatformWindows.cs:3222, 1004-1023).
            capi.Render.RenderMesh(fillRef);
            if (lineRef != null) capi.Render.RenderMesh(lineRef);

            prog.Stop();
            previous?.Use();
        }

        // IDisposable via IRenderer — teardown pattern ResonatorRenderer.cs:103-112 (api-notes §d.1).
        public void Dispose()
        {
            be.PreviewCellsChanged -= OnCellsChanged;
            // void UnregisterRenderer(IRenderer, EnumRenderStage) — api-notes §d.1 (IClientEventAPI.cs:208).
            capi.Event.UnregisterRenderer(this, EnumRenderStage.OIT);
            DeleteMeshes();
        }
    }
}
