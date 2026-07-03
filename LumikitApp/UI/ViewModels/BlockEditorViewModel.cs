using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace LumikitApp.ViewModels;

public class BlockEditorViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // Raised when the editor wants the window to restart the block preview
    public event Action? PreviewRequested;

    // ── Effect backing fields ─────────────────────────────────────────────────
    // Shape (Travel/Combine/Seperate) and Texture (Twinkle) are each mutually exclusive
    // and modeled as a single selection: Effect.None = none, null = mixed multi-selection
    // (leave untouched).
    private LightBlock.Effect? _selectedShape   = LightBlock.Effect.None;
    private LightBlock.Effect? _selectedTexture = LightBlock.Effect.None;
    private bool? _fadeIn, _fadeOut, _strobe, _repeat, _changeColor, _fillColor, _comet;

    // ── Slider backing fields ─────────────────────────────────────────────────
    private double _lightRangeLower, _lightRangeUpper = 1000;
    private double _range2Slider1Lower, _range2Slider1Upper = 1000;
    private double _range2Slider2Lower, _range2Slider2Upper = 1000;

    // ── Text / color / UI state ───────────────────────────────────────────────
    private string _intensityText = "";
    private bool   _editorVisible;
    private int    _selectedTabIndex;
    private IBrush _blockColorBrush       = Brushes.BlueViolet;
    private IBrush _secondBlockColorBrush = Brushes.Transparent;
    private IBrush _fillColorBrush        = Brushes.Transparent;

    // ── Shape selection ───────────────────────────────────────────────────────
    public LightBlock.Effect? SelectedShape
    {
        get => _selectedShape;
        set
        {
            if (_selectedShape == value) return;
            _selectedShape = value;
            Notify();
            NotifyShapeRadios();
            UpdateVisibility();
        }
    }

    // Radio button wrappers — setting one true routes through SelectedShape;
    // the false writes radio groups produce are ignored.
    public bool ShapeStatic   { get => _selectedShape == LightBlock.Effect.None;     set { if (value) SelectedShape = LightBlock.Effect.None; } }
    public bool ShapeTravel   { get => _selectedShape == LightBlock.Effect.Travel;   set { if (value) SelectedShape = LightBlock.Effect.Travel; } }
    public bool ShapeCombine  { get => _selectedShape == LightBlock.Effect.Combine;  set { if (value) SelectedShape = LightBlock.Effect.Combine; } }
    public bool ShapeSeparate { get => _selectedShape == LightBlock.Effect.Seperate; set { if (value) SelectedShape = LightBlock.Effect.Seperate; } }
    public bool ShapeScanner  { get => _selectedShape == LightBlock.Effect.Scanner;  set { if (value) SelectedShape = LightBlock.Effect.Scanner; } }

    private void NotifyShapeRadios()
    {
        Notify(nameof(ShapeStatic));
        Notify(nameof(ShapeTravel));
        Notify(nameof(ShapeCombine));
        Notify(nameof(ShapeSeparate));
        Notify(nameof(ShapeScanner));
    }

    // ── Texture selection ─────────────────────────────────────────────────────
    public LightBlock.Effect? SelectedTexture
    {
        get => _selectedTexture;
        set
        {
            if (_selectedTexture == value) return;
            _selectedTexture = value;
            Notify();
            NotifyTextureRadios();
        }
    }

    public bool TextureNone    { get => _selectedTexture == LightBlock.Effect.None;    set { if (value) SelectedTexture = LightBlock.Effect.None; } }
    public bool TextureTwinkle { get => _selectedTexture == LightBlock.Effect.Twinkle; set { if (value) SelectedTexture = LightBlock.Effect.Twinkle; } }
    public bool TextureShimmer { get => _selectedTexture == LightBlock.Effect.Shimmer; set { if (value) SelectedTexture = LightBlock.Effect.Shimmer; } }
    public bool TextureSparkle { get => _selectedTexture == LightBlock.Effect.Sparkle; set { if (value) SelectedTexture = LightBlock.Effect.Sparkle; } }

    private void NotifyTextureRadios()
    {
        Notify(nameof(TextureNone));
        Notify(nameof(TextureTwinkle));
        Notify(nameof(TextureShimmer));
        Notify(nameof(TextureSparkle));
    }

    // ── Modifier checkboxes ───────────────────────────────────────────────────
    public bool? FadeIn  { get => _fadeIn;  set { _fadeIn  = value; Notify(); } }
    public bool? FadeOut { get => _fadeOut; set { _fadeOut = value; Notify(); } }
    public bool? Strobe  { get => _strobe;  set { _strobe  = value; Notify(); } }
    public bool? Comet   { get => _comet;   set { _comet   = value; Notify(); } }

    public bool? Repeat
    {
        get => _repeat;
        set { _repeat = value; Notify(); UpdateVisibility(); }
    }

    public bool? ChangeColor
    {
        get => _changeColor;
        set { _changeColor = value; Notify(); UpdateVisibility(); }
    }

    public bool? FillColor
    {
        get => _fillColor;
        set { _fillColor = value; Notify(); UpdateVisibility(); }
    }

    // ── Dynamic effect params ─────────────────────────────────────────────────
    // One row per param of each active effect, generated from EffectCatalog by
    // BlockEditorPanel. The editor's ItemsControl renders these.
    public ObservableCollection<EffectParamViewModel> EffectParams { get; } = new();

    // ── Range sliders ─────────────────────────────────────────────────────────
    public double LightRangeLower    { get => _lightRangeLower;    set { _lightRangeLower    = value; Notify(); } }
    public double LightRangeUpper    { get => _lightRangeUpper;    set { _lightRangeUpper    = value; Notify(); } }
    public double Range2Slider1Lower { get => _range2Slider1Lower; set { _range2Slider1Lower = value; Notify(); } }
    public double Range2Slider1Upper { get => _range2Slider1Upper; set { _range2Slider1Upper = value; Notify(); } }
    public double Range2Slider2Lower { get => _range2Slider2Lower; set { _range2Slider2Lower = value; Notify(); } }
    public double Range2Slider2Upper { get => _range2Slider2Upper; set { _range2Slider2Upper = value; Notify(); } }

    // ── Text inputs ───────────────────────────────────────────────────────────
    public string IntensityText { get => _intensityText; set { _intensityText = value; Notify(); } }

    // ── Visibility (computed in UpdateVisibility) ─────────────────────────────
    public bool EditorVisible           { get => _editorVisible; set { _editorVisible = value; Notify(); } }
    public bool LightRangeVisible       { get; private set; }
    public bool LightRange2Visible      { get; private set; }
    public bool SecondColorPanelVisible { get; private set; }
    public bool FillColorPanelVisible   { get; private set; }

    // ── Labels (computed in UpdateVisibility) ─────────────────────────────────
    public string RangeSlider1Label { get; private set; } = "Start Lightspan";
    public string RangeSlider2Label { get; private set; } = "End Lightspan";

    // ── Colors ────────────────────────────────────────────────────────────────
    public IBrush BlockColorBrush       { get => _blockColorBrush;       set { _blockColorBrush       = value; Notify(); } }
    public IBrush SecondBlockColorBrush { get => _secondBlockColorBrush; set { _secondBlockColorBrush = value; Notify(); } }
    public IBrush FillColorBrush        { get => _fillColorBrush;        set { _fillColorBrush        = value; Notify(); } }

    // ── Tab ───────────────────────────────────────────────────────────────────
    public int SelectedTabIndex { get => _selectedTabIndex; set { _selectedTabIndex = value; Notify(); } }

    // ── Methods ───────────────────────────────────────────────────────────────

    public void RequestPreview() => PreviewRequested?.Invoke();

    // Load the shape, texture and all modifiers at once, bypassing setter side effects.
    // Used when restoring a saved block so state is set exactly as stored.
    public void LoadEffects(LightBlock.Effect? shape, LightBlock.Effect? texture,
                            bool? fadeIn, bool? fadeOut, bool? strobe,
                            bool? repeat, bool? changeColor, bool? fillColor, bool? comet)
    {
        _selectedShape = shape;
        _selectedTexture = texture;
        _fadeIn = fadeIn; _fadeOut = fadeOut; _strobe = strobe;
        _repeat = repeat; _changeColor = changeColor;
        _fillColor = fillColor; _comet = comet;

        Notify(nameof(SelectedShape));
        NotifyShapeRadios();
        Notify(nameof(SelectedTexture));
        NotifyTextureRadios();
        Notify(nameof(FadeIn));  Notify(nameof(FadeOut)); Notify(nameof(Strobe));
        Notify(nameof(Repeat));  Notify(nameof(ChangeColor));
        Notify(nameof(FillColor)); Notify(nameof(Comet));

        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        bool dualRange  = _selectedShape is LightBlock.Effect.Travel
                                         or LightBlock.Effect.Combine
                                         or LightBlock.Effect.Seperate;
        bool combineSep = _selectedShape is LightBlock.Effect.Combine
                                         or LightBlock.Effect.Seperate;

        LightRangeVisible       = !dualRange;
        LightRange2Visible      = dualRange;
        SecondColorPanelVisible = _changeColor == true;
        FillColorPanelVisible   = _fillColor == true;

        RangeSlider1Label = combineSep ? "Lightspan 1" : "Start Lightspan";
        RangeSlider2Label = combineSep ? "Lightspan 2" : "End Lightspan";

        Notify(nameof(LightRangeVisible));
        Notify(nameof(LightRange2Visible));
        Notify(nameof(SecondColorPanelVisible));
        Notify(nameof(FillColorPanelVisible));
        Notify(nameof(RangeSlider1Label));
        Notify(nameof(RangeSlider2Label));
    }

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}