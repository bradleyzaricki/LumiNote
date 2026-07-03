using System.Collections.Generic;
using System.Linq;

namespace LumikitApp;

/// <summary>How an effect participates in rendering.</summary>
public enum EffectCategory
{
    /// <summary>Defines which pixels are lit. Mutually exclusive — a block has exactly one shape.</summary>
    Shape,

    /// <summary>Per-pixel surface modulation (e.g. Twinkle) applied over the lit pixels —
    /// optionally including the fill color. Mutually exclusive — one texture at a time.</summary>
    Texture,

    /// <summary>Post-processes the shape's output. Any number can be active at once.</summary>
    Modifier,
}

/// <summary>Which editor control a parameter is rendered with in the block editor.</summary>
public enum ParamControl
{
    NumberBox,

    /// <summary>Boolean stored as 0/1 in EffectData.Params.</summary>
    CheckBox,
}

/// <summary>One editable parameter of an effect. Key indexes into EffectData.Params.</summary>
public sealed record EffectParamDefinition(
    string Key,
    string Title,
    ParamControl Control,
    double Default,
    double Min,
    double Max);

/// <summary>Static description of an effect: category, editor title, and parameter schema.</summary>
public sealed record EffectDefinition(
    LightBlock.Effect Type,
    string Title,
    EffectCategory Category,
    IReadOnlyList<EffectParamDefinition> Parameters)
{
    public EffectDefinition(LightBlock.Effect type, string title, EffectCategory category,
        params EffectParamDefinition[] parameters)
        : this(type, title, category, (IReadOnlyList<EffectParamDefinition>)parameters) { }
}

/// <summary>
/// Single source of truth for what effects exist, which category they belong to, and what
/// params they expose. The block editor generates its param inputs from this — to give an
/// effect a new param, declare it here and read it in LightEffectsComputer; the sidebar UI
/// follows automatically.
/// </summary>
public static class EffectCatalog
{
    private static readonly EffectDefinition[] Definitions =
    {
        new(LightBlock.Effect.Travel,   "Travel",   EffectCategory.Shape),
        new(LightBlock.Effect.Combine,  "Combine",  EffectCategory.Shape,
            new EffectParamDefinition("TargetWidth", "Combined Width (0-1000)", ParamControl.NumberBox, 0, 0, 1000)),
        new(LightBlock.Effect.Seperate, "Seperate", EffectCategory.Shape,
            new EffectParamDefinition("TargetWidth", "Combined Width (0-1000)", ParamControl.NumberBox, 0, 0, 1000)),
        new(LightBlock.Effect.Scanner,  "Scanner",  EffectCategory.Shape,
            new EffectParamDefinition("Width",  "Bar Width (0-1000)",  ParamControl.NumberBox, 50, 1, 1000),
            new EffectParamDefinition("Cycles", "Cycles (per block)",  ParamControl.NumberBox, 4,  1, 100),
            new EffectParamDefinition("Wrap",   "Wrap Around",         ParamControl.CheckBox,  0,  0, 1)),

        new(LightBlock.Effect.Twinkle, "Twinkle", EffectCategory.Texture,
            new EffectParamDefinition("AffectFill", "Affect Fill Color", ParamControl.CheckBox, 1, 0, 1)),
        new(LightBlock.Effect.Shimmer, "Shimmer", EffectCategory.Texture,
            new EffectParamDefinition("Wavelength", "Wavelength (px)",     ParamControl.NumberBox, 120, 5, 1000),
            new EffectParamDefinition("Speed",      "Scroll Speed",        ParamControl.NumberBox, 2,   0, 50),
            new EffectParamDefinition("Depth",      "Depth (0-100)",       ParamControl.NumberBox, 60,  0, 100),
            new EffectParamDefinition("AffectFill", "Affect Fill Color",   ParamControl.CheckBox,  1,   0, 1)),
        new(LightBlock.Effect.Sparkle, "Sparkle", EffectCategory.Texture,
            new EffectParamDefinition("Density",    "Density (0-100)",     ParamControl.NumberBox, 10,  1, 100),
            new EffectParamDefinition("Speed",      "Speed",               ParamControl.NumberBox, 20,  1, 200),
            new EffectParamDefinition("AffectFill", "Affect Fill Color",   ParamControl.CheckBox,  1,   0, 1)),

        new(LightBlock.Effect.FadeIn,      "Fade In",      EffectCategory.Modifier),
        new(LightBlock.Effect.FadeOut,     "Fade Out",     EffectCategory.Modifier),
        new(LightBlock.Effect.Strobe,      "Strobe",       EffectCategory.Modifier),
        new(LightBlock.Effect.Repeat,      "Repeat",       EffectCategory.Modifier,
            new EffectParamDefinition("Count", "Repeat Number", ParamControl.NumberBox, 1, 1, 1000)),
        new(LightBlock.Effect.ChangeColor, "Color Change", EffectCategory.Modifier),
        new(LightBlock.Effect.Comet,       "Comet",        EffectCategory.Modifier,
            new EffectParamDefinition("TailLength", "Tail Length (px)", ParamControl.NumberBox, 80, 1, 1000)),
        new(LightBlock.Effect.FillColor,   "Fill Color",   EffectCategory.Modifier),
    };

    private static readonly Dictionary<LightBlock.Effect, EffectDefinition> ByType =
        Definitions.ToDictionary(d => d.Type);

    public static IReadOnlyList<EffectDefinition> All => Definitions;

    public static IEnumerable<EffectDefinition> Shapes =>
        Definitions.Where(d => d.Category == EffectCategory.Shape);

    public static IEnumerable<EffectDefinition> Textures =>
        Definitions.Where(d => d.Category == EffectCategory.Texture);

    public static IEnumerable<EffectDefinition> Modifiers =>
        Definitions.Where(d => d.Category == EffectCategory.Modifier);

    public static EffectDefinition? Get(LightBlock.Effect type) =>
        ByType.GetValueOrDefault(type);

    public static bool IsShape(LightBlock.Effect type) =>
        ByType.TryGetValue(type, out var d) && d.Category == EffectCategory.Shape;

    public static bool IsTexture(LightBlock.Effect type) =>
        ByType.TryGetValue(type, out var d) && d.Category == EffectCategory.Texture;

    /// <summary>New EffectData seeded with the catalog's default param values.</summary>
    public static EffectData CreateData(LightBlock.Effect type)
    {
        var data = new EffectData { Type = type };
        var def = Get(type);
        if (def != null)
            foreach (var p in def.Parameters)
                data.Params[p.Key] = p.Default;
        return data;
    }
}