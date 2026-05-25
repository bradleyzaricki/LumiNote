namespace LumikitApp;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Owns all block editor sidebar logic such as loading block values into the editor
/// and applying selected changes onto lightblocks.
/// Accesses all UI controls directly via the LumikitWindow reference.
/// </summary>
public class BlockEditorPanel
{
    private readonly LumikitWindow _window;
    private readonly TimelineController _timeline;

    // Snapshot of loaded text-box values — used to detect what the user actually changed.
    // Range sliders don't need snapshots; their current position is always applied directly.
    private string _loadedIntensity;
    private string _loadedSingleInput1;
    private string _loadedSingleInput2;

    public BlockEditorPanel(LumikitWindow window, TimelineController timeline)
    {
        _window = window;
        _timeline = timeline;

        // Allow indeterminate state on all effect checkboxes (used for when multiple blocks selected)
        _window.Effect_FadeIn.IsThreeState      = true;
        _window.Effect_FadeOut.IsThreeState     = true;
        _window.Effect_FadeStrobe.IsThreeState  = true;
        _window.Effect_Travel.IsThreeState      = true;
        _window.Effect_Combine.IsThreeState     = true;
        _window.Effect_Seperate.IsThreeState    = true;
        _window.Effect_Repeat.IsThreeState      = true;
        _window.Effect_ChangeColor.IsThreeState = true;
        _window.Effect_Twinkle.IsThreeState     = true;
    }

    /// <summary>
    /// Loads sidebar editor with pre-existing lightblock values.
    /// For multi-select, effects that are not consistent across all blocks
    /// are shown as indeterminate — applying changes will not touch them.
    /// </summary>
    public void LoadBlockIntoEditor(List<LightBlock> selectedBlocks)
    {
        if (selectedBlocks == null || selectedBlocks.Count == 0) return;
        _window.BlockEditorScrollViewer.IsVisible = true;
        _window.MainTabControl.SelectedIndex = 1; // Light Preview tab
        UpdateSelectedColorsBackground();

        // Use the last block for numeric values
        var block = selectedBlocks.Last();

        // Primary light range — shared between both slider panels so either shows correct values
        _window.LightRangeSlider.LowerSelectedValue   = block.StartLight;
        _window.LightRangeSlider.UpperSelectedValue   = block.EndLight;
        _window.LightRange2Slider1.LowerSelectedValue = block.StartLight;
        _window.LightRange2Slider1.UpperSelectedValue = block.EndLight;

        // Secondary light range — only used in dual-range mode (Travel / Combine / Separate)
        _window.LightRange2Slider2.LowerSelectedValue = block.SecondaryStartLight;
        _window.LightRange2Slider2.UpperSelectedValue = block.SecondaryEndLight;

        // Text box inputs (still change-detected)
        _window.IntensityInput.Text                = block.Intensity.ToString();
        _window.AdditionalSingleInput1TextBox.Text = block.AdditionalIndividualInput1.ToString();
        _window.AdditionalSingleInput2TextBox.Text = block.AdditionalIndividualInput2.ToString();

        _window.ColorDropBox.Background       = new SolidColorBrush(block.BlockColor);
        _window.SecondColorDropBox.Background = new SolidColorBrush(block.SecondBlockColor);

        // Snapshot text-box values for change detection
        _loadedIntensity    = _window.IntensityInput.Text;
        _loadedSingleInput1 = _window.AdditionalSingleInput1TextBox.Text;
        _loadedSingleInput2 = _window.AdditionalSingleInput2TextBox.Text;

        // For effects: checked = all blocks have it, unchecked = none, indeterminate = mixed
        SetEffectCheckbox(_window.Effect_FadeIn,      selectedBlocks, LightBlock.Effect.FadeIn);
        SetEffectCheckbox(_window.Effect_FadeOut,     selectedBlocks, LightBlock.Effect.FadeOut);
        SetEffectCheckbox(_window.Effect_FadeStrobe,  selectedBlocks, LightBlock.Effect.Strobe);
        SetEffectCheckbox(_window.Effect_Travel,      selectedBlocks, LightBlock.Effect.Travel);
        SetEffectCheckbox(_window.Effect_Combine,     selectedBlocks, LightBlock.Effect.Combine);
        SetEffectCheckbox(_window.Effect_Seperate,    selectedBlocks, LightBlock.Effect.Seperate);
        SetEffectCheckbox(_window.Effect_Repeat,      selectedBlocks, LightBlock.Effect.Repeat);
        SetEffectCheckbox(_window.Effect_ChangeColor, selectedBlocks, LightBlock.Effect.ChangeColor);
        SetEffectCheckbox(_window.Effect_Twinkle,     selectedBlocks, LightBlock.Effect.Twinkle);

        UpdateEffectSettingVisibility();
    }

    /// <summary>
    /// Sets a checkbox to checked, unchecked, or indeterminate based on
    /// whether the effect is present in all, none, or some of the selected blocks.
    /// </summary>
    private void SetEffectCheckbox(CheckBox checkbox, List<LightBlock> blocks, LightBlock.Effect effect)
    {
        bool allHave  = blocks.All(b => b.BlockEffects.Contains(effect));
        bool noneHave = blocks.All(b => !b.BlockEffects.Contains(effect));
        checkbox.IsChecked = allHave ? true : noneHave ? false : null;
    }

