using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ShapeProjector
{
    /// <summary>
    /// Mod entry point. Registers the block class and block entity class on both sides, loads the
    /// mod config (spec §7), and — client side (step 6) — owns the session-only hide-hotkey state
    /// (spec §6) and the shared see-through shader program (spec §6 seeThroughDepth; api-notes §f.4)
    /// that every ProjectorRenderer consults.
    /// Base type: Vintagestory.API.Common.ModSystem — api-notes.md §b (ModSystem.cs:12).
    /// </summary>
    public class ShapeProjectorModSystem : ModSystem
    {
        /// <summary>This side's ModConfig/shapeprojector.json (not synced between sides — api-notes §d.11).</summary>
        public ProjectorConfig Config { get; private set; } = new ProjectorConfig();

        /// <summary>
        /// Client hide hotkey state (spec §6 "hide all projections locally without affecting other
        /// players"): session-only, never persisted, local player only. Renderers read it per frame.
        /// </summary>
        public bool ProjectionsHidden { get; private set; }

        /// <summary>
        /// The compiled see-through program (api-notes §f.4), or null while unavailable: before
        /// BlockTexturesLoaded, after a failed compile, or with config seeThroughMode "off".
        /// Renderers simply skip the AfterBlit pass while this is null — no fallback wallhack (spec §9).
        /// </summary>
        public IShaderProgram? SeeThroughShader { get; private set; }

        private ICoreClientAPI? capi;

        // Start(ICoreAPI) is "called on both server and client ... Typically also used ... to
        // register the classes for your blocks ... blockentities ... prior to loading assets"
        // — api-notes §b (ModSystem.cs:74, doc 68-71).
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            Config = ProjectorConfig.Load(api);

            // void RegisterBlockClass(string className, Type blockType) — api-notes §b
            // (ICoreAPICommon.cs:55; ICoreAPI : ICoreAPICommon at ICoreAPI.cs:9).
            // The string is the "class" value in assets/shapeprojector/blocktypes/projector.json
            // (binding: BlockType.CreateBlock, api-notes §b).
            api.RegisterBlockClass("BlockShapeProjector", typeof(BlockShapeProjector));

            // void RegisterBlockEntityClass(string className, Type blockentityType) — api-notes §b
            // (ICoreAPICommon.cs:69). The string is the "entityClass" value in projector.json.
            api.RegisterBlockEntityClass("BEShapeProjector", typeof(BEShapeProjector));

            // §11 compat-gate markers: exact-count server Notification lines pinned by
            // tools/compat-test.ps1 — logged once each at registration, server side only.
            // void Notification(string message) — api-notes §k.1 (ILogger.cs:110); routes to
            // server-main.log (ServerLogger.getLogFile default case, §k.1). Start() runs during
            // LoadAssets, before the "Dedicated Server now running" line — §k.2.
            if (api.Side == EnumAppSide.Server)
            {
                api.Logger.Notification("[shapeprojector] Registered block class BlockShapeProjector");
                api.Logger.Notification("[shapeprojector] Registered block entity class BEShapeProjector");
            }
        }

        // Server-side recipe-load marker (§11). AssetsFinalize runs AFTER vanilla RecipeLoader has
        // populated the grid-recipe registry (RecipeLoader registers in AssetsLoaded at ExecuteOrder
        // 1.0; our AssetsLoaded at default 0.1 would run BEFORE it) and still before the server's
        // ready line — api-notes §k.3. The line logs the ACTUAL count rather than asserting 1:
        // today's unnamed wildcards register as one recipe, but a future named wildcard multiplies
        // the count and the pinned harness marker is where that surfaces deliberately (§k.5).
        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            if (api.Side != EnumAppSide.Server) return;

            // List<GridRecipe> IWorldAccessor.GridRecipes (IWorldAccessor.cs:172); RecipeBase.Name is
            // the recipe JSON's asset location, so Domain identifies ours — api-notes §k.4.
            int n = api.World.GridRecipes.Count(r => r.Name?.Domain == "shapeprojector");
            api.Logger.Notification("[shapeprojector] Loaded grid recipes: " + n);
        }

        // StartClientSide — api-notes §b (ModSystem.cs:102). Registering a hotkey here is the vanilla
        // pattern: ModJournal.StartClientSide → RegisterHotKey + SetHotKeyHandler (ModJournal.cs:44-48,
        // read this session; api-notes §d.10).
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            capi = api;

            // void RegisterHotKey(string hotkeyCode, string name, GlKeys key, HotkeyType type = CharacterControls, ...)
            // — api-notes §d.10 (IInputAPI.cs:108); SetHotKeyHandler(string, ActionConsumable<KeyCombination>) (127).
            // Key from config clientHideHotkey (spec §7, default "O"; GlKeys.O = 97, GlKeys.cs:119),
            // parsed by enum name with a fallback to O for unparseable values.
            if (!Enum.TryParse(Config.clientHideHotkey, ignoreCase: true, out GlKeys key)) key = GlKeys.O;
            api.Input.RegisterHotKey("shapeprojectorhide", Lang.Get("shapeprojector:hotkey-toggleprojections"), key, HotkeyType.CharacterControls);
            api.Input.SetHotKeyHandler("shapeprojectorhide", OnToggleHide);

            // See-through shader (spec §6 seeThroughDepth; full cited recipe api-notes §f.4).
            // Registered once assets exist: event Action BlockTexturesLoaded — "Fired when server assets
            // were received and all texture atlases have been created" (IClientEventAPI.cs:118-121,
            // verified this session — api-notes "Renderer additions"). Re-created on every shader reload:
            // event ActionBoolReturn ReloadShader (IClientEventAPI.cs:126; §f.1 — ShaderRegistry.ReloadShaders
            // wipes mod programs, so the handler must rebuild ours). Vanilla precedent RiftRenderer.cs:45-57.
            if (Config.seeThroughMode != "off")
            {
                api.Event.BlockTexturesLoaded += OnTexturesLoaded;
                api.Event.ReloadShader += LoadSeeThroughShader;
            }
        }

        private void OnTexturesLoaded()
        {
            LoadSeeThroughShader();
        }

        private bool OnToggleHide(KeyCombination comb)
        {
            ProjectionsHidden = !ProjectionsHidden;
            // void ShowChatMessage(string) — "Shows a client side only chat message" (ICoreClientAPI.cs:199-202,
            // verified this session). Lang keys are the Curator's msg-projections-hidden / msg-projections-shown.
            capi?.ShowChatMessage(Lang.Get(ProjectionsHidden
                ? "shapeprojector:msg-projections-hidden"
                : "shapeprojector:msg-projections-shown"));
            return true;
        }

        /// <summary>
        /// (Re)creates, registers and compiles the see-through program — exactly RiftRenderer.LoadShader
        /// (RiftRenderer.cs:50-57): NewShaderProgram / NewShader (IShaderAPI.cs:12,18), VertexShader /
        /// FragmentShader setters (IShaderProgram.cs:34,39; EnumShaderType.VertexShader = 35633 /
        /// FragmentShader = 35632, EnumShaderType.cs:5-6), AssetDomain = "shapeprojector"
        /// (IShaderProgram.cs:14) so the files resolve to assets/shapeprojector/shaders/projectorghost.{vsh,fsh}
        /// (§f.1 lookup rule), RegisterFileShaderProgram (IShaderAPI.cs:27, which loads the files but does
        /// NOT compile — §f.1), then Compile(). A failed compile logs one warning and leaves
        /// SeeThroughShader null; the outline then renders depth-tested only.
        /// </summary>
        public bool LoadSeeThroughShader()
        {
            if (capi == null) return true;
            SeeThroughShader = null;

            IShaderProgram prog = capi.Shader.NewShaderProgram();
            prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
            prog.AssetDomain = "shapeprojector";
            capi.Shader.RegisterFileShaderProgram("projectorghost", prog);
            bool ok = prog.Compile();
            if (ok)
            {
                SeeThroughShader = prog;
            }
            else
            {
                capi.Logger.Warning("[shapeprojector] projectorghost shader failed to compile — the through-terrain reveal is disabled this session (config seeThroughMode \"off\" silences this warning).");
            }
            return ok;
        }
    }
}
