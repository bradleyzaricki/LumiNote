using System;
using System.ComponentModel;

namespace LumikitApp.ViewModels;

/// <summary>
/// One dynamically generated param editor row (title + input) for an active effect.
/// BlockEditorPanel creates these from the EffectCatalog schema whenever the active effect
/// set changes; user edits raise ValueChanged so the panel can apply them to the blocks.
/// </summary>
public class EffectParamViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the user edits the value (not on initial load).</summary>
    public event Action<EffectParamViewModel>? ValueChanged;

    public LightBlock.Effect Effect { get; }
    public EffectParamDefinition Definition { get; }
    public string Title => Definition.Title;
    public ParamControl Control => Definition.Control;

    // Template selectors for the editor's ItemsControl — one per ParamControl kind.
    public bool IsNumberBox => Definition.Control == ParamControl.NumberBox;
    public bool IsCheckBox  => Definition.Control == ParamControl.CheckBox;

    private string _text;

    public EffectParamViewModel(LightBlock.Effect effect, EffectParamDefinition definition, double initialValue)
    {
        Effect = effect;
        Definition = definition;
        _text = ((int)initialValue).ToString();
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BoolValue)));
            ValueChanged?.Invoke(this);
        }
    }

    /// <summary>CheckBox params: the value as a bool, stored as 0/1.</summary>
    public bool BoolValue
    {
        get => Value is > 0;
        set => Text = value ? "1" : "0";
    }

    /// <summary>Parsed and clamped value, or null while the text isn't a valid number.</summary>
    public double? Value =>
        double.TryParse(_text, out var v) ? Math.Clamp(v, Definition.Min, Definition.Max) : null;
}