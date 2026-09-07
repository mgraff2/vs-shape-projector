using System;
using System.Collections.Generic;
using System.Globalization;
using ShapeProjector.Geometry;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ShapeProjector
{
    /// <summary>Shape kinds (spec §5). Values are persisted; do not renumber.</summary>
    public enum ShapeType
    {
        Circle = 0,
        Ring = 1,
        Ellipse = 2,
        Rectangle = 3,
        Polygon = 4,
        Spiral = 5,
    }

    /// <summary>
    /// Curated ghost palette (spec §7): cyan default for circle/ring/ellipse, amber for rectangle/polygon,
    /// violet for spiral, green = "done" tint. Codes match the Curator's lang keys
    /// <c>shapeprojector:gui-color-&lt;code&gt;</c>.
    /// </summary>
    public static class GhostPalette
    {
        /// <summary>Default world-ghost alpha; the per-projector GhostOpacity (percent) scales from here.</summary>
        public const int Alpha = 110;
        /// <summary>GhostOpacity default: Alpha as a percentage (110/255 ≈ 43%).</summary>
        public const int DefaultOpacityPercent = 43;
        /// <summary>
        /// The build-feedback "done" tint (spec §6), the one colour a layer may NOT be: a layer in
        /// exactly this green would make done and not-done marks identical. The swatch grid leaves it
        /// out and <see cref="SanitizeRgb"/> nudges a typed hex code off it by one step.
        /// </summary>
        public const int DoneRgb = 0x3CDC5A;   // (60, 220, 90)

        /// <summary>
        /// Legacy six-entry palette: trees and presets written before the colour picker (2026-09-07)
        /// store a <c>colorIndex</c> into this table. Codes match the lang keys
        /// <c>shapeprojector:gui-color-&lt;code&gt;</c>. Never renumber.
        /// </summary>
        public static readonly (string Code, int R, int G, int B)[] Entries =
        {
            ("cyan",   0, 200, 255),
            ("amber",  255, 180, 0),
            ("violet", 170, 90, 255),
            ("green",  60, 220, 90),
            ("white",  240, 240, 240),
            ("red",    255, 70, 70),
        };

        public static int ClampIndex(int index) => index < 0 || index >= Entries.Length ? 0 : index;

        /// <summary>
        /// The colour picker's entries (user request 2026-09-07: "a colour picker ... they would
        /// like that"), as 0xRRGGBB — 24 hues spanning the wheel plus a neutral run, chosen to stay
        /// apart from each other at ghost alpha and from the done green (which is deliberately
        /// absent). The picker is a dropdown whose every entry shows the colour itself beside its
        /// hex code; the hex field beside it is for the few who want an exact value.
        /// </summary>
        public static readonly int[] Swatches =
        {
            0x00C8FF, 0x4FA3FF, 0x2255EE, 0x1A2E80, 0x5A46E0, 0xAA5AFF, 0xE040E0, 0xFF6EB4,
            0xFF4646, 0xC05070, 0xFF8C1E, 0xFFB400, 0xFFF03C, 0xB4FF3C, 0x3CFFB4, 0x00B4A0,
            0x2E8B57, 0x9AA020, 0x8B5A2B, 0xD2B48C, 0xF0F0F0, 0xB4B4B4, 0x787878, 0x3C3C3C,
        };

        /// <summary>Legacy palette entry as 0xRRGGBB.</summary>
        public static int Rgb(int index)
        {
            var e = Entries[ClampIndex(index)];
            return (e.R << 16) | (e.G << 8) | e.B;
        }

        /// <summary>
        /// Packs an 0xRRGGBB colour for the vertex colour attribute at the given alpha.
        /// ColorUtil.ColorFromRgba(int r, int g, int b, int a) — "true RGBA order", r in the low byte —
        /// api-notes §d.3 (ColorUtil.cs:276-281).
        /// </summary>
        public static int Pack(int rgb, int alpha) => ColorUtil.ColorFromRgba((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF, alpha);

        /// <summary>Masks to 24 bits and keeps a layer off the reserved done green (one step of blue away).</summary>
        public static int SanitizeRgb(int rgb)
        {
            rgb &= 0xFFFFFF;
            return rgb == DoneRgb ? DoneRgb + 1 : rgb;
        }

        /// <summary>Index of the swatch equal to <paramref name="rgb"/>, or -1 (a typed colour that is not a swatch).</summary>
        public static int SwatchIndexOf(int rgb)
        {
            for (int i = 0; i < Swatches.Length; i++) if (Swatches[i] == rgb) return i;
            return -1;
        }

        /// <summary>"#RRGGBB" for the hex field.</summary>
        public static string ToHex(int rgb) => "#" + (rgb & 0xFFFFFF).ToString("X6");

        /// <summary>Parses "#RRGGBB" / "RRGGBB" / "#RGB"; whitespace tolerated; false on anything else.</summary>
        public static bool TryParseHex(string? text, out int rgb)
        {
            rgb = 0;
            if (text == null) return false;
            string s = text.Trim();
            if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length == 3)
            {
                s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            }
            if (s.Length != 6) return false;
            if (!int.TryParse(s, System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
            rgb = v;
            return true;
        }

        public static int IndexOf(string code)
        {
            for (int i = 0; i < Entries.Length; i++) if (Entries[i].Code == code) return i;
            return 0;
        }

        /// <summary>Legacy palette entry packed for the vertex colour attribute, at the default alpha.</summary>
        public static int Color(int index) => Pack(Rgb(index), Alpha);

        /// <summary>Spec §6 "done" tint at the default alpha.</summary>
        public static int DoneColor => Pack(DoneRgb, Alpha);

        /// <summary>The done tint at the projector's own opacity.</summary>
        public static int DoneColorAt(int alpha) => Pack(DoneRgb, alpha);

        /// <summary>
        /// World-ghost alpha for a GhostOpacity percentage (user request 2026-09-07: "set the
        /// transparency of the ghost blocks"). 100% is fully opaque; the floor keeps a mark visible
        /// at the lowest setting the GUI offers. The hologram ignores this — it has its own contrast
        /// constants (ruling 9) and must stay legible whatever the world marks are set to.
        /// </summary>
        public static int AlphaFor(int opacityPercent) => Math.Clamp(opacityPercent * 255 / 100, 13, 255);


        /// <summary>Spec §7 defaults: cyan circles/rings, amber rectangles/polygons, violet spirals (ellipse → cyan).</summary>
        public static int DefaultIndexFor(ShapeType shape) => shape switch
        {
            ShapeType.Rectangle => 1,
            ShapeType.Polygon => 1,
            ShapeType.Spiral => 2,
            _ => 0,
        };
    }

    /// <summary>One layer (spec §4/§5). Plain data; persisted as a sub-tree of the block entity.</summary>
    public class LayerParams
    {
        public ShapeType Shape = ShapeType.Circle;

        // Circle radius / ring outer radius (0.5 steps).
        public double Radius = 11;
        // Ring inner radius (0.5 steps, < Radius).
        public double InnerRadius = 8;
        // Ellipse semi-axes (0.5 steps).
        public double RadiusX = 11;
        public double RadiusZ = 6;
        // Rectangle sides in blocks (see Rectangle.EffectiveSize for the parity rule).
        public int Width = 10;
        public int Depth = 6;
        // Regular polygon.
        public int Sides = 6;
        public double Circumradius = 11;
        public double RotationDeg = 0;
        // Archimedean spiral.
        public double Turns = 3;
        public double Spacing = 3;
        public double StartRadius = 0;
        public bool Clockwise = true;

        /// <summary>
        /// Spec §5a: in Fixed Y mode, outline Y = projector Y + YOffset. In Drape mode the SAME field is the
        /// optional drape offset ("optional layer offset for elevated walkways"): outline Y = surface + 1 + YOffset,
        /// and it doubles as the fixed-Y fallback for unloaded columns (DrapeResolver.ResolveColumn contract).
        /// </summary>
        public int YOffset = 0;
        /// <summary>Spec §5a vertical mode; defaults per shape family (ShapeSpec.DefaultVerticalMode).</summary>
        public VerticalMode VerticalMode = VerticalMode.Drape;
        /// <summary>
        /// Spec §5a fluid rule, Drape only: on → the outline sits on the fluid top (bank line);
        /// off → it sees through fluids to the bed (bottom line). Implemented in the BE's height
        /// callback (api-notes §g.3); the Geometry library never inspects blocks.
        /// </summary>
        public bool TreatFluidAsSurface = true;
        /// <summary>
        /// Spec §6 build feedback ("Optional per-layer"): Fixed-Y layers only per the step-6 design
        /// ruling (a draped ghost follows the surface, so it can never be "filled"); additionally
        /// gated by the client config showBuildFeedback.
        /// </summary>
        public bool ShowBuildFeedback = true;
        /// <summary>Legacy palette index; only read when <see cref="ColorRgb"/> is unset (trees and presets from before 2026-09-07).</summary>
        public int ColorIndex = 0;
        /// <summary>
        /// The layer's colour as 0xRRGGBB (colour picker, user request 2026-09-07), or -1 = "not set,
        /// use ColorIndex" — the value an old tree or an old preset JSON produces. Always read through
        /// <see cref="ResolveColor"/>. Never the done green (GhostPalette.SanitizeRgb).
        /// </summary>
        public int ColorRgb = -1;
        public bool Enabled = true;

        /// <summary>The colour to draw, resolving a legacy index on first use.</summary>
        public int ResolveColor()
        {
            if (ColorRgb < 0) ColorRgb = GhostPalette.Rgb(ColorIndex);
            return ColorRgb;
        }

        /// <summary>
        /// Outline thickness in blocks for THIS layer (user request 2026-09-02; spec §5's "one block
        /// thick" is superseded per layer). 1 is the plain outline. The Geometer thickens INWARD —
        /// Thickness.ThickenClosed peels the interior, so the outer extent stays exactly at the layer's
        /// radius and the band eats inward; a spiral instead repeats its path at smaller start radii
        /// (Spiral.Rasterize). A circle of radius 11, thickness 3, is therefore a band from about 9 to 11.
        /// Clamped to the config's maxOutlineThickness.
        /// </summary>
        public int Thickness = 1;
        /// <summary>
        /// Vertical extent in blocks for THIS layer (user request 2026-09-02): the outline is drawn on
        /// Height successive levels going UP from the layer's resolved Y, so one layer can describe a
        /// wall that used to need Height stacked layers. 1 is the flat outline. In Drape mode the base
        /// of the extrusion is each column's own resolved surface, so a tall draped layer follows the
        /// ground and stands the same height everywhere. Clamped to the config's maxLayerHeight.
        /// </summary>
        public int Height = 1;

        /// <summary>
        /// Fill up to level (user request 2026-09-07): besides the figure at its own level, mark
        /// every block from each column's ground up to that level, so a pit under a platform shows
        /// as the fill it needs and a lake bed (fluid rule off) as the fill of a causeway. The level
        /// is the layer's Y in Fixed Y; in Follow terrain it is the HIGHEST ground under the
        /// figure, so the whole figure levels up to its highest point. Height then stands on top
        /// of the level as usual. Resolved in the block entity (it needs the ground); the Geometry
        /// library's LevelFill holds the arithmetic.
        /// </summary>
        public bool FillToLevel = false;

        public LayerParams Clone() => (LayerParams)MemberwiseClone();

        /// <summary>Spec §7 colour default and §5a vertical-mode default for the current shape.</summary>
        public void ApplyShapeDefaults()
        {
            ColorIndex = GhostPalette.DefaultIndexFor(Shape);
            ColorRgb = GhostPalette.Rgb(ColorIndex);
            // ShapeSpec.DefaultVerticalMode — Geometry ShapeSpec.cs:22 (Drape for circle/ring/ellipse/spiral, FixedY otherwise).
            VerticalMode = ToShapeSpec()?.DefaultVerticalMode ?? VerticalMode.FixedY;
        }

        // ITreeAttribute setters/getters — api-notes §i.1 (ITreeAttribute.cs:55-90, 119-219).
        public void ToTree(ITreeAttribute tree)
        {
            tree.SetInt("shape", (int)Shape);
            tree.SetDouble("radius", Radius);
            tree.SetDouble("innerRadius", InnerRadius);
            tree.SetDouble("radiusX", RadiusX);
            tree.SetDouble("radiusZ", RadiusZ);
            tree.SetInt("width", Width);
            tree.SetInt("depth", Depth);
            tree.SetInt("sides", Sides);
            tree.SetDouble("circumradius", Circumradius);
            tree.SetDouble("rotationDeg", RotationDeg);
            tree.SetDouble("turns", Turns);
            tree.SetDouble("spacing", Spacing);
            tree.SetDouble("startRadius", StartRadius);
            tree.SetBool("clockwise", Clockwise);
            tree.SetInt("yOffset", YOffset);
            tree.SetInt("vmode", (int)VerticalMode);
            tree.SetBool("fluidSurface", TreatFluidAsSurface);
            tree.SetBool("buildFeedback", ShowBuildFeedback);
            tree.SetInt("color", ColorIndex);
            tree.SetBool("enabled", Enabled);
            tree.SetInt("thickness", Thickness);
            tree.SetInt("height", Height);
            tree.SetBool("fillToLevel", FillToLevel);
            tree.SetInt("colorRgb", ResolveColor());
        }

        public static LayerParams FromTree(ITreeAttribute tree)
        {
            LayerParams l = new LayerParams();
            l.Shape = (ShapeType)tree.GetInt("shape", 0);
            l.Radius = tree.GetDouble("radius", 11);
            l.InnerRadius = tree.GetDouble("innerRadius", 8);
            l.RadiusX = tree.GetDouble("radiusX", 11);
            l.RadiusZ = tree.GetDouble("radiusZ", 6);
            l.Width = tree.GetInt("width", 10);
            l.Depth = tree.GetInt("depth", 6);
            l.Sides = tree.GetInt("sides", 6);
            l.Circumradius = tree.GetDouble("circumradius", 11);
            l.RotationDeg = tree.GetDouble("rotationDeg", 0);
            l.Turns = tree.GetDouble("turns", 3);
            l.Spacing = tree.GetDouble("spacing", 3);
            l.StartRadius = tree.GetDouble("startRadius", 0);
            l.Clockwise = tree.GetBool("clockwise", true);
            l.YOffset = tree.GetInt("yOffset", 0);
            // Trees written by step 3 have no "vmode" for the default layer beyond FixedY(0); keep whatever is stored.
            l.VerticalMode = (VerticalMode)tree.GetInt("vmode", (int)VerticalMode.FixedY);
            l.TreatFluidAsSurface = tree.GetBool("fluidSurface", true);
            l.ShowBuildFeedback = tree.GetBool("buildFeedback", true);
            l.ColorIndex = tree.GetInt("color", 0);
            l.Enabled = tree.GetBool("enabled", true);
            // Trees written before these existed have neither key: 1 is the old behaviour exactly.
            l.Thickness = tree.GetInt("thickness", 1);
            l.Height = tree.GetInt("height", 1);
            l.FillToLevel = tree.GetBool("fillToLevel", false);   // trees before 2026-09-07 have no key → off
            l.ColorRgb = tree.GetInt("colorRgb", -1);               // absent → ResolveColor falls back to colorIndex
            return l;
        }

        /// <summary>
        /// Server-side validation mirroring the Geometry library's own rules (Lattice.RequireHalfStepRadius /
        /// RequireWithinMax, Ring inner &lt; outer, Rectangle sides ≥ 1 and effective side ≤ 2·maxRadius+1,
        /// RegularPolygon sides ≥ 3 and circumradius ≤ maxRadius, Spiral end radius ≤ maxRadius) so that
        /// LayerGeometry.Update never has to reject what the server stored.
        /// </summary>
        public void Clamp(ProjectorConfig cfg)
        {
            int max = cfg.maxRadius;
            if (!Enum.IsDefined(typeof(ShapeType), Shape)) Shape = ShapeType.Circle;
            if (!Enum.IsDefined(typeof(VerticalMode), VerticalMode)) VerticalMode = VerticalMode.FixedY;

            Radius = ProjectorParams.ToHalfStep(Radius, 0.5, max);
            InnerRadius = ProjectorParams.ToHalfStep(InnerRadius, 0.5, max);
            if (InnerRadius >= Radius)
            {
                if (Radius < 1) Radius = 1;
                InnerRadius = Radius - 0.5;
            }

            RadiusX = ProjectorParams.ToHalfStep(RadiusX, 0.5, max);
            RadiusZ = ProjectorParams.ToHalfStep(RadiusZ, 0.5, max);

            // Effective side ≤ 2*max+1 (Rectangle.Rasterize: (w-1)/2 ≤ maxRadius); parity may add 1, so cap the request at 2*max.
            Width = Math.Clamp(Width, 1, 2 * max);
            Depth = Math.Clamp(Depth, 1, 2 * max);

            Sides = Math.Clamp(Sides, 3, 64);
            Circumradius = ProjectorParams.ToHalfStep(Circumradius, 0.5, max);
            if (!double.IsFinite(RotationDeg)) RotationDeg = 0;
            RotationDeg = Math.Round(RotationDeg, 1);
            RotationDeg = ((RotationDeg % 360) + 360) % 360;

            if (!double.IsFinite(Turns) || Turns <= 0) Turns = 1;
            Turns = Math.Clamp(Math.Round(Turns * 4) / 4, 0.25, 64);
            StartRadius = ProjectorParams.ToHalfStep(StartRadius, 0, max);
            Spacing = ProjectorParams.ToHalfStep(Spacing, 0, max);
            // End radius = startRadius + spacing*turns must not exceed maxRadius (Spiral.RasterizePath contract).
            if (StartRadius + Spacing * Turns > max)
            {
                Turns = Spacing > 0 ? Math.Max(0.25, Math.Floor((max - StartRadius) / Spacing * 4) / 4) : Turns;
                if (StartRadius + Spacing * Turns > max) Spacing = 0;
            }

            YOffset = Math.Clamp(YOffset, -512, 512);
            ColorIndex = GhostPalette.ClampIndex(ColorIndex);
            ColorRgb = GhostPalette.SanitizeRgb(ResolveColor());
            // Lattice.RequireThickness only demands >= 1; the ceilings are this mod's cost guards.
            Thickness = Math.Clamp(Thickness, 1, MaxThickness(cfg));
            Height = Math.Clamp(Height, 1, cfg.maxLayerHeight);
        }

        /// <summary>
        /// The largest thickness this layer can use (user request 2026-09-07: "thickness maximum up
        /// to the radius"): the figure's own outer extent in blocks, or the config ceiling if that is
        /// lower. Thickness eats inward from the outer edge, so at this value the figure is solid —
        /// a disc, a filled rectangle, a filled polygon (Thickness.ThickenClosed saturates there and a
        /// larger number changes nothing, which is why the GUI clamps to it: the field shows the
        /// number that actually means "solid"). A ring's band cannot be wider than the ring itself;
        /// a spiral repeats its track inward, so its outer radius bounds it.
        /// </summary>
        public int MaxThickness(ProjectorConfig cfg)
        {
            double extent = Shape switch
            {
                ShapeType.Circle => Radius,
                ShapeType.Ring => Radius - InnerRadius,
                ShapeType.Ellipse => Math.Max(RadiusX, RadiusZ),
                ShapeType.Rectangle => Math.Max(Width, Depth) / 2.0,
                ShapeType.Polygon => Circumradius,
                ShapeType.Spiral => Math.Max(1, StartRadius + Spacing * Turns),
                _ => Radius,
            };
            int solid = Math.Max(1, (int)Math.Ceiling(extent + 0.5));
            return Math.Max(1, Math.Min(solid, cfg.maxOutlineThickness));
        }

        /// <summary>The Geometer's value-equal spec for this layer (ShapeSpec.cs:25-65).</summary>
        public ShapeSpec? ToShapeSpec() => Shape switch
        {
            ShapeType.Circle => new CircleSpec(Radius),
            ShapeType.Ring => new RingSpec(InnerRadius, Radius),
            ShapeType.Ellipse => new EllipseSpec(RadiusX, RadiusZ),
            ShapeType.Rectangle => new RectangleSpec(Width, Depth),
            ShapeType.Polygon => new RegularPolygonSpec(Sides, Circumradius, RotationDeg),
            ShapeType.Spiral => new SpiralSpec(Turns, Spacing, StartRadius, Clockwise),
            _ => null,
        };

        /// <summary>
        /// §10d radial adjustment: applies the Geometer's per-shape semantics (RadialAdjust.Adjust,
        /// RadialAdjust.cs) to this layer's parameters. Clamped/NotApplicable leave every field
        /// untouched (the whole-layer stays-put rule); the caller surfaces the outcome visibly.
        /// Y offset untouched by design (spec §10d).
        /// </summary>
        public RadialAdjustOutcome AdjustRadial(int direction)
        {
            ShapeSpec? spec = ToShapeSpec();
            if (spec == null) return RadialAdjustOutcome.NotApplicable;
            RadialAdjustResult r = RadialAdjust.Adjust(spec, direction);
            if (r.Outcome == RadialAdjustOutcome.Adjusted)
            {
                switch (r.Spec)
                {
                    case CircleSpec c: Radius = c.Radius; break;
                    case RingSpec g: InnerRadius = g.InnerRadius; Radius = g.OuterRadius; break;
                    case EllipseSpec e: RadiusX = e.RadiusX; RadiusZ = e.RadiusZ; break;
                    case RectangleSpec rc: Width = rc.Width; Depth = rc.Depth; break;
                    case RegularPolygonSpec pg: Circumradius = pg.Circumradius; break;
                }
            }
            return r.Outcome;
        }

        /// <summary>
        /// Thickness/height suffix for the layer list and the block-info HUD, e.g. " t3 h5"; empty when
        /// both are 1. Deliberately NOT part of <see cref="SizeSummary"/>: the radial tooltips print a
        /// before → after pair of size summaries, and neither of those two ever changes there.
        /// </summary>
        public string ExtentSummary()
        {
            string s = "";
            if (Thickness > 1) s += " t" + Thickness;
            if (Height > 1) s += " h" + Height;
            if (FillToLevel) s += " fill";
            return s;
        }

        /// <summary>Short size description for layer names and the block-info HUD, e.g. "r=11", "10×6".</summary>
        public string SizeSummary()
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            return Shape switch
            {
                ShapeType.Circle => "r=" + Radius.ToString("0.#", ci),
                ShapeType.Ring => "r=" + InnerRadius.ToString("0.#", ci) + "–" + Radius.ToString("0.#", ci),
                ShapeType.Ellipse => RadiusX.ToString("0.#", ci) + "×" + RadiusZ.ToString("0.#", ci),
                ShapeType.Rectangle => Width + "×" + Depth,
                ShapeType.Polygon => Sides + "-gon r=" + Circumradius.ToString("0.#", ci),
                ShapeType.Spiral => Turns.ToString("0.##", ci) + "× s=" + Spacing.ToString("0.#", ci),
                _ => "",
            };
        }
    }

    /// <summary>Everything the projector stores (spec §2/§3/§4): centre offset + ordered layers.</summary>
    public class ProjectorParams
    {
        /// <summary>
        /// Projector master switch (user ruling 2026-09-01, v2 amendment): off = the whole projector
        /// projects nothing — renderer cells, centre marker and the emissive variant all follow via
        /// EnabledLayerCount() returning 0. Per-layer Enabled flags are preserved underneath.
        /// Server-authoritative like every other parameter.
        /// </summary>
        public bool Enabled = true;

        /// <summary>
        /// Per-projector hologram switch (user request 2026-09-02): turns the §10c miniature above the
        /// block off on its own, leaving the world outlines projecting. Independent of the master
        /// <see cref="Enabled"/> switch, of the client hide hotkey and of the holoMode config — any of
        /// those can hide the mini as well; this is the one that travels with the block.
        /// Server-authoritative like every other parameter.
        /// </summary>
        public bool HologramEnabled = true;

        /// <summary>
        /// Per-projector world-marks switch (user request 2026-09-07): off = the full-size ghost
        /// cubes in the world are not drawn while the hologram above the block keeps showing the
        /// same figures — "hologram only". The mirror image of <see cref="HologramEnabled"/>. Unlike
        /// that one it IS in RenderKey: the world renderer is handed an empty list, so what it holds
        /// changes with the switch. Server-authoritative like every other parameter.
        /// </summary>
        public bool ProjectionEnabled = true;

        /// <summary>
        /// Opacity of the world ghost cubes in percent (user request 2026-09-07), 5..100. Baked into
        /// the vertex colours at mesh build (GhostPalette.AlphaFor), hence in RenderKey. The hologram
        /// does not follow it. Per projector, server-authoritative, so every player sees the same.
        /// </summary>
        public int GhostOpacity = GhostPalette.DefaultOpacityPercent;

        /// <summary>
        /// Surroundings model (user request 2026-09-07, "a model of your home and work"): the
        /// hologram also shows the standing blocks around the projector — every solid block the
        /// outside sees within <see cref="TerrainMapRadius"/> horizontally and <see cref="TerrainMapHeight"/>
        /// above or below it, each in its real block colour — so the coloured marks sit inside a
        /// miniature of what is already built. Hologram only, whether the world marks are on or off
        /// (user ruling, same day: it is never drawn in the world).
        /// </summary>
        public bool TerrainMap = false;
        /// <summary>
        /// Whether the hologram shows the figures at all (user request 2026-09-07: "hide the model
        /// but show the environment ... it surveys the land"). Off, the miniature is the surroundings
        /// model alone; the world marks are untouched. In RenderKey so the toggle re-fires the cell
        /// cache event the hologram rebuilds on.
        /// </summary>
        public bool HologramFigures = true;
        public int TerrainMapRadius = 16;
        public int TerrainMapHeight = 8;

        /// <summary>Centre offset from the projector block, 0.5 steps (spec §3). Shared by all layers (spec §4).</summary>
        public double Dx = 0;
        public double Dz = 0;
        public List<LayerParams> Layers = new List<LayerParams>();

        public static ProjectorParams Default()
        {
            ProjectorParams p = new ProjectorParams();
            LayerParams l = new LayerParams();
            l.ApplyShapeDefaults();
            p.Layers.Add(l);
            return p;
        }

        public ProjectorParams Clone()
        {
            ProjectorParams p = new ProjectorParams
            {
                Dx = Dx, Dz = Dz, Enabled = Enabled, HologramEnabled = HologramEnabled,
                ProjectionEnabled = ProjectionEnabled, GhostOpacity = GhostOpacity,
                TerrainMap = TerrainMap, TerrainMapRadius = TerrainMapRadius, TerrainMapHeight = TerrainMapHeight, HologramFigures = HologramFigures,
            };
            foreach (LayerParams l in Layers) p.Layers.Add(l.Clone());
            return p;
        }

        public int EnabledLayerCount()
        {
            if (!Enabled) return 0;   // master switch: the projector as a whole is off
            int n = 0;
            foreach (LayerParams l in Layers) if (l.Enabled) n++;
            return n;
        }

        public void ToTree(ITreeAttribute tree)
        {
            tree.SetBool("projectorEnabled", Enabled);
            tree.SetBool("hologramEnabled", HologramEnabled);
            tree.SetBool("projectionEnabled", ProjectionEnabled);
            tree.SetInt("ghostOpacity", GhostOpacity);
            tree.SetBool("terrainMap", TerrainMap);
            tree.SetBool("hologramFigures", HologramFigures);
            tree.SetInt("terrainMapRadius", TerrainMapRadius);
            tree.SetInt("terrainMapHeight", TerrainMapHeight);
            tree.SetDouble("dx", Dx);
            tree.SetDouble("dz", Dz);
            tree.SetInt("layerCount", Layers.Count);
            for (int i = 0; i < Layers.Count; i++)
            {
                // ITreeAttribute GetOrAddTreeAttribute(string key) — api-notes §i.1 (ITreeAttribute.cs:250).
                Layers[i].ToTree(tree.GetOrAddTreeAttribute("layer" + i));
            }
            // Drop stale sub-trees left over from a longer list (RemoveAttribute — ITreeAttribute.cs:48).
            for (int i = Layers.Count; i < 64; i++)
            {
                if (!tree.HasAttribute("layer" + i)) break;
                tree.RemoveAttribute("layer" + i);
            }
        }

        public static ProjectorParams FromTree(ITreeAttribute tree)
        {
            ProjectorParams p = new ProjectorParams();
            p.Enabled = tree.GetBool("projectorEnabled", true);   // trees from v1 have no key → on
            p.HologramEnabled = tree.GetBool("hologramEnabled", true);   // same: absent → on
            p.ProjectionEnabled = tree.GetBool("projectionEnabled", true);   // 2026-09-07 keys: absent → the old behaviour
            p.GhostOpacity = tree.GetInt("ghostOpacity", GhostPalette.DefaultOpacityPercent);
            p.TerrainMap = tree.GetBool("terrainMap", false);
            p.HologramFigures = tree.GetBool("hologramFigures", true);
            p.TerrainMapRadius = tree.GetInt("terrainMapRadius", 16);
            p.TerrainMapHeight = tree.GetInt("terrainMapHeight", 8);
            // terrainColorRgb / waterColorRgb / terrainTrueColor (same-day keys, retired the same day —
            // the model always wears the real block colours now) are ignored if present.
            p.Dx = tree.GetDouble("dx", 0);
            p.Dz = tree.GetDouble("dz", 0);
            int n = tree.GetInt("layerCount", -1);
            if (n < 0)
            {
                // Tree written before this format existed (step-1 block entities): one default layer.
                return Default();
            }
            for (int i = 0; i < n; i++)
            {
                // ITreeAttribute GetTreeAttribute(string key) — api-notes §i.1 (ITreeAttribute.cs:242); null when absent.
                ITreeAttribute? sub = tree.GetTreeAttribute("layer" + i);
                if (sub != null) p.Layers.Add(LayerParams.FromTree(sub));
            }
            return p;
        }

        /// <summary>Server-side validation: half-step offsets, radius/layer caps (spec §7).</summary>
        public void Clamp(ProjectorConfig cfg)
        {
            Dx = ToHalfStep(Dx, -cfg.maxRadius, cfg.maxRadius);
            Dz = ToHalfStep(Dz, -cfg.maxRadius, cfg.maxRadius);
            GhostOpacity = Math.Clamp(GhostOpacity, MinOpacityPercent, 100);
            TerrainMapRadius = Math.Clamp(TerrainMapRadius, 1, cfg.maxTerrainMapRadius);
            TerrainMapHeight = Math.Clamp(TerrainMapHeight, 1, cfg.maxTerrainMapHeight);
            if (Layers.Count > cfg.maxLayersPerProjector)
            {
                Layers.RemoveRange(cfg.maxLayersPerProjector, Layers.Count - cfg.maxLayersPerProjector);
            }
            foreach (LayerParams l in Layers) l.Clamp(cfg);
        }

        /// <summary>Lowest GhostOpacity the GUI and the server accept — a mark you cannot see is a mark you forget is there.</summary>
        public const int MinOpacityPercent = 5;

        /// <summary>Rounds to the nearest multiple of 0.5 and clamps; NaN/Infinity become the minimum.</summary>
        public static double ToHalfStep(double v, double min, double max)
        {
            if (!double.IsFinite(v)) v = min;
            v = Math.Round(v * 2.0, MidpointRounding.AwayFromZero) / 2.0;
            return Math.Clamp(v, min, max);
        }

        /// <summary>A stable string of everything that affects the ghost mesh; changes ⇒ rebuild (spec §5).</summary>
        public string RenderKey()
        {
            var sb = new System.Text.StringBuilder();
            CultureInfo ci = CultureInfo.InvariantCulture;
            sb.Append(Enabled ? 1 : 0).Append(',').Append(Dx.ToString(ci)).Append(',').Append(Dz.ToString(ci))
              // 2026-09-07: opacity is baked into the vertex colours; the world-marks switch decides
              // whether the world renderer gets the cells at all and whether the surroundings model
              // is built; the model's own extent changes the cell set.
              .Append(',').Append(ProjectionEnabled ? 1 : 0).Append(',').Append(GhostOpacity)
              .Append(',').Append(TerrainMap ? 1 : 0).Append(',').Append(TerrainMapRadius).Append(',').Append(TerrainMapHeight)
              .Append(',').Append(HologramFigures ? 1 : 0);
            foreach (LayerParams l in Layers)
            {
                sb.Append('|').Append((int)l.Shape)
                  .Append(',').Append(l.Radius.ToString(ci)).Append(',').Append(l.InnerRadius.ToString(ci))
                  .Append(',').Append(l.RadiusX.ToString(ci)).Append(',').Append(l.RadiusZ.ToString(ci))
                  .Append(',').Append(l.Width).Append(',').Append(l.Depth)
                  .Append(',').Append(l.Sides).Append(',').Append(l.Circumradius.ToString(ci)).Append(',').Append(l.RotationDeg.ToString(ci))
                  .Append(',').Append(l.Turns.ToString(ci)).Append(',').Append(l.Spacing.ToString(ci)).Append(',').Append(l.StartRadius.ToString(ci)).Append(',').Append(l.Clockwise ? 1 : 0)
                  .Append(',').Append(l.YOffset).Append(',').Append((int)l.VerticalMode).Append(',').Append(l.TreatFluidAsSurface ? 1 : 0).Append(',').Append(l.ShowBuildFeedback ? 1 : 0).Append(',').Append(l.ResolveColor())
                  .Append(',').Append(l.Enabled ? 1 : 0)
                  // Both change the cell set, so both must invalidate the cached mesh. HologramEnabled
                  // deliberately does NOT: the hologram reads it per frame, no geometry depends on it.
                  .Append(',').Append(l.Thickness).Append(',').Append(l.Height).Append(',').Append(l.FillToLevel ? 1 : 0);
            }
            return sb.ToString();
        }
    }
}
