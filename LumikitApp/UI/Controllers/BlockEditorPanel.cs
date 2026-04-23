namespace LumikitApp;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System;
using System.Linq;
/// <summary>
/// Owns all block editor sidebar logic such as loading block values into the editor
/// and applying selected changes onto lightblocks
/// LumikitWindow holds references to the UI controls and passes them in via a (long) constructor.
/// </summary>
public class BlockEditorPanel
{
    //Timeline Reference
    private readonly TimelineController _timeline;

    //Sidebar Visibillity
    private readonly Border _blockEditorBorder;

    //Main and secondary color drop boxes
    private readonly Canvas _blockColorDropBox;
    private readonly Canvas _secondColorDropBox;

    //Light Settings Inputs
    private readonly TextBox _startLightInput;
    private readonly TextBox _endLightInput;
    private readonly TextBox _intensityInput;
    private readonly TextBox _additionalDualInput1;
    private readonly TextBox _additionalDualInput2;
    private readonly TextBox _additionalSingleInput1;
    private readonly TextBox _additionalSingleInput2;

    //Effect Checkboxes
    private readonly CheckBox _effectFadeIn;
    private readonly CheckBox _effectFadeOut;
    private readonly CheckBox _effectStrobe;
    private readonly CheckBox _effectTravel;
    private readonly CheckBox _effectCombine;
    private readonly CheckBox _effectSeperate;
    private readonly CheckBox _effectRepeat;
    private readonly CheckBox _effectChangeColor;
    private readonly CheckBox _effectTwinkle;

    //Effect Labels (For Visibility Purposes)
    private readonly StackPanel _additionalDualInputsPanel;
    private readonly TextBlock _additionalDualInput1Label;
    private readonly TextBlock _additionalDualInput2Label;
    private readonly TextBlock _additionalSingleInputLabel1;
    private readonly TextBlock _additionalSingleInputLabel2;
    private readonly StackPanel _additionalSingleInputPanel1;
    private readonly StackPanel _additionalSingleInputPanel2;
   // Stores the text values at load time so we can detect user changes
    private string _loadedStartLight;
    private string _loadedEndLight;
    private string _loadedIntensity;
    private string _loadedDualInput1;
    private string _loadedDualInput2;
    private string _loadedSingleInput1;
    private string _loadedSingleInput2;

    public BlockEditorPanel(
        TimelineController timeline,
        Border blockEditorBorder,
        Canvas blockColorDropBox,
        Canvas secondColorDropBox,
        TextBox startLightInput,
        TextBox endLightInput,
        TextBox intensityInput,
        TextBox additionalDualInput1,
        TextBox additionalDualInput2,
        TextBox additionalSingleInput1,
        TextBox additionalSingleInput2,
        CheckBox effectFadeIn,
        CheckBox effectFadeOut,
        CheckBox effectStrobe,
        CheckBox effectTravel,
        CheckBox effectCombine,
        CheckBox effectSeperate,
        CheckBox effectRepeat,
        CheckBox effectChangeColor,
        CheckBox effectTwinkle,
        StackPanel additionalDualInputsPanel,
        TextBlock additionalDualInput1Label,
        TextBlock additionalDualInput2Label,
        TextBlock additionalSingleInputLabel1,
        TextBlock additionalSingleInputLabel2,
        StackPanel additionalSingleInputPanel1,
        StackPanel additionalSingleInputPanel2
    )
    {
        _timeline = timeline;
        _blockEditorBorder = blockEditorBorder;
        _blockColorDropBox = blockColorDropBox;
        _secondColorDropBox = secondColorDropBox;
        _startLightInput = startLightInput;
        _endLightInput = endLightInput;
        _intensityInput = intensityInput;
        _additionalDualInput1 = additionalDualInput1;
        _additionalDualInput2 = additionalDualInput2;
        _additionalSingleInput1 = additionalSingleInput1;
        _additionalSingleInput2 = additionalSingleInput2;
        _effectFadeIn = effectFadeIn;
        _effectFadeOut = effectFadeOut;
        _effectStrobe = effectStrobe;
        _effectTravel = effectTravel;
        _effectCombine = effectCombine;
        _effectSeperate = effectSeperate;
        _effectRepeat = effectRepeat;
        _effectChangeColor = effectChangeColor;
        _effectTwinkle = effectTwinkle;
        _additionalDualInputsPanel = additionalDualInputsPanel;
        _additionalDualInput1Label = additionalDualInput1Label;
        _additionalDualInput2Label = additionalDualInput2Label;
        _additionalSingleInputLabel1 = additionalSingleInputLabel1;
        _additionalSingleInputLabel2 = additionalSingleInputLabel2;
        _additionalSingleInputPanel1 = additionalSingleInputPanel1;
        _additionalSingleInputPanel2 = additionalSingleInputPanel2;

        // Allow indeterminate state on all effect checkboxes
        _effectFadeIn.IsThreeState = true;
        _effectFadeOut.IsThreeState = true;
        _effectStrobe.IsThreeState = true;
        _effectTravel.IsThreeState = true;
        _effectCombine.IsThreeState = true;
        _effectSeperate.IsThreeState = true;
        _effectRepeat.IsThreeState = true;
        _effectChangeColor.IsThreeState = true;
        _effectTwinkle.IsThreeState = true;
    }

