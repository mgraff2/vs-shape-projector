using System;
using System.Globalization;
using ShapeProjector.Geometry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ShapeProjector
{
    /// <summary>
    /// Right-click configuration dialog (spec §2, §4, §5, §8 steps 3-4): centre offset, the ordered layer
    /// list with Add / Remove / Duplicate / Move up / Move down, and the selected layer's shape, parameters,
    /// Y offset, colour and enabled switch.
    ///
    /// Modelled on vanilla GuiDialogBlockEntityTicker — api-notes.md §h.3 (GuiDialogBlockEntityTicker.cs:13-66);
    /// every composer call is cited in §h.2. GuiComposer dialogs are static, so whenever the selected layer,
    /// the layer list or the selected layer's shape changes the whole dialog is re-composed — vanilla
    /// precedent: GuiDialogEditAction.onSelectionChanged (a dropdown handler) calls Compose() directly
    /// (GuiDialogEditAction.cs:77-83).
    ///
    /// The dialog never writes the block entity directly: Apply sends the edited parameters to the server
    /// (§h.4); the server validates and resyncs (§c.4), and the BE calls <see cref="RefreshFrom"/> when the
    /// new state arrives.
    /// </summary>
    public class GuiDialogProjector : GuiDialogBlockEntity   // api-notes §h.1 (GuiDialogBlockEntity.cs:10)
    {
        private const string KeyCenter = "center";
        private const string KeyDx = "dx";
        private const string KeyDz = "dz";
        private const string KeyLayer = "layer";
        private const string KeyShape = "shape";
        private const string KeyYOffset = "yoffset";
        private const string KeyColor = "color";
        private const string KeyEnabled = "enabled";
        private const string KeyEffective = "effective";
        private const string KeyDirection = "direction";
        private const string KeyVMode = "vmode";
        private const string KeyFluid = "fluidsurface";
        private const string KeyFeedback = "buildfeedback";
        // Per-shape numeric inputs: key = "p:" + field name.
        private const string P = "p:";

        private const string KeyProjEnabled = "projenabled";
        private const string KeyHolo = "holoenabled";
        // 2026-09-07 user requests: world-marks switch, ghost opacity, surroundings model, fill up to level.
        private const string KeyWorldMarks = "worldmarks";
        private const string KeyHoloFigures = "holofigures";
        // Mark style + the live marks-cost line (user request 2026-09-08: "show red where
        // configurations exceed parameters").
        private const string KeyStyle = "drawstyle";
        private const string KeyFrozen = "frozen";
        private const string KeyCost = "markcost";
        private static readonly CairoFont CostFont = CairoFont.WhiteDetailText();
        private static readonly CairoFont CostFontRed = CairoFont.WhiteDetailText().WithColor(new double[] { 1.0, 0.35, 0.35, 1.0 });
        /// <summary>Per-layer rasterizer cache for the cost line: LayerGeometry re-rasterizes only when its value-equal parameters change (LayerGeometry.cs:35-58), so typing costs nothing until a value actually changes.</summary>
        private readonly List<LayerGeometry> costGeoms = new List<LayerGeometry>();
        private const string KeyOpacity = "opacity";
        private const string KeyTerrain = "terrainmap";
        private const string KeyTerrainRadius = "terrainradius";
        private const string KeyTerrainHeight = "terrainheight";
        private const string KeyFill = "filltolevel";
        // Colour picker (user request 2026-09-07): a dropdown whose every entry shows its colour AND
        // its hex code, plus a hex field with a live preview square, for the selected layer. (First
        // cut was the vanilla swatch grid; it lays itself out past the group inset, so the user asked
        // for the dropdown. The surroundings model had land/water pickers for an hour; it wears the
        // real block colours now and has none.)
        private const string KeyColorPicker = "colorpick";
        private const string KeyColorHex = "colorhex";
        private const string KeyColorPreview = "colorprev";
        /// <summary>Re-entrancy guard: a dropdown pick writes the hex field, whose change handler re-selects the dropdown.</summary>
        private bool syncingColor;

        /// <summary>
        /// Dropdown entry for a colour: four squares drawn in the colour itself, then its hex code.
        /// The dropdown's rows are GuiElementRichtext (GuiElementListMenu.cs:191 SetNewTextWithoutRecompose
        /// → VtmlUtil.Richtextify), so the vanilla &lt;font color="#hex"&gt; VTML tag paints the squares —
        /// the same tag vanilla's own UI strings use (game/lang/en.json). A colour is SEEN, never
        /// just named or coded (user, 2026-09-07).
        /// </summary>
        private static string ColorEntryName(int rgb, bool custom)
        {
            string hex = GhostPalette.ToHex(rgb);
            return "<font color=\"" + hex + "\">" + "■■■■" + "</font>  " + hex + (custom ? " " + Lang.Get("shapeprojector:gui-color-custom") : "");
        }

        /// <summary>
        /// The dropdown's list for a current colour: every swatch, with the current colour prepended
        /// as a "custom" entry when it is not one of them (typed in the hex field). Values are hex codes.
        /// </summary>
        private static (string[] values, string[] names, int index) ColorList(int rgb)
        {
            int at = GhostPalette.SwatchIndexOf(rgb);
            int extra = at < 0 ? 1 : 0;
            string[] values = new string[GhostPalette.Swatches.Length + extra];
            string[] names = new string[values.Length];
            if (extra == 1)
            {
                values[0] = GhostPalette.ToHex(rgb);
                names[0] = ColorEntryName(rgb, custom: true);
            }
            for (int i = 0; i < GhostPalette.Swatches.Length; i++)
            {
                values[i + extra] = GhostPalette.ToHex(GhostPalette.Swatches[i]);
                names[i + extra] = ColorEntryName(GhostPalette.Swatches[i], custom: false);
            }
            return (values, names, at < 0 ? 0 : at);
        }
        private const string KeyThickness = "thickness";
        private const string KeyHeight = "height";
        private const string KeyAdjust = "adjustreport";
        private const string KeyPresetList = "presetlist";
        private const string KeyPresetName = "presetname";

        private readonly BEShapeProjector be;
        private ProjectorParams edit;
        private int selected;
        private bool composing;

        // Presets (spec §4a): client-side per-player store, loaded once per dialog (ProjectorPresets.cs,
        // api-notes §d.11). selectedPreset survives recomposes.
        private readonly ProjectorPresets presets;
        private string selectedPreset = "";

        /// <summary>Last Global-radius report (spec §10d: clamps/skips are reported visibly). Survives recomposes.</summary>
        private string lastAdjustReport = "";


        // GuiDialogGeneric already returns null; vanilla ticker overrides it the same way (GuiDialogBlockEntityTicker.cs:11).
        public override string ToggleKeyCombinationCode => null!;

        // ctor (string dialogTitle, BlockPos blockEntityPos, ICoreClientAPI capi) — api-notes §h.1 (GuiDialogBlockEntity.cs:61).
        // Lang.Get(string key, params object[] args) — Lang.cs:182; keys with an explicit "shapeprojector:" domain
        // are used as-is (TranslationService.KeyWithDomain, api-notes §e.6).
        public GuiDialogProjector(BEShapeProjector be, ICoreClientAPI capi)
            : base(Lang.Get("shapeprojector:gui-title"), be.Pos, capi)
        {
            this.be = be;
            edit = be.Params.Clone();
            if (edit.Layers.Count == 0) edit.Layers.Add(NewLayer());
            selected = 0;
            presets = ProjectorPresets.Load(capi);
            if (presets.SkippedCount > 0)
            {
                // Spec §10b: malformed entries are skipped WITH a report, never a crash. The raw
                // entries stay in the file (ProjectorPresets.cs) so nothing is silently lost.
                capi.ShowChatMessage(Lang.Get("shapeprojector:chat-preset-skipped", presets.SkippedCount));
            }
            ComposeDialog();
        }

        private static readonly string[] IconNames =
        {
            "layer-add", "layer-remove", "layer-duplicate", "layer-moveup", "layer-movedown",
            "layer-addup", "layer-addout", "radius-plus", "radius-minus",
        };

        /// <summary>
        /// §10e toolbar icons: CustomIcons + SvgIconSource — ledger §p.2 (IconUtil.cs:13-33). Called at
        /// the top of EVERY compose and driven by the dictionary's own contents, never by a
        /// process-wide "already did it" flag.
        ///
        /// Why: <c>capi.Gui.Icons</c> is per-ClientMain — <c>GuiAPI</c> builds a fresh
        /// <c>IconUtil</c> (with a fresh, empty CustomIcons) on every world join (GuiAPI.cs:44).
        /// A static flag therefore registered the icons into the FIRST world's dictionary and then
        /// skipped every world after it, and the failure is silent: IconUtil.DrawIconInt falls
        /// through to its built-in name switch for an unknown key and simply draws nothing
        /// (IconUtil.cs:122-130) — no exception, no log line, just blank buttons. Vanilla guards the
        /// same way, re-registering whenever its key is missing (GuiDialogCharacter.cs:82-86).
        ///
        /// The SVGs are the Curator's (assets/shapeprojector/textures/icons/*.svg, tintable).
        /// </summary>
        private void RegisterIcons()
        {
            IconUtil icons = capi.Gui.Icons;
            foreach (string name in IconNames)
            {
                string key = "sp-" + name;
                if (icons.CustomIcons.ContainsKey(key)) continue;

                // SvgIconSource(AssetLocation) resolves the asset immediately and CAPTURES it; when the
                // lookup misses it hands back a delegate that draws nothing, for ever (IconUtil.cs:15-32).
                // So resolve it here and say so on a miss rather than shipping an invisible button.
                AssetLocation loc = new AssetLocation("shapeprojector", "textures/icons/" + name + ".svg");
                IAsset? svg;
                try
                {
                    svg = capi.Assets.TryGet(loc);
                }
                catch (Exception e)
                {
                    // TryGet is not exception-free: FolderOrigin.TryLoadAsset reads the file from the
                    // game's unpack cache and throws if that folder is gone under a running client
                    // (2026-09-08 crash report: DirectoryNotFoundException on layer-add.svg). A blank
                    // button beats a dead game.
                    capi.Logger.Warning("[shapeprojector] Toolbar icon " + loc + " could not be read (" + e.GetType().Name + "); its button will draw blank until the next world join.");
                    svg = null;
                }
                if (svg == null)
                {
                    capi.Logger.Warning("[shapeprojector] Toolbar icon " + loc + " could not be loaded; its button would draw blank.");
                    continue;   // left unregistered on purpose: the next compose tries again
                }
                icons.CustomIcons[key] = icons.SvgIconSource(svg);
            }
        }

        // ------------------------------------------------------------------ §10e composition
        // Two columns of titled groups (title AddStaticText + AddInset(…, 3, 0.85f) — ledger §p.4,
        // precedent GuiDialogActivity.cs:141): Projector + Presets left; Layers toolbar + Layer
        // settings right; Apply/Close bottom-right. Fixed-position cursor layout throughout — every
        // bounds is ElementBounds.Fixed with a running y, so group/inset heights are exact and the
        // FixedUnder-sees-declared-height-0 trap cannot recur.
        private void ComposeDialog()
        {
            composing = true;
            // Before anything is composed: a world rejoin swaps in an empty icon dictionary (see
            // RegisterIcons), and an icon button whose key is missing draws blank without complaint.
            RegisterIcons();
            selected = Math.Clamp(selected, 0, edit.Layers.Count - 1);
            // §10c hologram contract: the open dialog publishes its selection for selected-layer
            // brightening and holoMode "guiOpen" (BEShapeProjector.GuiSelectedLayer; cleared on close).
            be.GuiSelectedLayer = selected;
            LayerParams layer = edit.Layers[selected];

            const double rowH = 30, gap = 8, titleH = 26, pad = 6, labelW = 150;
            const double colLx = 0, colLw = 300, colRx = 336, colRw = 330;

            bool atCap = edit.Layers.Count >= be.Config.maxLayersPerProjector;
            bool drape = layer.VerticalMode == VerticalMode.Drape;
            // Fill up to level (2026-09-07) resolves the ground in BOTH vertical modes, so the fluid
            // rule row appears whenever it is on, next to the build-feedback row Fixed Y already has.
            bool fill = layer.FillToLevel;
            // §10d: Out is disabled where "out" has no honest meaning — the Geometer's IsAdjustable
            // (false exactly for spirals) drives it.
            bool outAdjustable = layer.ToShapeSpec() is ShapeSpec radialSpec && RadialAdjust.IsAdjustable(radialSpec);

            // Dropdown data (unchanged from the pre-§10e dialog).
            string[] layerValues = new string[edit.Layers.Count];
            string[] layerNames = new string[edit.Layers.Count];
            for (int i = 0; i < edit.Layers.Count; i++)
            {
                layerValues[i] = i.ToString(CultureInfo.InvariantCulture);
                layerNames[i] = LayerName(i, edit.Layers[i]);
            }
            // §10a: "Triangle" is a pure dropdown alias for regular polygon n=3 (Triangle.cs) — same
            // ShapeType, same rasterizer; any 3-sided polygon displays as Triangle.
            string[] shapeValues = { "circle", "ring", "ellipse", "rectangle", "polygon", "triangle", "spiral" };
            string[] shapeNames = new string[shapeValues.Length];
            for (int i = 0; i < shapeValues.Length; i++) shapeNames[i] = Lang.Get("shapeprojector:gui-shape-" + shapeValues[i]);
            string[] presetValues = presets.SortedNames();
            bool anyPresets = presetValues.Length > 0;
            string[] presetNames;
            int presetIndex = 0;
            if (anyPresets)
            {
                presetNames = (string[])presetValues.Clone();
                int found = Array.IndexOf(presetValues, selectedPreset);
                presetIndex = found >= 0 ? found : 0;
                selectedPreset = presetValues[presetIndex];
            }
            else
            {
                presetValues = new[] { "" };
                presetNames = new[] { Lang.Get("shapeprojector:gui-preset-none") };
                selectedPreset = "";
            }
            string[] vmodeValues = { "fixed", "drape" };
            string[] vmodeNames = { Lang.Get("shapeprojector:gui-verticalmode-fixed"), Lang.Get("shapeprojector:gui-verticalmode-drape") };

            ElementBounds bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bg.BothSizing = ElementSizing.FitToChildren;
            ElementBounds dialog = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

            SingleComposer?.Dispose();
            GuiComposer c = capi.Gui
                .CreateCompo("shapeprojector-config", dialog)
                .AddShadedDialogBG(bg, true)
                .AddDialogTitleBar(Lang.Get("shapeprojector:gui-title"), OnTitleBarClose)
                .BeginChildElements(bg);
            pendingInputs.Clear();

            // Group scaffolding: title, then inset sized to the exact content height (§p.4).
            double Group(string titleKey, double x, double w, double y, double contentH)
            {
                c.AddStaticText(Lang.Get(titleKey), CairoFont.WhiteDetailText(), ElementBounds.Fixed(x, y, w, titleH));
                c.AddInset(ElementBounds.Fixed(x, y + titleH, w, contentH + 2 * pad), 3, 0.85f);
                return y + titleH + pad;   // first content row's y
            }
            // A label + right-aligned input pair inside a group column.
            ElementBounds L(double x, double y) => ElementBounds.Fixed(x + pad, y, labelW, rowH);
            ElementBounds I(double x, double w, double y) => ElementBounds.Fixed(x + pad + labelW, y, w - 2 * pad - labelW, rowH);
            // The whole label+input row, for a hover area that covers the label as well as the field.
            ElementBounds Rowb(double x, double w, double y) => ElementBounds.Fixed(x + pad, y, w - 2 * pad, rowH);

            // Tooltips (spec §10e "compact icon buttons with tooltips", extended to every control on
            // user request 2026-09-02). AddHoverText on a copy of the element's own bounds AFTER the
            // element — ledger §p.5 (GuiDialogTrader.cs:363). GuiElementHoverText.OnMouseDownOnElement is
            // empty and never sets Handled (GuiElementHoverText.cs:273-275), so a hover laid over a
            // number input or a switch is click-transparent and cannot steal focus or typing.
            // 320px + CairoFont.WhiteDetailText is vanilla's own multi-line hover size (GuiScreenMods'
            // "Title\r\nBody" list hovers); the element wraps to that width and auto-heights, and a
            // "\n" in the text starts a new line.
            const int tipW = 320;
            // Body only (user, 2026-09-08: "get rid of all the titles from the tooltips" — the field's own
            // label already names it). Icon buttons have no label, so IconBtn keeps the name line.
            void Tip(ElementBounds b, string titleKey, string body) =>
                c.AddHoverText(body, CairoFont.WhiteDetailText(), tipW, b);

            // Colour picker block (user request 2026-09-07: colours must be SEEN, never just named or
            // coded): a label + dropdown whose entries each show their colour and hex code (ColorList),
            // then a "Hex code" row whose text field is for exact values and whose square beside it
            // always shows the current colour — typed or picked — as a colour. Two rows; stays inside
            // the group inset like every other row. Returns the y after the block.
            // AddDropDown(values, names, selectedIndex, SelectionChangedDelegate, bounds, key) — §h.2;
            // AddDynamicCustomDraw(bounds, DrawDelegateWithBounds, key) + GuiElementCustomDraw.Redraw()
            // for the preview square (GuiComposerHelpers.cs:1300, 1309).
            double ColorBlock(double x, double w, double y, string labelKey, string tipKey, string pickerKey, string hexKey, string previewKey,
                              SelectionChangedDelegate onPick, Action<string> onHex, System.Func<int> current)
            {
                var (cvalues, cnames, cindex) = ColorList(current());
                c.AddStaticText(Lang.Get(labelKey), CairoFont.WhiteSmallText(), L(x, y))
                 .AddDropDown(cvalues, cnames, cindex, onPick, I(x, w, y), pickerKey);
                Tip(Rowb(x, w, y), labelKey, Lang.Get(tipKey));
                y += rowH + gap;
                c.AddStaticText(Lang.Get("shapeprojector:gui-color-hex"), CairoFont.WhiteSmallText(), L(x, y));
                c.AddTextInput(ElementBounds.Fixed(x + pad + labelW, y, w - 2 * pad - labelW - rowH - 6, rowH), onHex, CairoFont.WhiteDetailText(), hexKey);
                c.AddDynamicCustomDraw(ElementBounds.Fixed(x + w - pad - rowH, y, rowH, rowH), (ctx, surface, b) =>
                {
                    int rgb = current();
                    ctx.SetSourceRGBA(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0, 1.0);
                    GuiElement.RoundRectangle(ctx, b.drawX, b.drawY, b.InnerWidth, b.InnerHeight, 3.0);
                    ctx.Fill();
                }, previewKey);
                Tip(Rowb(x, w, y), "shapeprojector:gui-color-hex", Lang.Get("shapeprojector:gui-tip-color-hex"));
                return y + rowH + gap;
            }

            // ================= LEFT column =================
            double yL = 30;

            // --- Projector (spec §10e: on/off, resolved center, offsets)
            // 13 plain rows: on, hologram, figures, world marks, opacity, style, freeze, model, radius, height, centre, dx, dz.
            double cy = Group("shapeprojector:gui-group-projector", colLx, colLw, yL, 13 * rowH + 12 * gap);
            c.AddStaticText(Lang.Get("shapeprojector:gui-projector-enabled"), CairoFont.WhiteSmallText(), L(colLx, cy))
             .AddSwitch(null, ElementBounds.Fixed(colLx + pad + labelW, cy, rowH, rowH), KeyProjEnabled, 25, 3);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-projector-enabled", Lang.Get("shapeprojector:gui-tip-projenabled"));
            cy += rowH + gap;
            // Hologram switch (user request 2026-09-02): independent of the master switch above.
            c.AddStaticText(Lang.Get("shapeprojector:gui-hologram-enabled"), CairoFont.WhiteSmallText(), L(colLx, cy))
             .AddSwitch(null, ElementBounds.Fixed(colLx + pad + labelW, cy, rowH, rowH), KeyHolo, 25, 3);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-hologram-enabled", Lang.Get("shapeprojector:gui-tip-holoenabled"));
            cy += rowH + gap;
            c.AddStaticText(Lang.Get("shapeprojector:gui-hologram-figures"), CairoFont.WhiteSmallText(), L(colLx, cy))
             .AddSwitch(null, ElementBounds.Fixed(colLx + pad + labelW, cy, rowH, rowH), KeyHoloFigures, 25, 3);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-hologram-figures", Lang.Get("shapeprojector:gui-tip-holofigures"));
            cy += rowH + gap;
            // --- 2026-09-07: world marks on/off (hologram-only mode), ghost opacity, surroundings model.
            c.AddStaticText(Lang.Get("shapeprojector:gui-worldmarks-enabled"), CairoFont.WhiteSmallText(), L(colLx, cy))
             .AddSwitch(OnWorldMarksToggled, ElementBounds.Fixed(colLx + pad + labelW, cy, rowH, rowH), KeyWorldMarks, 25, 3);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-worldmarks-enabled", Lang.Get("shapeprojector:gui-tip-worldmarks"));
            cy += rowH + gap;
            c.AddStaticText(Lang.Get("shapeprojector:gui-opacity"), CairoFont.WhiteSmallText(), L(colLx, cy))
             .AddNumberInput(I(colLx, colLw, cy), OnOffsetChanged, CairoFont.WhiteDetailText(), KeyOpacity);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-opacity", Lang.Get("shapeprojector:gui-tip-opacity", ProjectorParams.MinOpacityPercent, GhostPalette.DefaultOpacityPercent));
            cy += rowH + gap;
            string[] styleValues = { "faces", "blocks" };
            string[] styleNames = { Lang.Get("shapeprojector:gui-style-faces"), Lang.Get("shapeprojector:gui-style-blocks") };
            c.AddStaticText(Lang.Get("shapeprojector:gui-drawstyle"), CairoFont.WhiteSmallText(), L(colLx, cy))
             .AddDropDown(styleValues, styleNames, edit.Style == DrawStyle.Blocks ? 1 : 0, OnStyleChanged, I(colLx, colLw, cy), KeyStyle);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-drawstyle", Lang.Get("shapeprojector:gui-tip-drawstyle", be.Config.maxCellsPerProjector, be.Config.maxCubesPerProjector));
            cy += rowH + gap;
            c.AddStaticText(Lang.Get("shapeprojector:gui-frozen"), CairoFont.WhiteSmallText(), L(colLx, cy))
             .AddSwitch(null, ElementBounds.Fixed(colLx + pad + labelW, cy, rowH, rowH), KeyFrozen, 25, 3);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-frozen", Lang.Get("shapeprojector:gui-tip-frozen"));
            cy += rowH + gap;
            c.AddStaticText(Lang.Get("shapeprojector:gui-terrainmap"), CairoFont.WhiteSmallText(), L(colLx, cy))
             .AddSwitch(null, ElementBounds.Fixed(colLx + pad + labelW, cy, rowH, rowH), KeyTerrain, 25, 3);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-terrainmap", Lang.Get("shapeprojector:gui-tip-terrainmap"));
            cy += rowH + gap;
            c.AddStaticText(Lang.Get("shapeprojector:gui-terrainmap-radius"), CairoFont.WhiteSmallText(), L(colLx, cy))
             .AddNumberInput(I(colLx, colLw, cy), OnOffsetChanged, CairoFont.WhiteDetailText(), KeyTerrainRadius);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-terrainmap-radius", Lang.Get("shapeprojector:gui-tip-terrainmap-radius", be.Config.maxTerrainMapRadius));
            cy += rowH + gap;
            c.AddStaticText(Lang.Get("shapeprojector:gui-terrainmap-height"), CairoFont.WhiteSmallText(), L(colLx, cy))
             .AddNumberInput(I(colLx, colLw, cy), OnOffsetChanged, CairoFont.WhiteDetailText(), KeyTerrainHeight);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-terrainmap-height", Lang.Get("shapeprojector:gui-tip-terrainmap-height", be.Config.maxTerrainMapHeight));
            cy += rowH + gap;
            c.AddDynamicText(ResolvedCenterText(edit), CairoFont.WhiteSmallText(), ElementBounds.Fixed(colLx + pad, cy, colLw - 2 * pad, rowH), KeyCenter);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-center", Lang.Get("shapeprojector:gui-tip-center"));
            cy += rowH + gap;
            c.AddStaticText(Lang.Get("shapeprojector:gui-offset-x"), CairoFont.WhiteSmallText(), L(colLx, cy))
             .AddNumberInput(I(colLx, colLw, cy), OnOffsetChanged, CairoFont.WhiteDetailText(), KeyDx);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-offset-x", Lang.Get("shapeprojector:gui-tip-offsetx"));
            cy += rowH + gap;
            c.AddStaticText(Lang.Get("shapeprojector:gui-offset-z"), CairoFont.WhiteSmallText(), L(colLx, cy))
             .AddNumberInput(I(colLx, colLw, cy), OnOffsetChanged, CairoFont.WhiteDetailText(), KeyDz);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-offset-z", Lang.Get("shapeprojector:gui-tip-offsetz"));
            yL = cy + rowH + pad + gap * 2;

            // --- Presets (spec §10e / §10b)
            cy = Group("shapeprojector:gui-group-presets", colLx, colLw, yL, 4 * rowH + 3 * gap);
            c.AddDropDown(presetValues, presetNames, presetIndex, OnPresetSelected, ElementBounds.Fixed(colLx + pad, cy, colLw - 2 * pad, rowH), KeyPresetList);
            Tip(Rowb(colLx, colLw, cy), "shapeprojector:gui-presets", Lang.Get("shapeprojector:gui-tip-preset-list"));
            cy += rowH + gap;
            c.AddTextInput(ElementBounds.Fixed(colLx + pad, cy, 140, rowH), null, CairoFont.WhiteDetailText(), KeyPresetName);
            Tip(ElementBounds.Fixed(colLx + pad, cy, 140, rowH), "shapeprojector:gui-preset-name", Lang.Get("shapeprojector:gui-tip-preset-name"));
            c.AddSmallButton(Lang.Get("shapeprojector:gui-preset-save"), OnPresetSave, ElementBounds.Fixed(colLx + pad + 148, cy, 0, 0).WithFixedPadding(8, 2), EnumButtonStyle.Normal, "btnpresetsave");
            Tip(ElementBounds.Fixed(colLx + pad + 148, cy, 60, rowH), "shapeprojector:gui-preset-save", Lang.Get("shapeprojector:gui-tip-preset-save"));
            c.AddSmallButton(Lang.Get("shapeprojector:gui-preset-delete"), OnPresetDelete, ElementBounds.Fixed(colLx + pad + 214, cy, 0, 0).WithFixedPadding(8, 2), EnumButtonStyle.Normal, "btnpresetdelete");
            Tip(ElementBounds.Fixed(colLx + pad + 214, cy, 70, rowH), "shapeprojector:gui-preset-delete", Lang.Get("shapeprojector:gui-tip-preset-delete"));
            cy += rowH + gap;
            c.AddSmallButton(Lang.Get("shapeprojector:gui-preset-load-replace"), OnPresetLoadReplace, ElementBounds.Fixed(colLx + pad, cy, 0, 0).WithFixedPadding(8, 2), EnumButtonStyle.Normal, "btnpresetloadr");
            Tip(ElementBounds.Fixed(colLx + pad, cy, 130, rowH), "shapeprojector:gui-preset-load-replace", Lang.Get("shapeprojector:gui-tip-preset-loadreplace"));
            cy += rowH + gap;
            c.AddSmallButton(Lang.Get("shapeprojector:gui-preset-load-append"), OnPresetLoadAppend, ElementBounds.Fixed(colLx + pad, cy, 0, 0).WithFixedPadding(8, 2), EnumButtonStyle.Normal, "btnpresetloada");
            Tip(ElementBounds.Fixed(colLx + pad, cy, 130, rowH), "shapeprojector:gui-preset-load-append", Lang.Get("shapeprojector:gui-tip-preset-loadappend", be.Config.maxLayersPerProjector));
            yL = cy + rowH + pad + gap * 2;

            // ================= RIGHT column =================
            double yR = 30;

            // --- Layers: selector + icon toolbar + report (spec §10e "compact icon buttons with tooltips")
            // Report line gets two rows in the detail font: a budget report names a layer and a cut,
            // and one row of the small font truncated it (user, 2026-09-07).
            cy = Group("shapeprojector:gui-group-layers", colRx, colRw, yR, rowH + gap + 32 + gap + 2 * rowH);
            c.AddDropDown(layerValues, layerNames, selected, OnLayerSelected, ElementBounds.Fixed(colRx + pad, cy, colRw - 2 * pad, rowH), KeyLayer);
            Tip(Rowb(colRx, colRw, cy), "shapeprojector:gui-layers", Lang.Get("shapeprojector:gui-tip-layerlist"));
            cy += rowH + gap;
            // Icon buttons: AddIconButton(icon, font, Action<bool>, bounds, key) — ledger §p.1/§p.5
            // (GuiComposerHelpers.cs:501-508); momentary, fires (true) on press. The button IGNORES
            // Enabled for input and draw (§p.6), so disabled = grayed font at compose time + a guard
            // in the callback; Enabled is still set post-compose for focus semantics. Tooltip:
            // AddHoverText on the same bounds after the button (§p.5, GuiDialogTrader.cs:363).
            // Icon tint comes from Font.Color (§p.6, GuiElementToggleButton.cs:100).
            double bx = colRx + pad;
            void IconBtn(string icon, string titleKey, string body, string key, bool on, Func<bool> action)
            {
                ElementBounds b = ElementBounds.Fixed(bx, cy, 26, 26).WithFixedPadding(3, 3);
                CairoFont f = CairoFont.WhiteSmallText();
                if (!on) f.Color = new double[] { 0.45, 0.45, 0.45, 0.5 };
                c.AddIconButton("sp-" + icon, f, pressed => { if (pressed && on && !composing) action(); }, b, key);
                c.AddHoverText(Lang.Get(titleKey) + "\n" + body, CairoFont.WhiteDetailText(), tipW, b.FlatCopy());   // unlabelled: keep the name
                bx += 34;
                toolbarStates.Add((key, on));
            }
            // Toolbar tooltips state the ACTUAL arithmetic for the layer currently selected rather than
            // the general rule: the per-shape radial semantics (RadialAdjust.Adjust, spec §10d) are the
            // easiest thing in this dialog to guess wrong, so each tip shows the concrete before → after
            // it would produce, and a disabled button says why it is disabled.
            int layerNo = selected + 1;
            int count = edit.Layers.Count;
            int cap = be.Config.maxLayersPerProjector;
            string atCapTip = Lang.Get("shapeprojector:gui-tip-add-full", cap);
            string yNow = Signed(layer.YOffset);

            toolbarStates.Clear();
            IconBtn("layer-add", "shapeprojector:gui-layer-add",
                atCap ? atCapTip : Lang.Get("shapeprojector:gui-tip-add", count, count + 1, cap),
                "icoadd", !atCap, OnAddLayer);
            IconBtn("layer-remove", "shapeprojector:gui-layer-remove",
                count > 1 ? Lang.Get("shapeprojector:gui-tip-remove", layerNo, count, count - 1)
                          : Lang.Get("shapeprojector:gui-tip-remove-last"),
                "icoremove", count > 1, OnRemoveLayer);
            IconBtn("layer-duplicate", "shapeprojector:gui-layer-duplicate",
                atCap ? atCapTip : Lang.Get("shapeprojector:gui-tip-duplicate", layerNo, ShapeName(layer) + " " + layer.SizeSummary(), yNow),
                "icodup", !atCap, OnDuplicateLayer);
            IconBtn("layer-moveup", "shapeprojector:gui-layer-moveup",
                selected > 0 ? Lang.Get("shapeprojector:gui-tip-move", layerNo, layerNo - 1)
                             : Lang.Get("shapeprojector:gui-tip-move-first", layerNo),
                "icoup", selected > 0, OnMoveUp);
            IconBtn("layer-movedown", "shapeprojector:gui-layer-movedown",
                selected < count - 1 ? Lang.Get("shapeprojector:gui-tip-move", layerNo, layerNo + 1)
                                     : Lang.Get("shapeprojector:gui-tip-move-last", layerNo),
                "icodown", selected < count - 1, OnMoveDown);
            IconBtn("layer-addup", "shapeprojector:gui-layer-addup",
                atCap ? atCapTip : Lang.Get("shapeprojector:gui-tip-addup", layerNo, yNow, Signed(layer.YOffset + 1), layer.SizeSummary()),
                "icoaddup", !atCap, OnAddLayerUp);
            IconBtn("layer-addout", "shapeprojector:gui-layer-addout",
                !outAdjustable ? Lang.Get("shapeprojector:gui-tip-addout-spiral")
                    : atCap ? atCapTip
                    : Lang.Get("shapeprojector:gui-tip-addout", layerNo, RadialMathLine(layer, 1), yNow),
                "icoaddout", !atCap && outAdjustable, OnAddLayerOut);
            IconBtn("radius-plus", "shapeprojector:gui-global-plus",
                Lang.Get("shapeprojector:gui-tip-globalplus", layerNo, RadialMathLine(layer, 1), GlobalPreview(1)),
                "icorplus", true, OnGlobalPlus);
            IconBtn("radius-minus", "shapeprojector:gui-global-minus",
                Lang.Get("shapeprojector:gui-tip-globalminus", layerNo, RadialMathLine(layer, -1), GlobalPreview(-1)),
                "icorminus", true, OnGlobalMinus);
            cy += 32 + gap;
            c.AddDynamicText(lastAdjustReport, CairoFont.WhiteDetailText(), ElementBounds.Fixed(colRx + pad, cy, colRw - 2 * pad, 2 * rowH), KeyAdjust);
            yR = cy + 2 * rowH + pad + gap * 2;

            // --- Layer settings: shape + ONLY the selected shape's fields (spec §10e), then commons.
            int shapeRows = layer.Shape switch
            {
                ShapeType.Ring => 2,
                ShapeType.Ellipse => 2,
                ShapeType.Rectangle => 3,   // + effective-size line
                ShapeType.Polygon => 3,
                ShapeType.Spiral => 4,      // + direction dropdown
                _ => 1,
            };
            // shape row + shape fields + thickness/yoffset/height/vmode/fill/color/enabled + the
            // optional rows: fluid rule (Follow terrain, or any filled layer), build feedback (Fixed Y).
            int optRows = (drape || fill ? 1 : 0) + (drape ? 0 : 1);
            // The colour block is two rows (dropdown + hex), one more than the old colour dropdown.
            int settingsRows = 1 + shapeRows + 7 + optRows + 1 + 2;   // + the marks-cost line (two rows)
            cy = Group("shapeprojector:gui-group-layersettings", colRx, colRw, yR, settingsRows * rowH + (settingsRows - 1) * gap);
            c.AddStaticText(Lang.Get("shapeprojector:gui-shape"), CairoFont.WhiteSmallText(), L(colRx, cy))
             .AddDropDown(shapeValues, shapeNames, ShapeDropdownIndex(layer), OnShapeChanged, I(colRx, colRw, cy), KeyShape);
            Tip(Rowb(colRx, colRw, cy), "shapeprojector:gui-shape", Lang.Get("shapeprojector:gui-tip-shape"));
            cy += rowH + gap;

            void Row(string labelKey, string key, double interval, bool intMode, double value)
            {
                c.AddStaticText(Lang.Get(labelKey), CairoFont.WhiteSmallText(), L(colRx, cy))
                 .AddNumberInput(I(colRx, colRw, cy), OnParamChanged, CairoFont.WhiteDetailText(), P + key);
                // Every gui-param-<field> label has a gui-tip-param-<field> explanation to go with it.
                Tip(Rowb(colRx, colRw, cy), labelKey, Lang.Get(labelKey.Replace("gui-param-", "gui-tip-param-")));
                pendingInputs.Add((P + key, (float)interval, intMode, (float)value));
                cy += rowH + gap;
            }
            switch (layer.Shape)
            {
                case ShapeType.Circle:
                    Row("shapeprojector:gui-param-radius", "radius", 0.5, false, layer.Radius);
                    break;
                case ShapeType.Ring:
                    Row("shapeprojector:gui-param-innerradius", "innerRadius", 0.5, false, layer.InnerRadius);
                    Row("shapeprojector:gui-param-outerradius", "radius", 0.5, false, layer.Radius);
                    break;
                case ShapeType.Ellipse:
                    Row("shapeprojector:gui-param-radiusx", "radiusX", 0.5, false, layer.RadiusX);
                    Row("shapeprojector:gui-param-radiusz", "radiusZ", 0.5, false, layer.RadiusZ);
                    break;
                case ShapeType.Rectangle:
                    Row("shapeprojector:gui-param-width", "width", 1, true, layer.Width);
                    Row("shapeprojector:gui-param-depth", "depth", 1, true, layer.Depth);
                    // Rectangle.EffectiveSize: parity mismatch rounds a side outward by one block (Rectangle.cs:8-19).
                    c.AddDynamicText(EffectiveSizeText(edit, layer), CairoFont.WhiteSmallText(), ElementBounds.Fixed(colRx + pad, cy, colRw - 2 * pad, rowH), KeyEffective);
                    cy += rowH + gap;
                    break;
                case ShapeType.Polygon:
                    Row("shapeprojector:gui-param-sides", "sides", 1, true, layer.Sides);
                    Row("shapeprojector:gui-param-circumradius", "circumradius", 0.5, false, layer.Circumradius);
                    Row("shapeprojector:gui-param-rotation", "rotationDeg", 5, false, layer.RotationDeg);
                    break;
                case ShapeType.Spiral:
                    Row("shapeprojector:gui-param-turns", "turns", 0.25, false, layer.Turns);
                    Row("shapeprojector:gui-param-spacing", "spacing", 0.5, false, layer.Spacing);
                    Row("shapeprojector:gui-param-startradius", "startRadius", 0.5, false, layer.StartRadius);
                    c.AddStaticText(Lang.Get("shapeprojector:gui-param-direction"), CairoFont.WhiteSmallText(), L(colRx, cy))
                     .AddDropDown(new[] { "cw", "ccw" },
                                  new[] { Lang.Get("shapeprojector:gui-direction-clockwise"), Lang.Get("shapeprojector:gui-direction-counterclockwise") },
                                  layer.Clockwise ? 0 : 1, OnDirectionChanged, I(colRx, colRw, cy), KeyDirection);
                    Tip(Rowb(colRx, colRw, cy), "shapeprojector:gui-param-direction", Lang.Get("shapeprojector:gui-tip-param-direction"));
                    cy += rowH + gap;
                    break;
            }

            // Common per-layer rows. Spec §5a: YOffset is BOTH the Fixed-Y offset and the drape offset.
            // Thickness (horizontal) sits with the shape's own size fields; Height (vertical) sits
            // directly under the Y offset it extrudes upward from.
            c.AddStaticText(Lang.Get("shapeprojector:gui-thickness"), CairoFont.WhiteSmallText(), L(colRx, cy))
             .AddNumberInput(I(colRx, colRw, cy), OnParamChanged, CairoFont.WhiteDetailText(), KeyThickness);
            Tip(Rowb(colRx, colRw, cy), "shapeprojector:gui-thickness", Lang.Get("shapeprojector:gui-tip-thickness", layer.MaxThickness(be.Config)) + "\n" + Lang.Get("shapeprojector:gui-tip-budget", be.CellBudget));
            cy += rowH + gap;
            c.AddStaticText(Lang.Get("shapeprojector:gui-yoffset"), CairoFont.WhiteSmallText(), L(colRx, cy))
             .AddNumberInput(I(colRx, colRw, cy), null, CairoFont.WhiteDetailText(), KeyYOffset);
            Tip(Rowb(colRx, colRw, cy), "shapeprojector:gui-yoffset", Lang.Get("shapeprojector:gui-tip-yoffset"));
            cy += rowH + gap;
            c.AddStaticText(Lang.Get("shapeprojector:gui-height"), CairoFont.WhiteSmallText(), L(colRx, cy))
             .AddNumberInput(I(colRx, colRw, cy), OnParamChanged, CairoFont.WhiteDetailText(), KeyHeight);
            Tip(Rowb(colRx, colRw, cy), "shapeprojector:gui-height", Lang.Get("shapeprojector:gui-tip-height") + "\n" + Lang.Get("shapeprojector:gui-tip-budget", be.CellBudget));
            cy += rowH + gap;
            c.AddStaticText(Lang.Get("shapeprojector:gui-verticalmode"), CairoFont.WhiteSmallText(), L(colRx, cy))
             .AddDropDown(vmodeValues, vmodeNames, drape ? 1 : 0, OnVerticalModeChanged, I(colRx, colRw, cy), KeyVMode);
            Tip(Rowb(colRx, colRw, cy), "shapeprojector:gui-verticalmode", Lang.Get("shapeprojector:gui-tip-verticalmode"));
            cy += rowH + gap;
            // Fill up to level (user request 2026-09-07). Toggling recomposes: the fluid-rule row
            // depends on it.
            c.AddStaticText(Lang.Get("shapeprojector:gui-fill"), CairoFont.WhiteSmallText(), L(colRx, cy))
             .AddSwitch(OnFillToggled, ElementBounds.Fixed(colRx + pad + labelW, cy, rowH, rowH), KeyFill, 25, 3);
            Tip(Rowb(colRx, colRw, cy), "shapeprojector:gui-fill", Lang.Get(drape ? "shapeprojector:gui-tip-fill-drape" : "shapeprojector:gui-tip-fill-fixed") + "\n" + Lang.Get("shapeprojector:gui-tip-budget", be.CellBudget));
            cy += rowH + gap;
            // Optional rows: fluid rule where the ground is resolved (Follow terrain, spec §5a — and
            // any filled layer, 2026-09-07); per-layer build feedback in Fixed Y (spec §6 "Optional per-layer").
            if (drape || fill)
            {
                c.AddStaticText(Lang.Get("shapeprojector:gui-treatfluidassurface"), CairoFont.WhiteSmallText(), L(colRx, cy))
                 .AddSwitch(null, ElementBounds.Fixed(colRx + pad + labelW, cy, rowH, rowH), KeyFluid, 25, 3);
                Tip(Rowb(colRx, colRw, cy), "shapeprojector:gui-treatfluidassurface", Lang.Get(drape ? "shapeprojector:gui-tip-fluid" : "shapeprojector:gui-tip-fluid-fill"));
                cy += rowH + gap;
            }
            if (!drape)
            {
                c.AddStaticText(Lang.Get("shapeprojector:gui-buildfeedback"), CairoFont.WhiteSmallText(), L(colRx, cy))
                 .AddSwitch(null, ElementBounds.Fixed(colRx + pad + labelW, cy, rowH, rowH), KeyFeedback, 25, 3);
                Tip(Rowb(colRx, colRw, cy), "shapeprojector:gui-buildfeedback", Lang.Get("shapeprojector:gui-tip-buildfeedback"));
                cy += rowH + gap;
            }
            cy = ColorBlock(colRx, colRw, cy, "shapeprojector:gui-color", "shapeprojector:gui-tip-color",
                            KeyColorPicker, KeyColorHex, KeyColorPreview, OnLayerPick, OnLayerHex, () => edit.Layers[selected].ResolveColor());
            c.AddStaticText(Lang.Get("shapeprojector:gui-enabled"), CairoFont.WhiteSmallText(), L(colRx, cy))
             .AddSwitch(null, ElementBounds.Fixed(colRx + pad + labelW, cy, rowH, rowH), KeyEnabled, 25, 3);
            Tip(Rowb(colRx, colRw, cy), "shapeprojector:gui-enabled", Lang.Get("shapeprojector:gui-tip-enabled"));
            cy += rowH + gap;
            // Marks-cost line: what this configuration will ask of the projector's budget, from the
            // real rasterizer, red when it exceeds the current style's budget or a field was capped.
            var (costText, costOver) = MarkCost();
            c.AddDynamicText(costText, costOver ? CostFontRed : CostFont, ElementBounds.Fixed(colRx + pad, cy, colRw - 2 * pad, 2 * rowH), KeyCost);   // two rows: it wraps
            Tip(ElementBounds.Fixed(colRx + pad, cy, colRw - 2 * pad, 2 * rowH), "shapeprojector:gui-cost-title", Lang.Get("shapeprojector:gui-tip-cost"));
            yR = cy + 2 * rowH + pad + gap * 2;

            // ================= Apply / Close, persistent bottom-right (spec §10e) =================
            double yB = Math.Max(yL, yR) + gap;
            c.AddSmallButton(Lang.Get("shapeprojector:gui-close"), OnClose, ElementBounds.Fixed(colRx + pad, yB, 0, 0).WithFixedPadding(10, 2), EnumButtonStyle.Normal)
             .AddSmallButton(Lang.Get("shapeprojector:gui-apply"), OnApply, ElementBounds.Fixed(colRx + colRw - 90, yB, 0, 0).WithFixedPadding(10, 2), EnumButtonStyle.Normal);
            Tip(ElementBounds.Fixed(colRx + pad, yB, 70, rowH), "shapeprojector:gui-close", Lang.Get("shapeprojector:gui-tip-close"));
            Tip(ElementBounds.Fixed(colRx + colRw - 90, yB, 70, rowH), "shapeprojector:gui-apply", Lang.Get("shapeprojector:gui-tip-apply"));
            c.EndChildElements()
             .Compose();

            SingleComposer = c;

            GuiElementNumberInput dx = c.GetNumberInput(KeyDx); dx.Interval = 0.5f; dx.SetValue((float)edit.Dx);
            GuiElementNumberInput dz = c.GetNumberInput(KeyDz); dz.Interval = 0.5f; dz.SetValue((float)edit.Dz);
            foreach (var (key, interval, intMode, value) in pendingInputs)
            {
                GuiElementNumberInput n = c.GetNumberInput(key);
                n.Interval = interval;
                n.IntMode = intMode;
                n.SetValue(value);
            }
            GuiElementNumberInput yoff = c.GetNumberInput(KeyYOffset); yoff.Interval = 1f; yoff.IntMode = true; yoff.SetValue((float)layer.YOffset);
            GuiElementNumberInput thick = c.GetNumberInput(KeyThickness); thick.Interval = 1f; thick.IntMode = true; thick.SetValue(layer.Thickness);
            GuiElementNumberInput hgt = c.GetNumberInput(KeyHeight); hgt.Interval = 1f; hgt.IntMode = true; hgt.SetValue(layer.Height);
            c.GetSwitch(KeyProjEnabled).On = edit.Enabled;
            c.GetSwitch(KeyHolo).On = edit.HologramEnabled;
            c.GetSwitch(KeyWorldMarks).On = edit.ProjectionEnabled;
            c.GetSwitch(KeyHoloFigures).On = edit.HologramFigures;
            c.GetSwitch(KeyFrozen).On = edit.Frozen;
            GuiElementNumberInput opac = c.GetNumberInput(KeyOpacity); opac.Interval = 5f; opac.IntMode = true; opac.SetValue(edit.GhostOpacity);
            c.GetSwitch(KeyTerrain).On = edit.TerrainMap;
            GuiElementNumberInput tr = c.GetNumberInput(KeyTerrainRadius); tr.Interval = 1f; tr.IntMode = true; tr.SetValue(edit.TerrainMapRadius);
            GuiElementNumberInput th = c.GetNumberInput(KeyTerrainHeight); th.Interval = 1f; th.IntMode = true; th.SetValue(edit.TerrainMapHeight);
            c.GetSwitch(KeyEnabled).On = layer.Enabled;
            c.GetSwitch(KeyFill).On = layer.FillToLevel;
            // Colour block: the dropdown was composed already selected (ColorList); fill the hex field.
            syncingColor = true;
            c.GetTextInput(KeyColorHex).SetValue(GhostPalette.ToHex(layer.ResolveColor()));
            syncingColor = false;
            if (drape || fill) c.GetSwitch(KeyFluid).On = layer.TreatFluidAsSurface;
            if (!drape) c.GetSwitch(KeyFeedback).On = layer.ShowBuildFeedback;

            // Focus semantics only — the visual disable is the grayed font above (§p.6:
            // GetToggleButton, GuiComposerHelpers.cs:455-461; Enabled ⇒ Focusable, GuiElementToggleButton.cs:40).
            foreach (var (key, on) in toolbarStates) c.GetToggleButton(key).Enabled = on;

            // Save/Delete write the library file — refused while it is unreadable (playbook §7).
            c.GetButton("btnpresetsave").Enabled = !presets.LoadFailed;
            c.GetButton("btnpresetloadr").Enabled = anyPresets;
            c.GetButton("btnpresetloada").Enabled = anyPresets;
            c.GetButton("btnpresetdelete").Enabled = anyPresets && !presets.LoadFailed;

            c.UnfocusOwnElements();
            composing = false;
        }

        private readonly System.Collections.Generic.List<(string key, bool on)> toolbarStates = new();

        private readonly System.Collections.Generic.List<(string key, float interval, bool intMode, float value)> pendingInputs = new();

        // ------------------------------------------------------------------ text helpers

        /// <summary>§10a: index into shapeValues — polygon n=3 displays as Triangle (index 5); spiral moved to 6.</summary>
        private static int ShapeDropdownIndex(LayerParams l) => l.Shape switch
        {
            ShapeType.Spiral => 6,
            ShapeType.Polygon when l.Sides == 3 => 5,
            _ => (int)l.Shape,
        };

        /// <summary>Shape name as the dropdown spells it — a 3-sided polygon reads as Triangle (§10a alias).</summary>
        private static string ShapeName(LayerParams l) =>
            l.Shape == ShapeType.Polygon && l.Sides == 3
                ? Lang.Get("shapeprojector:gui-shape-triangle")
                : Lang.Get("shapeprojector:gui-shape-" + l.Shape.ToString().ToLowerInvariant());

        /// <summary>"+3" / "−2" for a signed block count in a tooltip.</summary>
        private static string Signed(int v) => v < 0 ? "−" + (-v) : "+" + v;

        /// <summary>
        /// The "what one radial step does to THIS layer" line shared by the Add-layer-out and
        /// Global-radius tooltips: the per-shape rule from RadialAdjust.Adjust (spec §10d) followed by
        /// the concrete before → after it would produce. A clamped step reports that the layer would
        /// stay entirely put — the whole-layer stays-put rule, which is exactly what surprises people.
        /// Pure preview: it works on a clone, so nothing here can edit the layer.
        /// </summary>
        private static string RadialMathLine(LayerParams l, int direction)
        {
            if (l.Shape == ShapeType.Spiral) return Lang.Get("shapeprojector:gui-tip-math-spiral");

            // Rectangle steps by 2 (one block per side); every other adjustable shape by 1 — RadialAdjust.cs.
            int step = l.Shape == ShapeType.Rectangle ? 2 * direction : direction;
            string rule = Lang.Get("shapeprojector:gui-tip-math-" + l.Shape.ToString().ToLowerInvariant(), Signed(step));
            string before = l.SizeSummary();
            LayerParams probe = l.Clone();
            return probe.AdjustRadial(direction) == RadialAdjustOutcome.Adjusted
                ? rule + ": " + before + " → " + probe.SizeSummary() + "."
                : rule + ", " + Lang.Get("shapeprojector:gui-tip-math-clamped", before);
        }

        /// <summary>
        /// What Global radius ±1 would do to the whole list, counted the same way OnGlobalRadius counts
        /// it afterwards — on clones, so the tooltip is a preview and never an edit.
        /// </summary>
        private string GlobalPreview(int direction)
        {
            int adjusted = 0, clamped = 0, skipped = 0;
            foreach (LayerParams l in edit.Layers)
            {
                switch (l.Clone().AdjustRadial(direction))
                {
                    case RadialAdjustOutcome.Adjusted: adjusted++; break;
                    case RadialAdjustOutcome.Clamped: clamped++; break;
                    default: skipped++; break;
                }
            }
            string s = Lang.Get("shapeprojector:gui-tip-global-count", adjusted, edit.Layers.Count);
            if (clamped > 0) s += " " + Lang.Get("shapeprojector:gui-tip-global-clamped", clamped);
            if (skipped > 0) s += " " + Lang.Get("shapeprojector:gui-tip-global-skipped", skipped);
            return s;
        }

        private string LayerName(int index, LayerParams l)
        {
            string name = Lang.Get("shapeprojector:gui-layer-n", index + 1) + ": " + ShapeName(l) + " " + l.SizeSummary() + l.ExtentSummary();
            if (!l.Enabled) name += " " + Lang.Get("shapeprojector:gui-layer-disabled");
            return name;
        }

        private string ResolvedCenterText(ProjectorParams p)
        {
            // Spec §3: centre = projector position + (dx, dz); a .5 means a corner/edge. Shown in the
            // coordinates the player sees everywhere else: the game's own coordinate HUD subtracts the
            // default spawn position (HudElementCoordinates.cs:65, asBlockPos.Sub(DefaultSpawnPosition.AsBlockPos);
            // IWorldAccessor.DefaultSpawnPosition - "usually the map middle", ~512000). Raw block
            // coordinates were shown until 2026-09-07 (user: "about 512000 blocks off").
            BlockPos spawn = capi.World.DefaultSpawnPosition.AsBlockPos;
            string cx = (be.Pos.X - spawn.X + p.Dx).ToString("0.#", CultureInfo.InvariantCulture);
            string cz = (be.Pos.Z - spawn.Z + p.Dz).ToString("0.#", CultureInfo.InvariantCulture);
            return Lang.Get("shapeprojector:gui-resolved-center", cx, cz);
        }

        private string EffectiveSizeText(ProjectorParams p, LayerParams l)
        {
            try
            {
                // Rectangle.EffectiveSize(ShapeCenter, int width, int depth) — Rectangle.cs:14 (throws below 1 block).
                var (w, d) = Rectangle.EffectiveSize(new ShapeCenter(p.Dx, p.Dz), Math.Max(1, l.Width), Math.Max(1, l.Depth));
                return Lang.Get("shapeprojector:gui-effective-size", w, d);
            }
            catch (ArgumentException)
            {
                return "";
            }
        }

        private void UpdateDynamicTexts()
        {
            // GetDynamicText(key) — GuiElementDynamicTextHelper.cs:43; SetNewText(string, bool autoHeight, bool forceRedraw, bool async)
            // — GuiElementDynamicText.cs:114.
            SingleComposer.GetDynamicText(KeyCenter).SetNewText(ResolvedCenterText(edit), false, true);
            LayerParams layer = edit.Layers[selected];
            if (layer.Shape == ShapeType.Rectangle)
            {
                SingleComposer.GetDynamicText(KeyEffective)?.SetNewText(EffectiveSizeText(edit, layer), false, true);
            }
            GuiElementDynamicText? cost = SingleComposer.GetDynamicText(KeyCost);
            if (cost != null)
            {
                var (text, over) = MarkCost();
                cost.Font = over ? CostFontRed : CostFont;   // GuiElementTextBase.Font is the field the recompose reads
                cost.SetNewText(text, false, true);
            }
        }

        /// <summary>
        /// The marks this configuration will ask for, from the Geometer's own rasterizer on cached
        /// per-layer geometry: every enabled layer's columns x height (fill and the surroundings model
        /// are ground-dependent and noted rather than counted), against the advisory threshold of the
        /// style the projector is set to. Red (over = true) past it, with a performance warning — the
        /// figure is still drawn in full up to the hard ceiling (2026-09-08).
        /// </summary>
        private (string text, bool over) MarkCost()
        {
            ProjectorConfig cfg = be.Config;
            int budget = edit.Style == DrawStyle.Blocks ? cfg.maxCubesPerProjector : cfg.maxCellsPerProjector;
            ShapeCenter center;
            try { center = new ShapeCenter(edit.Dx, edit.Dz); }
            catch (ArgumentException) { return ("", false); }

            long total = 0, thisLayer = 0;
            bool anyFill = false;
            while (costGeoms.Count < edit.Layers.Count) costGeoms.Add(new LayerGeometry());
            for (int i = 0; i < edit.Layers.Count; i++)
            {
                LayerParams l = edit.Layers[i];
                if (!edit.Enabled || !l.Enabled) continue;
                ShapeSpec? spec = l.ToShapeSpec();
                if (spec == null) continue;
                LayerGeometry g = costGeoms[i];
                g.Update(new LayerParameters(spec, center, cfg.maxRadius, Math.Clamp(l.Thickness, 1, cfg.maxOutlineThickness)));
                if (g.Error != null) continue;
                long n = (long)g.Positions.Count * Math.Max(1, l.Height);
                total += n;
                if (i == selected) thisLayer = n;
                if (l.FillToLevel) anyFill = true;
            }

            bool over = total > budget;
            string text = Lang.Get(over ? "shapeprojector:gui-cost-over" : "shapeprojector:gui-cost",
                total.ToString("N0", CultureInfo.InvariantCulture), budget.ToString("N0", CultureInfo.InvariantCulture), thisLayer.ToString("N0", CultureInfo.InvariantCulture));
            if (anyFill) text += " " + Lang.Get("shapeprojector:gui-cost-fill");
            return (text, over);
        }

        private void OnStyleChanged(string code, bool selectedFlag)
        {
            if (composing) return;
            ReadInputsInto(edit);
            UpdateDynamicTexts();   // the budget the cost line is measured against changed
        }

        // ------------------------------------------------------------------ reading the elements

        /// <summary>Reads every element into <paramref name="p"/> (selected layer only), rounding to the spec's 0.5 steps.</summary>
        private void ReadInputsInto(ProjectorParams p)
        {
            if (composing || SingleComposer == null) return;
            int max = be.Config.maxRadius;
            GuiComposer c = SingleComposer;
            // GuiElementNumberInput.GetValue() → float — §h.2 (GuiElementNumberInput.cs:57).
            p.Enabled = c.GetSwitch(KeyProjEnabled).On;
            p.HologramEnabled = c.GetSwitch(KeyHolo).On;
            p.ProjectionEnabled = c.GetSwitch(KeyWorldMarks).On;
            p.HologramFigures = c.GetSwitch(KeyHoloFigures).On;
            p.Style = c.GetDropDown(KeyStyle)?.SelectedValue == "blocks" ? DrawStyle.Blocks : DrawStyle.Faces;
            p.Frozen = c.GetSwitch(KeyFrozen).On;
            p.GhostOpacity = Math.Clamp((int)Math.Round(c.GetNumberInput(KeyOpacity).GetValue()), ProjectorParams.MinOpacityPercent, 100);
            p.TerrainMap = c.GetSwitch(KeyTerrain).On;
            p.TerrainMapRadius = Math.Clamp((int)Math.Round(c.GetNumberInput(KeyTerrainRadius).GetValue()), 1, be.Config.maxTerrainMapRadius);
            p.TerrainMapHeight = Math.Clamp((int)Math.Round(c.GetNumberInput(KeyTerrainHeight).GetValue()), 1, be.Config.maxTerrainMapHeight);
            p.Dx = ProjectorParams.ToHalfStep(c.GetNumberInput(KeyDx).GetValue(), -max, max);
            p.Dz = ProjectorParams.ToHalfStep(c.GetNumberInput(KeyDz).GetValue(), -max, max);

            LayerParams layer = p.Layers[selected];
            float Get(string key) => c.GetNumberInput(P + key).GetValue();
            switch (layer.Shape)
            {
                case ShapeType.Circle:
                    layer.Radius = ProjectorParams.ToHalfStep(Get("radius"), 0.5, max);
                    break;
                case ShapeType.Ring:
                    layer.InnerRadius = ProjectorParams.ToHalfStep(Get("innerRadius"), 0.5, max);
                    layer.Radius = ProjectorParams.ToHalfStep(Get("radius"), 0.5, max);
                    break;
                case ShapeType.Ellipse:
                    layer.RadiusX = ProjectorParams.ToHalfStep(Get("radiusX"), 0.5, max);
                    layer.RadiusZ = ProjectorParams.ToHalfStep(Get("radiusZ"), 0.5, max);
                    break;
                case ShapeType.Rectangle:
                    layer.Width = Math.Clamp((int)Math.Round(Get("width")), 1, 2 * max);
                    layer.Depth = Math.Clamp((int)Math.Round(Get("depth")), 1, 2 * max);
                    break;
                case ShapeType.Polygon:
                    layer.Sides = Math.Clamp((int)Math.Round(Get("sides")), 3, 64);
                    layer.Circumradius = ProjectorParams.ToHalfStep(Get("circumradius"), 0.5, max);
                    layer.RotationDeg = Get("rotationDeg");
                    break;
                case ShapeType.Spiral:
                    layer.Turns = Math.Clamp(Math.Round(Get("turns") * 4) / 4, 0.25, 64);
                    // GUI minimum spacing 2 (Geometer's recommendation: below ~2 laps merge at block resolution, Spiral.cs:14-16).
                    layer.Spacing = ProjectorParams.ToHalfStep(Get("spacing"), 2, max);
                    layer.StartRadius = ProjectorParams.ToHalfStep(Get("startRadius"), 0, max);
                    // GuiElementDropDown.SelectedValue — §h.2 (GuiElementDropDown.cs:70).
                    layer.Clockwise = c.GetDropDown(KeyDirection).SelectedValue != "ccw";
                    break;
            }
            layer.YOffset = (int)Math.Round(c.GetNumberInput(KeyYOffset).GetValue());
            // Thickness tops out at the figure's own extent (2026-09-07): the shape fields above were
            // just read, so MaxThickness sees the current radius.
            // Only the config ceilings (2026-09-08: nothing is pulled back to the figure's own extent;
            // past it extra thickness simply changes nothing, and the tooltip says where that is).
            layer.Thickness = Math.Clamp((int)Math.Round(c.GetNumberInput(KeyThickness).GetValue()), 1, be.Config.maxOutlineThickness);
            layer.Height = Math.Clamp((int)Math.Round(c.GetNumberInput(KeyHeight).GetValue()), 1, be.Config.maxLayerHeight);
            GuiElementSwitch? fillSw = c.GetSwitch(KeyFill);
            if (fillSw != null) layer.FillToLevel = fillSw.On;
            // Vertical mode + fluid rule (spec §5a, step 5). GuiComposer.GetElement returns null for a missing
            // key (GuiComposer.cs:834-845, verified this session — api-notes "Renderer additions"), so the
            // Drape-only switch is read only when the dialog was composed with it.
            GuiElementDropDown? vmDrop = c.GetDropDown(KeyVMode);
            if (vmDrop != null) layer.VerticalMode = vmDrop.SelectedValue == "drape" ? VerticalMode.Drape : VerticalMode.FixedY;
            GuiElementSwitch? fluidSw = c.GetSwitch(KeyFluid);
            if (fluidSw != null) layer.TreatFluidAsSurface = fluidSw.On;
            GuiElementSwitch? fbSw = c.GetSwitch(KeyFeedback);
            if (fbSw != null) layer.ShowBuildFeedback = fbSw.On;
            layer.Enabled = c.GetSwitch(KeyEnabled).On;
            // The hex field is the colour's source of truth (a dropdown pick writes it); an unparsable
            // entry leaves the last good colour.
            if (GhostPalette.TryParseHex(c.GetTextInput(KeyColorHex)?.GetText(), out int lrgb)) layer.ColorRgb = GhostPalette.SanitizeRgb(lrgb);
        }

        // ------------------------------------------------------------------ handlers

        private void OnOffsetChanged(string _)
        {
            if (composing) return;
            ReadInputsInto(edit);
            UpdateDynamicTexts();
        }

        private void OnParamChanged(string _)
        {
            if (composing) return;
            ReadInputsInto(edit);
            UpdateDynamicTexts();
        }

        private void OnLayerSelected(string code, bool selectedFlag)
        {
            if (composing) return;
            ReadInputsInto(edit);
            int idx = int.Parse(code, CultureInfo.InvariantCulture);
            if (idx == selected) return;
            selected = idx;
            ComposeDialog();   // static composer: rebuild for the newly selected layer (precedent GuiDialogEditAction.cs:77-83)
        }

        private void OnShapeChanged(string code, bool selectedFlag)
        {
            if (composing) return;
            ReadInputsInto(edit);
            LayerParams layer = edit.Layers[selected];
            ShapeType newShape = code switch
            {
                "ring" => ShapeType.Ring,
                "ellipse" => ShapeType.Ellipse,
                "rectangle" => ShapeType.Rectangle,
                "polygon" => ShapeType.Polygon,
                "triangle" => ShapeType.Polygon,   // §10a alias — polygon n=3, no parallel path
                "spiral" => ShapeType.Spiral,
                _ => ShapeType.Circle,
            };
            if (newShape == layer.Shape)
            {
                // Already a polygon: "Triangle" just pins sides to 3 (Triangle.Spec semantics).
                if (code == "triangle" && layer.Sides != 3) { layer.Sides = 3; ComposeDialog(); }
                return;
            }
            layer.Shape = newShape;
            if (code == "triangle") layer.Sides = 3;
            // Spec §7 colour default and §5a vertical-mode default are reset on a shape change.
            layer.ApplyShapeDefaults();
            ComposeDialog();   // the parameter rows depend on the shape (precedent GuiDialogEditAction.cs:77-83)
        }

        private void OnDirectionChanged(string code, bool selectedFlag)
        {
            if (composing) return;
            edit.Layers[selected].Clockwise = code != "ccw";
        }

        private void OnVerticalModeChanged(string code, bool selectedFlag)
        {
            if (composing) return;
            VerticalMode before = edit.Layers[selected].VerticalMode;
            ReadInputsInto(edit);   // also reads the new dropdown value into the layer
            if (edit.Layers[selected].VerticalMode != before)
            {
                ComposeDialog();    // the fluid-rule row exists only in Drape (precedent GuiDialogEditAction.cs:77-83)
            }
        }

        /// <summary>Fill up to level toggled: the fluid-rule row exists for a filled Fixed-Y layer only, so recompose (same reason as OnVerticalModeChanged).</summary>
        private void OnFillToggled(bool on)
        {
            if (composing) return;
            ReadInputsInto(edit);
            ComposeDialog();
        }

        /// <summary>World marks toggled: no rows depend on it — read it so a hotkey-driven Apply carries it.</summary>
        private void OnWorldMarksToggled(bool on)
        {
            if (composing) return;
            ReadInputsInto(edit);
        }

        // ---- Colour picker handlers (2026-09-07). A dropdown pick sets the colour and writes the hex
        // field; a hex edit sets the colour and re-selects the dropdown (adding a "custom" entry when
        // the colour is not a swatch); both repaint the preview square. The guard stops the two from
        // feeding each other.
        private void OnLayerPick(string code, bool selectedFlag)
        {
            if (composing || syncingColor) return;
            if (!GhostPalette.TryParseHex(code, out int rgb)) return;
            SetColor(GhostPalette.SanitizeRgb(rgb), KeyColorHex, KeyColorPicker, KeyColorPreview, v => edit.Layers[selected].ColorRgb = v);
        }

        private void OnLayerHex(string text)
        {
            if (composing || syncingColor) return;
            if (!GhostPalette.TryParseHex(text, out int rgb)) return;
            SetColor(GhostPalette.SanitizeRgb(rgb), null, KeyColorPicker, KeyColorPreview, v => edit.Layers[selected].ColorRgb = v);
        }


        /// <summary>Applies a colour to the model and to every control that shows it (hex field only when <paramref name="hexKey"/> is given — a typed hex must not be rewritten under the caret).</summary>
        private void SetColor(int rgb, string? hexKey, string pickerKey, string previewKey, Action<int> store)
        {
            store(rgb);
            if (SingleComposer == null) return;
            syncingColor = true;
            try
            {
                if (hexKey != null) SingleComposer.GetTextInput(hexKey).SetValue(GhostPalette.ToHex(rgb));
                // Re-list the dropdown so a typed colour appears (and is selected) as its own entry.
                // GuiElementDropDown.SetList(values, names) / SetSelectedIndex(int) — GuiElementDropDown.cs:416/395.
                GuiElementDropDown? dd = SingleComposer.GetDropDown(pickerKey);
                if (dd != null)
                {
                    var (values, names, index) = ColorList(rgb);
                    dd.SetList(values, names);
                    dd.SetSelectedIndex(index);
                }
                SingleComposer.GetCustomDraw(previewKey)?.Redraw();
            }
            finally
            {
                syncingColor = false;
            }
        }

        private static LayerParams NewLayer()
        {
            LayerParams l = new LayerParams();
            l.ApplyShapeDefaults();
            return l;
        }

        private bool OnAddLayer()
        {
            ReadInputsInto(edit);
            if (edit.Layers.Count >= be.Config.maxLayersPerProjector)
            {
                // void TriggerIngameError(object sender, string errorCode, string text) — ICoreClientAPI.cs:218.
                capi.TriggerIngameError(this, "layerlimit", Lang.Get("shapeprojector:gui-layer-limit", be.Config.maxLayersPerProjector));
                return true;
            }
            edit.Layers.Add(NewLayer());
            selected = edit.Layers.Count - 1;
            ComposeDialog();
            return true;
        }

        private bool OnRemoveLayer()
        {
            ReadInputsInto(edit);
            if (edit.Layers.Count <= 1) return true;
            edit.Layers.RemoveAt(selected);
            selected = Math.Clamp(selected, 0, edit.Layers.Count - 1);
            ComposeDialog();
            return true;
        }

        private bool OnDuplicateLayer()
        {
            ReadInputsInto(edit);
            if (edit.Layers.Count >= be.Config.maxLayersPerProjector)
            {
                capi.TriggerIngameError(this, "layerlimit", Lang.Get("shapeprojector:gui-layer-limit", be.Config.maxLayersPerProjector));
                return true;
            }
            edit.Layers.Insert(selected + 1, edit.Layers[selected].Clone());
            selected++;
            ComposeDialog();
            return true;
        }

        private bool OnMoveUp()
        {
            ReadInputsInto(edit);
            if (selected <= 0) return true;
            (edit.Layers[selected - 1], edit.Layers[selected]) = (edit.Layers[selected], edit.Layers[selected - 1]);
            selected--;
            ComposeDialog();
            return true;
        }

        private bool OnMoveDown()
        {
            ReadInputsInto(edit);
            if (selected >= edit.Layers.Count - 1) return true;
            (edit.Layers[selected + 1], edit.Layers[selected]) = (edit.Layers[selected], edit.Layers[selected + 1]);
            selected++;
            ComposeDialog();
            return true;
        }

        // ------------------------------------------------------------------ §10d shortcuts

        private bool OnAddLayerUp()
        {
            ReadInputsInto(edit);
            if (edit.Layers.Count >= be.Config.maxLayersPerProjector)
            {
                capi.TriggerIngameError(this, "layerlimit", Lang.Get("shapeprojector:gui-layer-limit", be.Config.maxLayersPerProjector));
                return true;
            }
            // Spec §10d: duplicate selected, Y offset +1, new layer selected — repeated presses chain
            // (one press per tower course). Insert-after + select mirrors OnDuplicateLayer.
            LayerParams copy = edit.Layers[selected].Clone();
            copy.YOffset++;
            edit.Layers.Insert(selected + 1, copy);
            selected++;
            ComposeDialog();
            return true;
        }

        private bool OnAddLayerOut()
        {
            ReadInputsInto(edit);
            if (edit.Layers.Count >= be.Config.maxLayersPerProjector)
            {
                capi.TriggerIngameError(this, "layerlimit", Lang.Get("shapeprojector:gui-layer-limit", be.Config.maxLayersPerProjector));
                return true;
            }
            // Spec §10d: duplicate selected, radial +1 (per-shape semantics, RadialAdjust.cs), new layer
            // selected — chains toward a filled disc. Spiral never reaches here (button disabled).
            LayerParams copy = edit.Layers[selected].Clone();
            if (copy.AdjustRadial(1) != RadialAdjustOutcome.Adjusted) return true;
            edit.Layers.Insert(selected + 1, copy);
            selected++;
            ComposeDialog();
            return true;
        }

        private bool OnGlobalPlus() => OnGlobalRadius(1);
        private bool OnGlobalMinus() => OnGlobalRadius(-1);

        private bool OnGlobalRadius(int direction)
        {
            ReadInputsInto(edit);
            int adjusted = 0, clamped = 0, skipped = 0;
            // Spec §10d: EVERY layer including disabled ones (hidden layers must not fall out of sync);
            // Y offsets untouched; a clamped layer stays put entirely (RadialAdjust.cs) and is counted
            // for the visible report; spirals are skipped and noted.
            foreach (LayerParams l in edit.Layers)
            {
                switch (l.AdjustRadial(direction))
                {
                    case RadialAdjustOutcome.Adjusted: adjusted++; break;
                    case RadialAdjustOutcome.Clamped: clamped++; break;
                    default: skipped++; break;
                }
            }
            string report = Lang.Get("shapeprojector:gui-adjust-adjusted", direction > 0 ? "+1" : "−1", adjusted);
            if (clamped > 0) report += " " + Lang.Get("shapeprojector:gui-adjust-clamped", clamped);
            if (skipped > 0) report += " " + Lang.Get("shapeprojector:gui-adjust-skipped", skipped);
            lastAdjustReport = report;
            ComposeDialog();
            return true;
        }

        /// <summary>
        /// §10d hotkeys while the GUI is open (ledger §m): PageUp = Add layer up, PageDown = Add layer
        /// out, + / − (main row and keypad) = Global radius ±1. base.OnKeyDown first — composers get
        /// the key and a FOCUSED text input consumes it (GuiElementEditableTextBase.OnKeyDown gated on
        /// HasFocus, §m.2) — then the explicit vanilla-pattern guard (GuiComposer.cs:607 via §m.2)
        /// so typing can never trigger a shortcut. Letter keys avoided deliberately: an open dialog
        /// does NOT gate CharacterControls hotkeys (§m.4), so a letter here would shadow its world
        /// hotkey (e.g. the "O" hide toggle) while the dialog is focused.
        /// GuiDialog.OnKeyDown(KeyEvent) — §m.1 (GuiDialog.cs:472); KeyEvent.Handled (KeyEvent.cs:23);
        /// GlKeys values — §m.3 (GlKeys.cs: PageUp=56, PageDown=57, Plus=121, Minus=120,
        /// KeypadAdd=80, KeypadMinus=79).
        /// </summary>
        public override void OnKeyDown(KeyEvent args)
        {
            base.OnKeyDown(args);
            if (args.Handled) return;
            if (SingleComposer?.CurrentTabIndexElement is GuiElementEditableTextBase) return;

            switch (args.KeyCode)
            {
                case (int)GlKeys.PageUp: OnAddLayerUp(); break;
                case (int)GlKeys.PageDown: OnAddLayerOut(); break;
                case (int)GlKeys.Plus:
                case (int)GlKeys.KeypadAdd: OnGlobalRadius(1); break;
                case (int)GlKeys.Minus:
                case (int)GlKeys.KeypadMinus: OnGlobalRadius(-1); break;
                default: return;
            }
            args.Handled = true;
        }

        // ------------------------------------------------------------------ presets (spec §4a)

        private void OnPresetSelected(string code, bool selectedFlag)
        {
            if (composing) return;
            selectedPreset = code;
        }

        private bool OnPresetSave()
        {
            ReadInputsInto(edit);
            // GetTextInput(key) — §h.2 (GuiComposerHelpers.cs:955); GetText() — GuiElementTextBase.cs:100
            // (editable override GuiElementEditableTextBase.cs:787).
            string name = SingleComposer.GetTextInput(KeyPresetName).GetText()?.Trim() ?? "";
            if (name.Length == 0) return true;   // guard: empty name → ignore Save (task §2)

            presets.Presets[name] = edit.Clone();   // overwrite same name (spec §4a)
            presets.Store(capi);
            selectedPreset = name;
            ComposeDialog();   // refresh the dropdown (recompose precedent GuiDialogEditAction.cs:77-83)
            return true;
        }

        private bool OnPresetLoadReplace() => DoPresetLoad(append: false);
        private bool OnPresetLoadAppend() => DoPresetLoad(append: true);

        private bool DoPresetLoad(bool append)
        {
            if (selectedPreset.Length == 0 || !presets.Presets.TryGetValue(selectedPreset, out ProjectorParams? preset) || preset == null)
            {
                return true;
            }
            // Spec §10b: loading writes through the standard server-authoritative edit packets —
            // claims and caps apply exactly as for manual edits, so an oversized load clamps (and is
            // pre-clamped here with the client's config).
            if (append)
            {
                // Append keeps the projector's current centre offset and layers; the preset's layers
                // join at the end and the FIRST appended layer becomes selected. Drops at the layer
                // cap are reported on the adjustment line.
                ReadInputsInto(edit);
                int before = edit.Layers.Count;
                int offered = preset.Layers.Count;
                foreach (LayerParams l in preset.Layers) edit.Layers.Add(l.Clone());
                edit.Clamp(be.Config);
                int added = edit.Layers.Count - before;
                if (added <= 0)
                {
                    capi.TriggerIngameError(this, "layerlimit", Lang.Get("shapeprojector:gui-layer-limit", be.Config.maxLayersPerProjector));
                    return true;
                }
                lastAdjustReport = Lang.Get("shapeprojector:gui-preset-appended", added);
                if (added < offered)
                {
                    lastAdjustReport += " " + Lang.Get("shapeprojector:gui-preset-append-dropped", offered - added, be.Config.maxLayersPerProjector);
                }
                selected = before;
            }
            else
            {
                edit = preset.Clone();
                edit.Clamp(be.Config);
                if (edit.Layers.Count == 0) edit.Layers.Add(NewLayer());
                selected = 0;
            }
            ComposeDialog();
            be.SendApply(edit);
            return true;
        }

        private bool OnPresetDelete()
        {
            if (selectedPreset.Length == 0) return true;
            presets.Remove(selectedPreset);   // clears a malformed entry of the same name as well
            presets.Store(capi);
            selectedPreset = "";   // guard: deleting the selected preset resets the dropdown (task §2)
            ComposeDialog();
            return true;
        }

        private bool OnApply()
        {
            ReadInputsInto(edit);
            be.SendApply(edit);
            // Stay open: the outlines move when the server echoes the new state (§c.4) and RefreshFrom() runs.
            return true;
        }

        private bool OnClose()
        {
            // GuiDialog.TryClose() — §h.1 (GuiDialog.cs:349).
            TryClose();
            return true;
        }

        // GuiDialog.OnGuiClosed — ledger (GuiDialog.cs:311). Clears the §10c hologram's dialog signal.
        public override void OnGuiClosed()
        {
            be.GuiSelectedLayer = null;
            base.OnGuiClosed();
        }

        private void OnTitleBarClose()
        {
            TryClose();
        }

        /// <summary>Pushes server-confirmed parameters into the dialog (called by the BE after a resync, §c.4).</summary>
        public void RefreshFrom(ProjectorParams p)
        {
            // The server's echo is also the moment the client rebuilt its cells: if the budget cut
            // anything, say so where the clamp reports already appear (2026-09-07).
            if (be.BudgetReport.Length > 0) lastAdjustReport = be.BudgetReport;
            edit = p.Clone();
            if (edit.Layers.Count == 0) edit.Layers.Add(NewLayer());
            selected = Math.Clamp(selected, 0, edit.Layers.Count - 1);
            ComposeDialog();
        }
    }
}
