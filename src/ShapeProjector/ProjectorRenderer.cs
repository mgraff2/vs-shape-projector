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
        private MeshRef? lineRef;    // cell-boundary grid over them (lines), config ghostGridLines
        private readonly bool gridLines;

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
        }

        /// <summary>
        /// Replaces the ghost geometry. Called by the block entity only when the render key changed
        /// (spec §5 "recomputed only on parameter change"). An empty list removes the mesh.
        /// </summary>
        public void SetCells(IReadOnlyList<GhostCell> cells)
        {
            DeleteMeshes();
            if (cells.Count == 0) return;

            BuildMeshes(cells, out MeshData faces, out MeshData? lines);

            // MeshRef UploadMesh(MeshData data) — api-notes §d.3 (IRenderAPI.cs:525).
            meshRef = capi.Render.UploadMesh(faces);
            if (lines != null) lineRef = capi.Render.UploadMesh(lines);
        }

        private void DeleteMeshes()
        {
            // void DeleteMesh(MeshRef vao) — "Should always be called at the end of a meshes lifetime"
            // — api-notes §d.3 (IRenderAPI.cs:552).
            if (meshRef != null) { capi.Render.DeleteMesh(meshRef); meshRef = null; }
            if (lineRef != null) { capi.Render.DeleteMesh(lineRef); lineRef = null; }
        }

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
        private void BuildMeshes(IReadOnlyList<GhostCell> cells, out MeshData faces, out MeshData? lines)
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

            lines = null;
            if (!gridLines) return;

            // Grid: darker, more opaque than the face it lies on, so it reads as the seam between two
            // blocks. Two vertices per segment; EnumDrawMode.Lines (api-notes §l.3/§o.1, the hologram's
            // own edge mesh uses the same path).
            MeshData grid = new MeshData(quads.Count * 8, quads.Count * 8, withNormals: false, withUv: false, withRgba: true, withFlags: false);
            grid.mode = EnumDrawMode.Lines;
            foreach (FaceQuad q in quads)
            {
                int c = GridColor(q.Color);
                GhostMesher.GridLines(q, Inset, (x0, y0, z0, x1, y1, z1) =>
                {
                    int v = grid.VerticesCount;
                    grid.AddVertexSkipTex(x0, y0, z0, c);
                    grid.AddVertexSkipTex(x1, y1, z1, c);
                    grid.AddIndex(v);
                    grid.AddIndex(v + 1);
                });
            }
            lines = grid;
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

            // void RenderMesh(MeshRef meshRef) — api-notes §d.3 (IRenderAPI.cs:560).
            capi.Render.RenderMesh(meshRef);
            // The grid lines share the shader and the transform; RenderMesh draws GL_LINES for a Lines VAO.
            if (lineRef != null) capi.Render.RenderMesh(lineRef);

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
