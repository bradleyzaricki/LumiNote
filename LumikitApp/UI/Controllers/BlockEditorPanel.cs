namespace LumikitApp;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using LumikitApp.ViewModels;

/// <summary>
/// Owns all block editor sidebar logic: loading block values into the editor
/// and applying selected changes back onto light blocks.
/// All UI state is written through BlockEditorViewModel; bindings keep the view in sync.
/// </summary>
public class BlockEditorPanel
{
    private readonly BlockEditorViewModel _viewModel;
    private readonly TimelineController _timeline;

    // Snapshots of text-box values at load time — used to detect what the user changed.
    private string _loadedIntensity    = "";
    private string _loadedSingleInput1 = "";
    private string _loadedSingleInput2 = "";

    public BlockEditorPanel(BlockEditorViewModel viewModel, TimelineController timeline)
    {
        _viewModel = viewModel;
        _timeline  = timeline;
    }

    /// <summary>
    /// Loads the sidebar editor with the values of the selected light blocks.
    /// For multi-select, effects not consistent across all blocks are shown as indeterminate.
    /// </summary>
    public void LoadBlockIntoEditor(List<LightBlock> selectedBlocks)
    {
        if (selectedBlocks == null || selectedBlocks.Count == 0) return;

        _viewModel.EditorVisible    = true;
        _viewModel.SelectedTabIndex = 1;
        UpdateSelectedColorsBackground();

        var block = selectedBlocks.Last();

        _viewModel.LightRangeLower    = block.StartLight;
        _viewModel.LightRangeUpper    = block.EndLight;
        _viewModel.Range2Slider1Lower = block.StartLight;
        _viewModel.Range2Slider1Upper = block.EndLight;
        _viewModel.Range2Slider2Lower = block.SecondaryStartLight;
        _viewModel.Range2Slider2Upper = block.SecondaryEndLight;

        _viewModel.BlockColorBrush       = new SolidColorBrush(block.BlockColor);
        _viewModel.SecondBlockColorBrush = new SolidColorBrush(block.SecondBlockColor);

        // Load all effect states at once — bypasses mutual-exclusion so saved data is
        // restored exactly as stored rather than triggering side-effect clears.
        _viewModel.LoadEffects(
            fadeIn:      EffectState(selectedBlocks, LightBlock.Effect.FadeIn),
            fadeOut:     EffectState(selectedBlocks, LightBlock.Effect.FadeOut),
            strobe:      EffectState(selectedBlocks, LightBlock.Effect.Strobe),
            travel:      EffectState(selectedBlocks, LightBlock.Effect.Travel),
            combine:     EffectState(selectedBlocks, LightBlock.Effect.Combine),
            separate:    EffectState(selectedBlocks, LightBlock.Effect.Seperate),
            repeat:      EffectState(selectedBlocks, LightBlock.Effect.Repeat),
            changeColor: EffectState(selectedBlocks, LightBlock.Effect.ChangeColor),
            twinkle:     EffectState(selectedBlocks, LightBlock.Effect.Twinkle)
        );

        _viewModel.IntensityText        = block.Intensity.ToString();
        _viewModel.AdditionalInput1Text = block.AdditionalIndividualInput1.ToString();
        _viewModel.AdditionalInput2Text = block.AdditionalIndividualInput2.ToString();

        _loadedIntensity    = _viewModel.IntensityText;
        _loadedSingleInput1 = _viewModel.AdditionalInput1Text;
        _loadedSingleInput2 = _viewModel.AdditionalInput2Text;

        _viewModel.RequestPreview();
    }

    /// <summary>
    /// Returns checked, unchecked, or null (indeterminate) based on whether the effect
    /// is present in all, none, or some of the selected blocks.
    /// </summary>
    private static bool? EffectState(List<LightBlock> blocks, LightBlock.Effect effect)
    {
        bool allHave  = blocks.All(b => b.BlockEffects.Contains(effect));
        bool noneHave = blocks.All(b => !b.BlockEffects.Contains(effect));
        return allHave ? true : noneHave ? false : null;
    }

    /// <summary>
    /// Applies editor values back onto all selected blocks.
    /// Slider values are always applied; text inputs only when changed from the load snapshot.
    /// Indeterminate effects (null) are left untouched.
    /// </summary>
    public void ApplyBlockChanges()
    {
        if (_timeline._selectedBlocks == null) return;

        bool dualRange = _viewModel.Travel  == true
                      || _viewModel.Combine == true
                      || _viewModel.Separate == true;

        foreach (var block in _timeline._selectedBlocks)
        {
            if (dualRange)
            {
                block.StartLight          = (int)_viewModel.Range2Slider1Lower;
                block.EndLight            = (int)_viewModel.Range2Slider1Upper;
                block.SecondaryStartLight = (int)_viewModel.Range2Slider2Lower;
                block.SecondaryEndLight   = (int)_viewModel.Range2Slider2Upper;
            }
            else
            {
                block.StartLight = (int)_viewModel.LightRangeLower;
                block.EndLight   = (int)_viewModel.LightRangeUpper;
            }

            if (_viewModel.IntensityText != _loadedIntensity
                    && int.TryParse(_viewModel.IntensityText, out int intensity))
                block.Intensity = Math.Clamp(intensity, 0, 255);

            if (_viewModel.AdditionalInput1Text != _loadedSingleInput1
                    && int.TryParse(_viewModel.AdditionalInput1Text, out int single1))
                block.AdditionalIndividualInput1 = single1;

            if (_viewModel.AdditionalInput2Text != _loadedSingleInput2
                    && int.TryParse(_viewModel.AdditionalInput2Text, out int single2))
                block.AdditionalIndividualInput2 = single2;

            ApplyEffect(block, LightBlock.Effect.FadeIn,      _viewModel.FadeIn);
            ApplyEffect(block, LightBlock.Effect.FadeOut,     _viewModel.FadeOut);
            ApplyEffect(block, LightBlock.Effect.Strobe,      _viewModel.Strobe);
            ApplyEffect(block, LightBlock.Effect.Travel,      _viewModel.Travel);
            ApplyEffect(block, LightBlock.Effect.Combine,     _viewModel.Combine);
            ApplyEffect(block, LightBlock.Effect.Seperate,    _viewModel.Separate);
            ApplyEffect(block, LightBlock.Effect.Repeat,      _viewModel.Repeat);
            ApplyEffect(block, LightBlock.Effect.ChangeColor, _viewModel.ChangeColor);
            ApplyEffect(block, LightBlock.Effect.Twinkle,     _viewModel.Twinkle);
        }
    }

    private static void ApplyEffect(LightBlock block, LightBlock.Effect effect, bool? isChecked)
    {
        if (isChecked == true && !block.BlockEffects.Contains(effect))
            block.BlockEffects.Add(effect);
        else if (isChecked == false)
            block.BlockEffects.Remove(effect);
        // null = indeterminate = leave untouched
    }

    /// <summary>
    /// Dims selected blocks visually to indicate selection.
    /// </summary>
    public void UpdateSelectedColorsBackground()
    {
        if (_timeline._selectedBlocks == null) return;
        foreach (var block in _timeline._selectedBlocks)
        {
            var color  = block.BlockColor;
            var dimmed = new Color((byte)(color.A * 0.5), color.R, color.G, color.B);
            block.UpdateBackground(dimmed);
        }
    }

    public void Hide() => _viewModel.EditorVisible = false;
}