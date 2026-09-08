using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace ShapeProjector
{
    /// <summary>One ghost cube: block-local offset from the projector block, plus its packed RGBA.</summary>
    public readonly record struct GhostCell(int X, int Y, int Z, int Color);

    /// <summary>
    /// Draws the projector's ghost cubes: a translucent, slightly inset cube per outline position,
    /// built into ONE MeshRef whenever the parameters change (spec §5/§6 — never per frame) and drawn
    /// every frame with the engine's own "blockhighlights" shader inside the OIT stage.
    ///
    /// Rendering path and every call below are documented with citations in docs/api-notes.md §d.
    /// Positions come from the block entity (Geometer's rasterizers, spec §5); this class only meshes them.
    /// </summary>
    public class ProjectorRenderer : IRenderer   // IRenderer : IDisposable — api-notes §d.1 (IRenderer.cs:8)
    {
        private const float Inset = 0.05f;

        private readonly ICoreClientAPI capi;
        private readonly BlockPos pos;
        private MeshRef? meshRef;    // merged face rectangles (triangles)
        /// <summary>
        /// Cell-boundary grid over the rectangles (lines), config ghostGridLines — ONE mesh per face
        /// direction (BlockFacing index), because lines cannot be back-face culled the way the faces
        /// are: without this the underside's grid showed through the top face displaced by parallax
        /// (2026-09-08, "how do we expect all these lines to line up?"). Per frame a direction is drawn
        /// only if the camera is on the outward side of that direction's nearest plane
        /// (<see cref="lineBound"/>) — every face of that direction is back-facing otherwise.
        /// </summary>
        private readonly MeshRef?[] lineRefs = new MeshRef?[6];
        private readonly float[] lineBound = new float[6];
        private readonly bool gridLines;
        /// <summary>Set by SetCells: Blocks style keeps every cube face (the classic dense look); Faces style culls back faces.</summary>
        private bool blocksStyle;
        private readonly int gridLineMaxCells;

        // Matrixf (namespace Vintagestory.API.Client) — api-notes §d.5 (Matrixf.cs:8,39,87,201).
        private readonly Matrixf modelViewMat = new Matrixf();

        // Bounding sphere for frustum culling, centred on the projector block (world coords), recomputed per mesh.
        private double cullRadius = 1.0;

        // double RenderOrder — api-notes §d.1 (IRenderer.cs:74). 0.85 sits after OIT entities (0.4)
        // and particles (0.6) and just before the engine's own block highlights (0.89,
        // SystemHighlightBlocks.cs:20).
        public double RenderOrder => 0.85;

        // int RenderRange — api-notes §d.1 (IRenderer.cs:79, "currently not used!").
        public int RenderRange => 128;

        // Spec §6: "render only within renderDistance of the player" — squared client-config distance,
        // tested per frame before the frustum test (ProjectorConfig.renderDistance, spec §7).
        private readonly double renderDistanceSq;

        // Step 6: the mod system carries the session-only hide flag (spec §6 hotkey) and the shared
        // see-through shader program (spec §6 seeThroughDepth; api-notes §f.4).
        private readonly ShapeProjectorModSystem modSystem;
        private readonly float seeThroughDepth;

        // No world-space centre marker (user ruling 10, docs/STATUS.md): the full-size white ghost
        // cube collided visually with the hologram above the block. Centre indication now lives in
        // the GUI's resolved-centre readout and the hologram's internal offset dot only.

        public ProjectorRenderer(ICoreClientAPI capi, BlockPos pos, ShapeProjectorModSystem modSystem)
        {
            this.capi = capi;
            this.modSystem = modSystem;
            // BlockPos.Copy() so a later mutation of the BE's Pos cannot move the mesh.
            this.pos = pos.Copy();
            renderDistanceSq = (double)modSystem.Config.renderDistance * modSystem.Config.renderDistance;
            seeThroughDepth = modSystem.Config.seeThroughDepth;
            gridLines = modSystem.Config.ghostGridLines;
            gridLineMaxCells = modSystem.Config.gridLineMaxCells;
        }

        /// <summary>
        /// Replaces the ghost geometry. Called by the block entity only when the render key changed
        /// (spec §5 "recomputed only on parameter change"). An empty list removes the mesh.
        /// </summary>
        public void SetCells(IReadOnlyList<GhostCell> cells, bool blocks)
        {
            DeleteMeshes();
            if (cells.Count == 0) return;

            MeshData faces;
            MeshData?[] lines = new MeshData?[6];
            blocksStyle = blocks;
            if (blocks) faces = BuildCubeMesh(cells);
            else BuildMeshes(cells, out faces, lines);

            // MeshRef UploadMesh(MeshData data) — api-notes §d.3 (IRenderAPI.cs:525).
            meshRef = capi.Render.UploadMesh(faces);
            for (int d = 0; d < 6; d++) if (lines[d] != null) lineRefs[d] = capi.Render.UploadMesh(lines[d]!);
        }

        private void DeleteMeshes()
        {
            // void DeleteMesh(MeshRef vao) — "Should always be called at the end of a meshes lifetime"
            // — api-notes §d.3 (IRenderAPI.cs:552).
            if (meshRef != null) { capi.Render.DeleteMesh(meshRef); meshRef = null; }
            for (int d = 0; d < 6; d++)
            {
                if (lineRefs[d] != null) { capi.Render.DeleteMesh(lineRefs[d]!); lineRefs[d] = null; }
            }
        }

        /// <summary>
        /// Whether any face of direction <paramref name="d"/> can face the camera: the camera (block-local)
        /// lies on the outward side of the direction's nearest plane. Conservative — a direction is
        /// skipped only when every one of its faces is back-facing.
        /// </summary>
        private bool LinesVisible(int d, double cx, double cy, double cz) => d switch
        {
            0 => cz < lineBound[0],   // NORTH (-Z): camera north of the northernmost north face
            1 => cx > lineBound[1],   // EAST (+X)
            2 => cz > lineBound[2],   // SOUTH (+Z)
            3 => cx < lineBound[3],   // WEST (-X)
            4 => cy > lineBound[4],   // UP
            _ => cy < lineBound[5],   // DOWN
        };

        /// <summary>
        /// Builds the ghost meshes in block-local space (origin = projector block corner) from the
        /// merged faces GhostMesher produces (user request 2026-09-07, full detail at radius 256 x
        /// thickness 256): one triangles mesh of exposed, colour-merged face rectangles — a solid
        /// figure is a few hundred quads instead of six per cell — and, when the config has
        /// ghostGridLines on, one Lines mesh of the cell-boundary grid over those rectangles, which is
        /// what still lets a mark be counted block by block now that the inset cubes are gone.
        /// Layout as the engine's BlockHighlight — api-notes §d.2 (BlockHighlight.cs:245-272): xyz +
        /// rgba only, no uv/normals/flags. Per-face shading as before (CubeMeshUtil.DefaultBlockSideShadingsByFacing
        /// via ColorUtil.ColorMultiply3 — exactly what ModelCubeUtilExt.AddFaceSkipTex did per cube).
        /// The old 0.05 cube inset survives as the plane inset of each rectangle: a ghost face coplanar
        /// with a real block face would z-fight.
        /// </summary>
        private void BuildMeshes(IReadOnlyList<GhostCell> cells, out MeshData faces, MeshData?[] lines)
        {
            List<FaceQuad> quads = GhostMesher.Merge(cells);

            // MeshData(int capacityVertices, int capacityIndices, bool withNormals, bool withUv, bool withRgba, bool withFlags)
            // — api-notes §d.3 (MeshData.cs:665). With Uv == null the rgba attribute lands at VBO location 1,
            // which is what blockhighlights.vsh expects ("vertexColor" at location 1) — api-notes §d.2.
            faces = new MeshData(quads.Count * 4, quads.Count * 6, withNormals: false, withUv: false, withRgba: true, withFlags: false);
            Span<float> xyz = stackalloc float[12];
            double maxDistSq = 0;
            foreach (FaceQuad q in quads)
            {
                GhostMesher.Corners(q, Inset, xyz);
                int color = ColorUtil.ColorMultiply3(q.Color, GhostMesher.Shading(q.Face));
                int baseVert = faces.VerticesCount;
                for (int i = 0; i < 4; i++)
                {
                    float x = xyz[i * 3], y = xyz[i * 3 + 1], z = xyz[i * 3 + 2];
                    // AddVertexSkipTex(float x, float y, float z, int color) — api-notes §o.1 (MeshData.cs:1158-1176).
                    faces.AddVertexSkipTex(x, y, z, color);
                    double d = (double)x * x + (double)y * y + (double)z * z;
                    if (d > maxDistSq) maxDistSq = d;
                }
                // Two triangles, 0-1-2 / 0-2-3 — ModelCubeUtilExt.AddFaceSkipTex's own index pattern.
                faces.AddIndex(baseVert); faces.AddIndex(baseVert + 1); faces.AddIndex(baseVert + 2);
                faces.AddIndex(baseVert); faces.AddIndex(baseVert + 2); faces.AddIndex(baseVert + 3);
            }
            cullRadius = Math.Sqrt(maxDistSq) + 1.5;

            if (!gridLines) return;

            // Grid: darker, more opaque than the face it lies on, so it reads as the seam between two
            // blocks. Two vertices per segment; EnumDrawMode.Lines (api-notes §l.3/§o.1, the hologram's
            // own edge mesh uses the same path). One mesh per face direction, plus that direction's
            // nearest plane, so OnRenderFrame can skip directions whose every face is back-facing.
            for (int d = 0; d < 6; d++) lineBound[d] = (d == 0 || d == 3 || d == 5) ? float.MinValue : float.MaxValue;
            // Past gridLineMaxCells only the rectangle outlines are drawn: a per-cell grid over a
            // quarter-million marks is a fill-rate stall from a glancing angle (2026-09-08).
            bool full = cells.Count <= gridLineMaxCells;
            foreach (FaceQuad q in quads)
            {
                int d = q.Face;
                MeshData grid = lines[d] ??= NewLineMesh(quads.Count);
                float plane = (d == 0 || d == 3 || d == 5) ? q.Slice + Inset : q.Slice + 1 - Inset;
                lineBound[d] = (d == 0 || d == 3 || d == 5) ? Math.Max(lineBound[d], plane) : Math.Min(lineBound[d], plane);
                int c = GridColor(q.Color);
                Action<float, float, float, float, float, float> add = (x0, y0, z0, x1, y1, z1) =>
                {
                    int v = grid.VerticesCount;
                    grid.AddVertexSkipTex(x0, y0, z0, c);
                    grid.AddVertexSkipTex(x1, y1, z1, c);
                    grid.AddIndex(v);
                    grid.AddIndex(v + 1);
                };
                if (full) GhostMesher.GridLines(q, Inset, add);
                else GhostMesher.OutlineLines(q, Inset, add);
            }
        }

        private static MeshData NewLineMesh(int quadCount)
        {
            MeshData grid = new MeshData(quadCount * 2, quadCount * 2, withNormals: false, withUv: false, withRgba: true, withFlags: false);
            grid.mode = EnumDrawMode.Lines;
            return grid;
        }

        /// <summary>
        /// The classic look (ProjectorParams.Style == Blocks, user request 2026-09-08): one inset cube
        /// per cell, exactly the pre-mesher mesh — 24 verts / 36 indices per cube, per-face shading
        /// from CubeMeshUtil.DefaultBlockSideShadingsByFacing via ModelCubeUtilExt.AddFaceSkipTex
        /// (api-notes §d.3, BlockHighlight.cs:254-267). Six faces a cell, so it is held to the smaller
        /// maxCubesPerProjector budget; no grid lines (the insets are the grid).
        /// </summary>
        private MeshData BuildCubeMesh(IReadOnlyList<GhostCell> cells)
        {
            MeshData mesh = new MeshData(cells.Count * 24, cells.Count * 36, withNormals: false, withUv: false, withRgba: true, withFlags: false);
            float size = 1f - 2f * Inset;
            Vec3f sizeXyz = new Vec3f(size, size, size);
            Vec3f center = new Vec3f();
            double maxDistSq = 0;
            foreach (GhostCell c in cells)
            {
                center.X = c.X + 0.5f;
                center.Y = c.Y + 0.5f;
                center.Z = c.Z + 0.5f;
                double d = (double)c.X * c.X + (double)c.Y * c.Y + (double)c.Z * c.Z;
                if (d > maxDistSq) maxDistSq = d;
                for (int i = 0; i < 6; i++)
                {
                    BlockFacing face = BlockFacing.ALLFACES[i];
                    ModelCubeUtilExt.AddFaceSkipTex(mesh, face, center, sizeXyz, c.Color, CubeMeshUtil.DefaultBlockSideShadingsByFacing[face.Index]);
                }
            }
            cullRadius = Math.Sqrt(maxDistSq) + 1.5;
            return mesh;
        }

        /// <summary>Half-brightness RGB at a firmer alpha: packed r | g&lt;&lt;8 | b&lt;&lt;16 | a&lt;&lt;24 (ColorUtil.ColorFromRgba, api-notes §d.3).</summary>
        private static int GridColor(int packed)
        {
            int r = (packed & 0xFF) / 2, g = ((packed >> 8) & 0xFF) / 2, bl = ((packed >> 16) & 0xFF) / 2;
            int a = Math.Min(255, ((packed >> 24) & 0xFF) * 3 / 2);
            return ColorUtil.ColorFromRgba(r, g, bl, a);
        }

        // void OnRenderFrame(float deltaTime, EnumRenderStage stage) — api-notes §d.1 (IRenderer.cs:86).
        // Called inside the OIT stage, i.e. between LoadFrameBuffer(Transparent) and
        // UnloadFrameBuffer — api-notes §d.2 (ClientMain.cs:1163-1175). The engine has already set
        // cull-face OFF, depth-mask OFF, depth-test ON and OIT blending
        // (ClientPlatformWindows.cs:1681-1699), so no GL state is touched here.
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (meshRef == null) return;

            // Client hide hotkey (spec §6): session-only local flag on the mod system. One bool read is
            // the only per-frame addition; everything else stays the step-1 cull/uniform/draw path.
            if (modSystem.ProjectionsHidden) return;

            // EntityPlayer.CameraPos ("Set only by the game client") — api-notes §d.5 (EntityPlayer.cs:41),
            // reached via capi.World.Player (IClientWorldAccessor.cs:31) .Entity (IPlayer.cs:57).
            Vec3d cameraPos = capi.World.Player.Entity.CameraPos;

            // renderDistance enforcement (spec §6): cheapest test first — three subtractions and a compare.
            double distX = pos.X + 0.5 - cameraPos.X;
            double distY = pos.InternalY + 0.5 - cameraPos.Y;
            double distZ = pos.Z + 0.5 - cameraPos.Z;
            if (distX * distX + distY * distY + distZ * distZ > renderDistanceSq) return;

            // FrustumCulling.SphereInFrustum(double x, double y, double z, double radius) takes WORLD
            // coordinates — api-notes §d.7 (FrustumCulling.cs:31; BlockEntitySignRenderer.cs:204).
            // IRenderAPI.DefaultFrustumCuller — api-notes §d.7 (IRenderAPI.cs:23).
            // BlockPos.InternalY = Y + dimension*32768 — api-notes §d.5 (BlockPos.cs:35-45).
            if (!capi.Render.DefaultFrustumCuller.SphereInFrustum(pos.X + 0.5, pos.InternalY + 0.5, pos.Z + 0.5, cullRadius))
            {
                return;
            }

            // Two registrations, one renderer (api-notes §f.4 step 1): OIT = the step-1 primary,
            // depth-tested draw; AfterBlit = the see-through reveal (vanilla precedent for the stage:
            // RiftRenderer.cs:41 registers at stage 9).
            if (stage == EnumRenderStage.AfterBlit)
            {
                RenderSeeThrough(cameraPos);
                return;
            }

            // IShaderProgram GetEngineShader(EnumShaderProgram program) — api-notes §d.2 (IRenderAPI.cs:480);
            // EnumShaderProgram.Blockhighlights = 24 (EnumShaderProgram.cs:92). Returns the same
            // instance the engine's SystemHighlightBlocks uses (RenderAPIBase.cs:379-382).
            IShaderProgram prog = capi.Render.GetEngineShader(EnumShaderProgram.Blockhighlights);

            // ShaderProgramBase.Use() throws if a different shader is active (ShaderProgramBase.cs:265-268),
            // so stop/restore the current one — pattern from ModSystemSupportBeamPlacer.cs:344-347,357-360
            // (api-notes §d.2). IRenderAPI.CurrentActiveShader — IRenderAPI.cs:141.
            IShaderProgram? previous = capi.Render.CurrentActiveShader;
            previous?.Stop();

            // IShaderProgram.Use() — api-notes §d.2 (IShaderProgram.cs:57). Also uploads the engine's
            // default fog/shadow uniforms for this shader (ShaderProgramBase.cs:276-318).
            prog.Use();

            // Uniform names "projectionMatrix" / "modelViewMatrix" — blockhighlights.vsh:8-9 and
            // ShaderProgramBlockhighlights.cs:151-165 (api-notes §d.2).
            // IShaderProgram.UniformMatrix(string, float[]) — IShaderProgram.cs:92.
            // IRenderAPI.CurrentProjectionMatrix — api-notes §d.5 (IRenderAPI.cs:126).
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);

            // modelView = CameraMatrixOrigin * Translate(blockPos - cameraPos):
            //   Matrixf.Identity() (Matrixf.cs:39), .Translate(double,double,double) (Matrixf.cs:87),
            //   .ReverseMul(float[]) = m * this (Matrixf.cs:201) — api-notes §d.5.
            //   IRenderAPI.CameraMatrixOriginf: "Player camera matrix with player positioned at 0,0,0"
            //   (IRenderAPI.cs:121). Same construction as RiftRenderer.cs:121-126 and the engine's
            //   SystemHighlightBlocks.cs:86-87,152 (api-notes §d.2).
            modelViewMat.Identity()
                .Translate(pos.X - cameraPos.X, pos.InternalY - cameraPos.Y, pos.Z - cameraPos.Z)
                .ReverseMul(capi.Render.CameraMatrixOriginf);
            prog.UniformMatrix("modelViewMatrix", modelViewMat.Values);   // Matrixf.Values — Matrixf.cs:8

            // Faces style (2026-09-08, user: "overlaps with the faces on the edges"): a mark is a closed
            // translucent slab — top, bottom and sides — and the OIT stage draws back faces too
            // (cull-face OFF, ClientPlatformWindows.cs:1685), so from any angle the bottom face lands
            // beside the top face on screen and the double-blended overlap reads as slanted fins
            // along every edge. Back faces are culled for this draw only: the rectangles follow the
            // engine's own outward winding (CubeMeshUtil.CubeVertices via GhostMesher.Corners), so a
            // slab reads as one surface, at half the fill cost. The cube style keeps all six faces —
            // that density is the classic look. IRenderAPI.GlEnableCullFace / GlDisableCullFace.
            if (!blocksStyle) capi.Render.GlEnableCullFace();
            // void RenderMesh(MeshRef meshRef) — api-notes §d.3 (IRenderAPI.cs:560).
            capi.Render.RenderMesh(meshRef);
            if (!blocksStyle) capi.Render.GlDisableCullFace();   // back to the OIT stage's state
            // The grid lines share the shader and the transform; RenderMesh draws GL_LINES for a Lines
            // VAO. Only the directions that can face the camera (block-local camera position).
            double lcx = cameraPos.X - pos.X, lcy = cameraPos.Y - pos.InternalY, lcz = cameraPos.Z - pos.Z;
            for (int d = 0; d < 6; d++)
            {
                if (lineRefs[d] != null && LinesVisible(d, lcx, lcy, lcz)) capi.Render.RenderMesh(lineRefs[d]!);
            }

            // IShaderProgram.Stop() — IShaderProgram.cs:59.
            prog.Stop();
            previous?.Use();
        }

        /// <summary>
        /// See-through pass (spec §6/§9; cited recipe api-notes §f.4; precedent RiftRenderer.cs:96-147):
        /// draws the SAME VBO at AfterBlit with the mod's projectorghost shader, which compares each
        /// fragment's linear depth against the primary depth texture and keeps only fragments buried by
        /// ≤ seeThroughDepth blocks, at reduced, depth-faded alpha. Fragments in open view are discarded
        /// there (the OIT pass already drew them); deeper fragments are discarded too (spec §9 — not a
        /// wallhack). Cost per visible projector per frame: 1 shader bind, 2 matrix uniforms, 2 small
        /// uniforms, 1 texture bind, 1 draw call of the existing VBO — no new geometry, no allocation.
        /// GL state: blending is already on at AfterBlit and the default framebuffer's depth was cleared
        /// before the stage (§f.2, ScreenManager.cs:735-742), so occlusion comes solely from the shader's
        /// own compare; depth mask off/on around the draw exactly as RiftRenderer.cs:96,147.
        /// </summary>
        private void RenderSeeThrough(Vec3d cameraPos)
        {
            // Null while unavailable (compile failed, seeThroughMode "off", or before BlockTexturesLoaded).
            IShaderProgram? prog = modSystem.SeeThroughShader;
            if (prog == null || seeThroughDepth <= 0) return;

            // Use() throws if another shader is active (ShaderProgramBase.cs:265-268) — same stop/restore
            // as the OIT path.
            IShaderProgram? previous = capi.Render.CurrentActiveShader;
            previous?.Stop();

            // GLDepthMask — api-notes §d.6 (IRenderAPI.cs:278); pattern RiftRenderer.cs:96/147.
            capi.Render.GLDepthMask(false);
            prog.Use();

            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            modelViewMat.Identity()
                .Translate(pos.X - cameraPos.X, pos.InternalY - cameraPos.Y, pos.Z - cameraPos.Z)
                .ReverseMul(capi.Render.CameraMatrixOriginf);
            prog.UniformMatrix("modelViewMatrix", modelViewMat.Values);

            // BindTexture2D — IShaderProgram.cs:94. FrameBuffers[0].DepthTextureId is the primary depth
            // texture (FrameBufferRef.cs:9, verified this session; format §f.2), safe to sample at
            // AfterBlit — where vanilla's rift shader reads it (RiftRenderer.cs:104).
            prog.BindTexture2D("depthTex", capi.Render.FrameBuffers[0].DepthTextureId, 0);

            // Uniform(string, float, float) — IShaderProgram.cs:77 (RiftRenderer.cs:111 uses it for
            // invFrameSize); FrameWidth/FrameHeight — IRenderAPI.cs:75/80. Uniform(string, float) — :63.
            prog.Uniform("invFrameSize", 1f / capi.Render.FrameWidth, 1f / capi.Render.FrameHeight);
            prog.Uniform("seeThroughDepth", seeThroughDepth);

            capi.Render.RenderMesh(meshRef!);

            prog.Stop();
            capi.Render.GLDepthMask(true);
            previous?.Use();
        }

        // IDisposable via IRenderer. Same teardown as vanilla ResonatorRenderer.Dispose
        // (ResonatorRenderer.cs:103-112) — api-notes §d.1.
        public void Dispose()
        {
            // void UnregisterRenderer(IRenderer renderer, EnumRenderStage renderStage) — api-notes §d.1
            // (IClientEventAPI.cs:208). Both registrations (OIT primary + AfterBlit see-through).
            capi.Event.UnregisterRenderer(this, EnumRenderStage.OIT);
            capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
            DeleteMeshes();
        }
    }
}