    /// <summary>
    /// Applies block values back from the editor controls.
    /// Slider values are always applied; text-box values only when changed from load snapshot.
    /// Effects: null (indeterminate) = untouched, true = add, false = remove.
    /// </summary>
    public void ApplyBlockChanges()
    {
        if (_timeline._selectedBlocks == null) return;

        bool dualRange = _window.Effect_Travel?.IsChecked  == true
                      || _window.Effect_Combine?.IsChecked == true
                      || _window.Effect_Seperate?.IsChecked == true;

        foreach (var selectedBlock in _timeline._selectedBlocks)
        {
            // Light range — read from whichever slider panel is active
            if (dualRange)
            {
                selectedBlock.StartLight          = (int)_window.LightRange2Slider1.LowerSelectedValue;
                selectedBlock.EndLight            = (int)_window.LightRange2Slider1.UpperSelectedValue;
                selectedBlock.SecondaryStartLight = (int)_window.LightRange2Slider2.LowerSelectedValue;
                selectedBlock.SecondaryEndLight   = (int)_window.LightRange2Slider2.UpperSelectedValue;
            }
            else
            {
                selectedBlock.StartLight = (int)_window.LightRangeSlider.LowerSelectedValue;
                selectedBlock.EndLight   = (int)_window.LightRangeSlider.UpperSelectedValue;
            }

            // Text box inputs — only apply if the user changed them since load
            if (_window.IntensityInput.Text != _loadedIntensity && int.TryParse(_window.IntensityInput.Text, out int intensity))
                selectedBlock.Intensity = Math.Clamp(intensity, 0, 255);
            if (_window.AdditionalSingleInput1TextBox.Text != _loadedSingleInput1 && int.TryParse(_window.AdditionalSingleInput1TextBox.Text, out int single1))
                selectedBlock.AdditionalIndividualInput1 = single1;
            if (_window.AdditionalSingleInput2TextBox.Text != _loadedSingleInput2 && int.TryParse(_window.AdditionalSingleInput2TextBox.Text, out int single2))
                selectedBlock.AdditionalIndividualInput2 = single2;

            // null = indeterminate = user didn't touch it = don't change
            ApplyEffect(selectedBlock, LightBlock.Effect.FadeIn,      _window.Effect_FadeIn.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.FadeOut,     _window.Effect_FadeOut.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.Strobe,      _window.Effect_FadeStrobe.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.Travel,      _window.Effect_Travel.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.Combine,     _window.Effect_Combine.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.Seperate,    _window.Effect_Seperate.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.Repeat,      _window.Effect_Repeat.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.ChangeColor, _window.Effect_ChangeColor.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.Twinkle,     _window.Effect_Twinkle.IsChecked);
        }
    }

    /// <summary>
    /// Adds or removes an effect based on checkbox state.
    /// Null (indeterminate) means the user didn't change it — leave it alone.
    /// </summary>
    private void ApplyEffect(LightBlock block, LightBlock.Effect effect, bool? isChecked)
    {
        if (isChecked == true && !block.BlockEffects.Contains(effect))
            block.BlockEffects.Add(effect);
        else if (isChecked == false)
            block.BlockEffects.Remove(effect);
        // null = don't touch
    }

    /// <summary>
    /// Visual indicator for selected lightblock
    /// </summary>
    public void UpdateSelectedColorsBackground()
    {
        if (_timeline._selectedBlocks == null) return;
        foreach (var selectedBlock in _timeline._selectedBlocks)
        {
            var color = selectedBlock.BlockColor;
            var newcolor = new Color((byte)(color.A * 0.5), color.R, color.G, color.B);
            selectedBlock.UpdateBackground(newcolor);
        }
    }

    /// <summary>
    /// Shows the correct range slider panel and optional extra inputs based on active effects.
    ///   No dual-range effects → LightRange (single slider)
    ///   Travel / Combine / Separate → LightRange2 (two sliders)
    ///   Combine / Separate also show the combined-width input
    ///   Repeat shows the repeat-count input
    /// </summary>
    public void UpdateEffectSettingVisibility()
    {
        var travelEffectActive   = _window.Effect_Travel?.IsChecked   == true;
        var combineEffectActive  = _window.Effect_Combine?.IsChecked  == true;
        var seperateEffectActive = _window.Effect_Seperate?.IsChecked == true;
        var repeatEffectActive   = _window.Effect_Repeat?.IsChecked   == true;

        bool needsDualRange = travelEffectActive || combineEffectActive || seperateEffectActive;

        // Show the appropriate light range slider panel
        _window.LightRange.IsVisible  = !needsDualRange;
        _window.LightRange2.IsVisible = needsDualRange;

        // Combined-width input — only for Combine / Separate
        if (combineEffectActive || seperateEffectActive)
        {
            _window.AdditionalSingleInputPanel1.IsVisible = true;
            _window.AdditionalSingleInputLabel1.Text      = "Combined Width (0-1000)";
        }
        else
        {
            _window.AdditionalSingleInputLabel1.Text      = "";
            _window.AdditionalSingleInput1TextBox.Text    = "";
            _window.AdditionalSingleInputPanel1.IsVisible = false;
        }

        // Repeat-count input
        if (repeatEffectActive)
        {
            _window.AdditionalSingleInputLabel2.Text      = "Repeat Number";
            _window.AdditionalSingleInputPanel2.IsVisible = true;
        }
        else
        {
            _window.AdditionalSingleInputLabel2.Text      = "";
            _window.AdditionalSingleInput2TextBox.Text    = "";
            _window.AdditionalSingleInputPanel2.IsVisible = false;
        }
    }

    /// <summary>
    /// Hide the block editor sidebar
    /// </summary>
    public void Hide() => _window.BlockEditorScrollViewer.IsVisible = false;
}