    /// <summary>
    /// Loads sidebar editor with pre-existing lightblock values.
    /// For multi-select, effects that are not consistent across all blocks
    /// are shown as indeterminate — applying changes will not touch them.
    /// </summary>
    public void LoadBlockIntoEditor(List<LightBlock> selectedBlocks)
    {
        if (selectedBlocks == null || selectedBlocks.Count == 0) return;
        _blockEditorBorder.IsVisible = true;
        UpdateSelectedColorsBackground();

        // Use the last block for numeric values (same as before)
        var block = selectedBlocks.Last();
        _startLightInput.Text = block.StartLight.ToString();
        _endLightInput.Text = block.EndLight.ToString();
        _intensityInput.Text = block.Intensity.ToString();
        _additionalDualInput1.Text = block.SecondaryStartLight.ToString();
        _additionalDualInput2.Text = block.SecondaryEndLight.ToString();
        _additionalSingleInput1.Text = block.AdditionalIndividualInput1.ToString();
        _additionalSingleInput2.Text = block.AdditionalIndividualInput2.ToString();
        _blockColorDropBox.Background = new SolidColorBrush(block.BlockColor);
        _secondColorDropBox.Background = new SolidColorBrush(block.SecondBlockColor);

        // Store loaded values so ApplyBlockChanges can detect what changed
        _loadedStartLight = _startLightInput.Text;
        _loadedEndLight = _endLightInput.Text;
        _loadedIntensity = _intensityInput.Text;
        _loadedDualInput1 = _additionalDualInput1.Text;
        _loadedDualInput2 = _additionalDualInput2.Text;
        _loadedSingleInput1 = _additionalSingleInput1.Text;
        _loadedSingleInput2 = _additionalSingleInput2.Text;

        // For effects: checked = all blocks have it, unchecked = no blocks have it, dot = mixed
        SetEffectCheckbox(_effectFadeIn,    selectedBlocks, LightBlock.Effect.FadeIn);
        SetEffectCheckbox(_effectFadeOut,   selectedBlocks, LightBlock.Effect.FadeOut);
        SetEffectCheckbox(_effectStrobe,    selectedBlocks, LightBlock.Effect.Strobe);
        SetEffectCheckbox(_effectTravel,    selectedBlocks, LightBlock.Effect.Travel);
        SetEffectCheckbox(_effectCombine,   selectedBlocks, LightBlock.Effect.Combine);
        SetEffectCheckbox(_effectSeperate,  selectedBlocks, LightBlock.Effect.Seperate);
        SetEffectCheckbox(_effectRepeat,    selectedBlocks, LightBlock.Effect.Repeat);
        SetEffectCheckbox(_effectChangeColor, selectedBlocks, LightBlock.Effect.ChangeColor);
        SetEffectCheckbox(_effectTwinkle,   selectedBlocks, LightBlock.Effect.Twinkle);

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
    /// Applies only the values the user actually changed since loading.
    /// Numeric fields: only applied if text differs from loaded value.
    /// Effects: null (indeterminate) = untouched, true = add, false = remove.
    /// </summary>
    public void ApplyBlockChanges()
    {
        if (_timeline._selectedBlocks == null) return;
        foreach (var selectedBlock in _timeline._selectedBlocks)
        {
            if (_startLightInput.Text != _loadedStartLight && int.TryParse(_startLightInput.Text, out int start))
                selectedBlock.StartLight = start;
            if (_endLightInput.Text != _loadedEndLight && int.TryParse(_endLightInput.Text, out int end))
                selectedBlock.EndLight = end;
            if (_intensityInput.Text != _loadedIntensity && int.TryParse(_intensityInput.Text, out int intensity))
                selectedBlock.Intensity = Math.Clamp(intensity, 0, 255);
            if (_additionalDualInput1.Text != _loadedDualInput1 && int.TryParse(_additionalDualInput1.Text, out int travelStart))
                selectedBlock.SecondaryStartLight = travelStart;
            if (_additionalDualInput2.Text != _loadedDualInput2 && int.TryParse(_additionalDualInput2.Text, out int travelEnd))
                selectedBlock.SecondaryEndLight = travelEnd;
            if (_additionalSingleInput1.Text != _loadedSingleInput1 && int.TryParse(_additionalSingleInput1.Text, out int single1))
                selectedBlock.AdditionalIndividualInput1 = single1;
            if (_additionalSingleInput2.Text != _loadedSingleInput2 && int.TryParse(_additionalSingleInput2.Text, out int single2))
                selectedBlock.AdditionalIndividualInput2 = single2;

            // null = indeterminate = user didn't touch it = don't change
            ApplyEffect(selectedBlock, LightBlock.Effect.FadeIn,      _effectFadeIn.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.FadeOut,     _effectFadeOut.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.Strobe,      _effectStrobe.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.Travel,      _effectTravel.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.Combine,     _effectCombine.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.Seperate,    _effectSeperate.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.Repeat,      _effectRepeat.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.ChangeColor, _effectChangeColor.IsChecked);
            ApplyEffect(selectedBlock, LightBlock.Effect.Twinkle,     _effectTwinkle.IsChecked);
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
    /// Updates the visibility of the lightblock effect variables to ensure proper light effect combinations
    /// </summary>
    public void UpdateEffectSettingVisibility()
    {
        var travelEffectActive = _effectTravel?.IsChecked == true;
        var combineEffectActive = _effectCombine?.IsChecked == true;
        var seperateEffectActive = _effectSeperate?.IsChecked == true;
        var repeatEffectActive = _effectRepeat?.IsChecked == true;

        if (!(travelEffectActive || combineEffectActive || seperateEffectActive))
        {
            _additionalDualInputsPanel.IsVisible = false;
            _additionalDualInput1.Text = "";
            _additionalDualInput2.Text = "";
            _additionalSingleInputLabel1.Text = "";
            _additionalSingleInput1.Text = "";
            _additionalSingleInputPanel1.IsVisible = false;
        }

        if (travelEffectActive)
        {
            _additionalDualInputsPanel.IsVisible = true;
            _additionalDualInput1Label.Text = "Final Start Light (0-1000)";
            _additionalDualInput2Label.Text = "Final End Light (0-1000)";
            _additionalSingleInputLabel1.Text = "";
            _additionalSingleInput1.Text = "";
            _additionalSingleInputPanel1.IsVisible = false;
        }

        if (combineEffectActive || seperateEffectActive)
        {
            _additionalDualInputsPanel.IsVisible = true;
            _additionalDualInput1Label.Text = "Second Start Light (0-1000)";
            _additionalDualInput2Label.Text = "Second End Light (0-1000)";
            _additionalSingleInputPanel1.IsVisible = true;
            _additionalSingleInputLabel1.Text = "Combined Width (0-1000)";
        }

        if (repeatEffectActive)
        {
            _additionalSingleInputLabel2.Text = "Repeat Number";
            _additionalSingleInputPanel2.IsVisible = true;
        }
        else
        {
            _additionalSingleInputLabel2.Text = "";
            _additionalSingleInput2.Text = "";
            _additionalSingleInputPanel2.IsVisible = false;
        }
    }

    /// <summary>
    /// Hide the block editor sidebar
    /// </summary>
    public void Hide() => _blockEditorBorder.IsVisible = false;
}