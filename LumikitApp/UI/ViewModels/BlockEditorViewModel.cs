using System;
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
    private bool? _fadeIn, _fadeOut, _strobe, _travel, _combine, _separate, _repeat, _changeColor, _twinkle;

    // ── Slider backing fields ─────────────────────────────────────────────────
    private double _lightRangeLower, _lightRangeUpper = 1000;
    private double _range2Slider1Lower, _range2Slider1Upper = 1000;
    private double _range2Slider2Lower, _range2Slider2Upper = 1000;

    // ── Text / color / UI state ───────────────────────────────────────────────
    private string _intensityText        = "";
    private string _additionalInput1Text = "";
    private string _additionalInput2Text = "";
    private bool   _editorVisible;
    private int    _selectedTabIndex;
    private IBrush _blockColorBrush       = Brushes.BlueViolet;
    private IBrush _secondBlockColorBrush = Brushes.Transparent;

    // ── Effect checkboxes ─────────────────────────────────────────────────────
    public bool? FadeIn    { get => _fadeIn;    set { _fadeIn    = value; Notify(); } }
    public bool? FadeOut   { get => _fadeOut;   set { _fadeOut   = value; Notify(); } }
    public bool? Strobe    { get => _strobe;    set { _strobe    = value; Notify(); } }
    public bool? Twinkle   { get => _twinkle;   set { _twinkle   = value; Notify(); } }

    // Travel, Combine, Separate are mutually exclusive — setting one clears the others
    public bool? Travel
    {
        get => _travel;
        set
        {
            _travel = value;
            if (value == true) { _combine = false; _separate = false; Notify(nameof(Combine)); Notify(nameof(Separate)); }
            Notify();
            UpdateVisibility();
        }
    }

    public bool? Combine
    {
        get => _combine;
        set
        {
            _combine = value;
            if (value == true) { _travel = false; _separate = false; Notify(nameof(Travel)); Notify(nameof(Separate)); }
            Notify();
            UpdateVisibility();
        }
    }

    public bool? Separate
    {
        get => _separate;
        set
        {
            _separate = value;
            if (value == true) { _travel = false; _combine = false; Notify(nameof(Travel)); Notify(nameof(Combine)); }
            Notify();
            UpdateVisibility();
        }
    }

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

    // ── Range sliders ─────────────────────────────────────────────────────────
    public double LightRangeLower    { get => _lightRangeLower;    set { _lightRangeLower    = value; Notify(); } }
    public double LightRangeUpper    { get => _lightRangeUpper;    set { _lightRangeUpper    = value; Notify(); } }
    public double Range2Slider1Lower { get => _range2Slider1Lower; set { _range2Slider1Lower = value; Notify(); } }
    public double Range2Slider1Upper { get => _range2Slider1Upper; set { _range2Slider1Upper = value; Notify(); } }
    public double Range2Slider2Lower { get => _range2Slider2Lower; set { _range2Slider2Lower = value; Notify(); } }
    public double Range2Slider2Upper { get => _range2Slider2Upper; set { _range2Slider2Upper = value; Notify(); } }

    // ── Text inputs ───────────────────────────────────────────────────────────
    public string IntensityText        { get => _intensityText;        set { _intensityText        = value; Notify(); } }
    public string AdditionalInput1Text { get => _additionalInput1Text; set { _additionalInput1Text = value; Notify(); } }
    public string AdditionalInput2Text { get => _additionalInput2Text; set { _additionalInput2Text = value; Notify(); } }

    // ── Visibility (computed in UpdateVisibility) ─────────────────────────────
    public bool EditorVisible            { get => _editorVisible; set { _editorVisible = value; Notify(); } }
    public bool LightRangeVisible        { get; private set; }
    public bool LightRange2Visible       { get; private set; }
    public bool SecondColorPanelVisible  { get; private set; }
    public bool AdditionalInput1PanelVisible { get; private set; }
    public bool AdditionalInput2PanelVisible { get; private set; }

    // ── Labels (computed in UpdateVisibility) ─────────────────────────────────
    public string RangeSlider1Label     { get; private set; } = "Start Lightspan";
    public string RangeSlider2Label     { get; private set; } = "End Lightspan";
    public string AdditionalInput1Label { get; private set; } = "";
    public string AdditionalInput2Label { get; private set; } = "";

    // ── Colors ────────────────────────────────────────────────────────────────
    public IBrush BlockColorBrush       { get => _blockColorBrush;       set { _blockColorBrush       = value; Notify(); } }
    public IBrush SecondBlockColorBrush { get => _secondBlockColorBrush; set { _secondBlockColorBrush = value; Notify(); } }

    // ── Tab ───────────────────────────────────────────────────────────────────
    public int SelectedTabIndex { get => _selectedTabIndex; set { _selectedTabIndex = value; Notify(); } }

    // ── Methods ───────────────────────────────────────────────────────────────

    public void RequestPreview() => PreviewRequested?.Invoke();

    // Load all nine effects at once, bypassing mutual-exclusion side effects.
    // Used when restoring a saved block so state is set exactly as stored.
    public void LoadEffects(bool? fadeIn, bool? fadeOut, bool? strobe, bool? travel,
                            bool? combine, bool? separate, bool? repeat, bool? changeColor, bool? twinkle)
    {
        _fadeIn = fadeIn; _fadeOut = fadeOut; _strobe = strobe;
        _travel = travel; _combine = combine; _separate = separate;
        _repeat = repeat; _changeColor = changeColor; _twinkle = twinkle;

        Notify(nameof(FadeIn));  Notify(nameof(FadeOut)); Notify(nameof(Strobe));
        Notify(nameof(Travel));  Notify(nameof(Combine)); Notify(nameof(Separate));
        Notify(nameof(Repeat));  Notify(nameof(ChangeColor)); Notify(nameof(Twinkle));

        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        bool dualRange  = _travel == true || _combine == true || _separate == true;
        bool combineSep = _combine == true || _separate == true;

        LightRangeVisible            = !dualRange;
        LightRange2Visible           = dualRange;
        SecondColorPanelVisible      = _changeColor == true;
        AdditionalInput1PanelVisible = combineSep;
        AdditionalInput2PanelVisible = _repeat == true;

        RangeSlider1Label = combineSep ? "Lightspan 1"      : "Start Lightspan";
        RangeSlider2Label = combineSep ? "Lightspan 2"      : "End Lightspan";

        if (combineSep)
        {
            AdditionalInput1Label = "Combined Width (0-1000)";
        }
        else
        {
            AdditionalInput1Label  = "";
            _additionalInput1Text  = "";
            Notify(nameof(AdditionalInput1Text));
        }

        if (_repeat == true)
        {
            AdditionalInput2Label = "Repeat Number";
        }
        else
        {
            AdditionalInput2Label  = "";
            _additionalInput2Text  = "";
            Notify(nameof(AdditionalInput2Text));
        }

        Notify(nameof(LightRangeVisible));
        Notify(nameof(LightRange2Visible));
        Notify(nameof(SecondColorPanelVisible));
        Notify(nameof(AdditionalInput1PanelVisible));
        Notify(nameof(AdditionalInput2PanelVisible));
        Notify(nameof(RangeSlider1Label));
        Notify(nameof(RangeSlider2Label));
        Notify(nameof(AdditionalInput1Label));
        Notify(nameof(AdditionalInput2Label));
    }

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}