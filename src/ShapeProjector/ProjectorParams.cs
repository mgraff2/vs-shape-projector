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
        public const int Alpha = 110;

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

        public static int IndexOf(string code)
        {
            for (int i = 0; i < Entries.Length; i++) if (Entries[i].Code == code) return i;
            return 0;
        }

        /// <summary>Packed RGBA for the vertex colour attribute.</summary>
        public static int Color(int index)
        {
            var e = Entries[ClampIndex(index)];
            // ColorUtil.ColorFromRgba(int r, int g, int b, int a) — "true RGBA order" — api-notes §d.3 (ColorUtil.cs:276-281).
            return ColorUtil.ColorFromRgba(e.R, e.G, e.B, Alpha);
        }

        /// <summary>Spec §6 "done" tint: the palette green, same alpha as the layer colours.</summary>
        public static int DoneColor => Color(3);


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
        public int ColorIndex = 0;
        public bool Enabled = true;

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

        public LayerParams Clone() => (LayerParams)MemberwiseClone();

        /// <summary>Spec §7 colour default and §5a vertical-mode default for the current shape.</summary>
        public void ApplyShapeDefaults()
        {
            ColorIndex = GhostPalette.DefaultIndexFor(Shape);
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
            // Lattice.RequireThickness only demands >= 1; the ceilings are this mod's cost guards.
            Thickness = Math.Clamp(Thickness, 1, cfg.maxOutlineThickness);
            Height = Math.Clamp(Height, 1, cfg.maxLayerHeight);
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
            ProjectorParams p = new ProjectorParams { Dx = Dx, Dz = Dz, Enabled = Enabled, HologramEnabled = HologramEnabled };
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
            if (Layers.Count > cfg.maxLayersPerProjector)
            {
                Layers.RemoveRange(cfg.maxLayersPerProjector, Layers.Count - cfg.maxLayersPerProjector);
            }
            foreach (LayerParams l in Layers) l.Clamp(cfg);
        }

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
            sb.Append(Enabled ? 1 : 0).Append(',').Append(Dx.ToString(ci)).Append(',').Append(Dz.ToString(ci));
            foreach (LayerParams l in Layers)
            {
                sb.Append('|').Append((int)l.Shape)
                  .Append(',').Append(l.Radius.ToString(ci)).Append(',').Append(l.InnerRadius.ToString(ci))
                  .Append(',').Append(l.RadiusX.ToString(ci)).Append(',').Append(l.RadiusZ.ToString(ci))
                  .Append(',').Append(l.Width).Append(',').Append(l.Depth)
                  .Append(',').Append(l.Sides).Append(',').Append(l.Circumradius.ToString(ci)).Append(',').Append(l.RotationDeg.ToString(ci))
                  .Append(',').Append(l.Turns.ToString(ci)).Append(',').Append(l.Spacing.ToString(ci)).Append(',').Append(l.StartRadius.ToString(ci)).Append(',').Append(l.Clockwise ? 1 : 0)
                  .Append(',').Append(l.YOffset).Append(',').Append((int)l.VerticalMode).Append(',').Append(l.TreatFluidAsSurface ? 1 : 0).Append(',').Append(l.ShowBuildFeedback ? 1 : 0).Append(',').Append(l.ColorIndex)
                  .Append(',').Append(l.Enabled ? 1 : 0)
                  // Both change the cell set, so both must invalidate the cached mesh. HologramEnabled
                  // deliberately does NOT: the hologram reads it per frame, no geometry depends on it.
                  .Append(',').Append(l.Thickness).Append(',').Append(l.Height);
            }
            return sb.ToString();
        }
    }
}
