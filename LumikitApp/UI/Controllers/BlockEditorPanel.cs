namespace LumikitApp;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using LumikitApp.Controls;
using LumikitApp.ViewModels;

/// <summary>
/// Owns all block editor sidebar logic: loading block values into the editor
/// and applying selected changes back onto light blocks.
/// All UI state is written through BlockEditorViewModel; bindings keep the view in sync.
/// </summary>
public class BlockEditorPanel
{
    private readonly BlockEditorViewModel _viewModel;
    private readonly TimelineView _timeline;

    private string _loadedIntensity    = "";
    private string _loadedSingleInput1 = "";
    private string _loadedSingleInput2 = "";

    private double _loadedLightRangeLower, _loadedLightRangeUpper;
    private double _loadedRange2Slider1Lower, _loadedRange2Slider1Upper;
    private double _loadedRange2Slider2Lower, _loadedRange2Slider2Upper;

    // True while LoadBlockIntoEditor is populating the view model, so the resulting
    // PropertyChanged storm doesn't auto-apply back onto the blocks.
    private bool _isLoading;

    // Snapshot of the timeline taken when a block is loaded for editing, plus a flag
    // that pushes it to the undo stack on the first real edit of this session.
    private List<LightBlockData>? _preEditSnapshot;
    private bool _pendingEditPush;

    // View model properties that represent actual block data; changes to these auto-apply.
    private static readonly HashSet<string> EditableProps = new()
    {
        nameof(BlockEditorViewModel.FadeIn),    nameof(BlockEditorViewModel.FadeOut),
        nameof(BlockEditorViewModel.Strobe),    nameof(BlockEditorViewModel.Travel),
        nameof(BlockEditorViewModel.Combine),   nameof(BlockEditorViewModel.Separate),
        nameof(BlockEditorViewModel.Repeat),    nameof(BlockEditorViewModel.ChangeColor),
        nameof(BlockEditorViewModel.Twinkle),
        nameof(BlockEditorViewModel.LightRangeLower),    nameof(BlockEditorViewModel.LightRangeUpper),
        nameof(BlockEditorViewModel.Range2Slider1Lower), nameof(BlockEditorViewModel.Range2Slider1Upper),
        nameof(BlockEditorViewModel.Range2Slider2Lower), nameof(BlockEditorViewModel.Range2Slider2Upper),
        nameof(BlockEditorViewModel.IntensityText),
        nameof(BlockEditorViewModel.AdditionalInput1Text),
        nameof(BlockEditorViewModel.AdditionalInput2Text),
    };

    public BlockEditorPanel(BlockEditorViewModel viewModel, TimelineView timeline)
    {
        _viewModel = viewModel;
        _timeline  = timeline;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Auto-applies editor changes onto the selected blocks the moment any editable
    /// value changes — no explicit "Apply" step needed.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isLoading) return;
        if (e.PropertyName == null || !EditableProps.Contains(e.PropertyName)) return;

        ApplyBlockChanges();
        _viewModel.RequestPreview();
    }

    /// <summary>
    /// Loads the sidebar editor with the values of the selected light blocks.
    /// For multi-select, effects not consistent across all blocks are shown as indeterminate.
    /// </summary>
    public void LoadBlockIntoEditor(List<LightBlock> selectedBlocks)
    {
        if (selectedBlocks == null || selectedBlocks.Count == 0) return;

        _isLoading = true;

        // Snapshot the timeline so the first edit of this session can be undone as one step.
        _preEditSnapshot = _timeline.CaptureState();
        _pendingEditPush = true;

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

        _loadedLightRangeLower    = _viewModel.LightRangeLower;
        _loadedLightRangeUpper    = _viewModel.LightRangeUpper;
        _loadedRange2Slider1Lower = _viewModel.Range2Slider1Lower;
        _loadedRange2Slider1Upper = _viewModel.Range2Slider1Upper;
        _loadedRange2Slider2Lower = _viewModel.Range2Slider2Lower;
        _loadedRange2Slider2Upper = _viewModel.Range2Slider2Upper;

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

        var combineEffect = block.BlockEffects.FirstOrDefault(e => e.Type == LightBlock.Effect.Combine)
                         ?? block.BlockEffects.FirstOrDefault(e => e.Type == LightBlock.Effect.Seperate);
        var repeatEffect  = block.BlockEffects.FirstOrDefault(e => e.Type == LightBlock.Effect.Repeat);

        _viewModel.IntensityText        = block.Intensity.ToString();
        _viewModel.AdditionalInput1Text = ((int)(combineEffect?.Params.GetValueOrDefault("TargetWidth", 0) ?? 0)).ToString();
        _viewModel.AdditionalInput2Text = ((int)(repeatEffect?.Params.GetValueOrDefault("Count", 1) ?? 1)).ToString();

        _loadedIntensity    = _viewModel.IntensityText;
        _loadedSingleInput1 = _viewModel.AdditionalInput1Text;
        _loadedSingleInput2 = _viewModel.AdditionalInput2Text;

        _isLoading = false;
        _viewModel.RequestPreview();
    }

    /// <summary>
    /// Returns checked, unchecked, or null (indeterminate) based on whether the effect
    /// is present in all, none, or some of the selected blocks.
    /// </summary>
    private static bool? EffectState(List<LightBlock> blocks, LightBlock.Effect effect)
    {
        bool allHave  = blocks.All(b => b.BlockEffects.Any(e => e.Type == effect));
        bool noneHave = blocks.All(b => b.BlockEffects.All(e => e.Type != effect));
        return allHave ? true : noneHave ? false : null;
    }

    /// <summary>
    /// Applies editor values back onto all selected blocks.
    /// Slider values are always applied; text inputs only when changed from the load snapshot.
    /// Indeterminate effects (null) are left untouched.
    /// </summary>
    public void ApplyBlockChanges()
    {
        if (_timeline.SelectedBlocks == null) return;

        // Record one undo entry for the whole editing session, on the first edit.
        if (_pendingEditPush && _preEditSnapshot != null)
        {
            _timeline.PushUndo(_preEditSnapshot);
            _pendingEditPush = false;
        }

        bool dualRange = _viewModel.Travel  == true
                      || _viewModel.Combine == true
                      || _viewModel.Separate == true;

        foreach (var block in _timeline.SelectedBlocks)
        {
            if (dualRange)
            {
                if (_viewModel.Range2Slider1Lower != _loadedRange2Slider1Lower)
                    block.StartLight = (int)_viewModel.Range2Slider1Lower;
                if (_viewModel.Range2Slider1Upper != _loadedRange2Slider1Upper)
                    block.EndLight = (int)_viewModel.Range2Slider1Upper;
                if (_viewModel.Range2Slider2Lower != _loadedRange2Slider2Lower)
                    block.SecondaryStartLight = (int)_viewModel.Range2Slider2Lower;
                if (_viewModel.Range2Slider2Upper != _loadedRange2Slider2Upper)
                    block.SecondaryEndLight = (int)_viewModel.Range2Slider2Upper;
            }
            else
            {
                if (_viewModel.LightRangeLower != _loadedLightRangeLower)
                    block.StartLight = (int)_viewModel.LightRangeLower;
                if (_viewModel.LightRangeUpper != _loadedLightRangeUpper)
                    block.EndLight = (int)_viewModel.LightRangeUpper;
            }

            if (_viewModel.IntensityText != _loadedIntensity
                    && int.TryParse(_viewModel.IntensityText, out int intensity))
                block.Intensity = Math.Clamp(intensity, 0, 255);

            ApplyEffect(block, LightBlock.Effect.FadeIn,      _viewModel.FadeIn);
            ApplyEffect(block, LightBlock.Effect.FadeOut,     _viewModel.FadeOut);
            ApplyEffect(block, LightBlock.Effect.Strobe,      _viewModel.Strobe);
            ApplyEffect(block, LightBlock.Effect.Travel,      _viewModel.Travel);
            ApplyEffect(block, LightBlock.Effect.Combine,     _viewModel.Combine);
            ApplyEffect(block, LightBlock.Effect.Seperate,    _viewModel.Separate);
            ApplyEffect(block, LightBlock.Effect.Repeat,      _viewModel.Repeat);
            ApplyEffect(block, LightBlock.Effect.ChangeColor, _viewModel.ChangeColor);
            ApplyEffect(block, LightBlock.Effect.Twinkle,     _viewModel.Twinkle);

            // Write params into effect entries after presence is applied
            if (_viewModel.AdditionalInput1Text != _loadedSingleInput1
                    && int.TryParse(_viewModel.AdditionalInput1Text, out int combineWidth))
            {
                foreach (var e in block.BlockEffects.Where(e =>
                    e.Type == LightBlock.Effect.Combine || e.Type == LightBlock.Effect.Seperate))
                    e.Params["TargetWidth"] = combineWidth;
            }

            if (_viewModel.AdditionalInput2Text != _loadedSingleInput2
                    && int.TryParse(_viewModel.AdditionalInput2Text, out int repeatCount))
            {
                foreach (var e in block.BlockEffects.Where(e => e.Type == LightBlock.Effect.Repeat))
                    e.Params["Count"] = repeatCount;
            }
        }
    }

    private static void ApplyEffect(LightBlock block, LightBlock.Effect effect, bool? isChecked)
    {
        bool has = block.BlockEffects.Any(e => e.Type == effect);
        if (isChecked == true && !has)
            block.BlockEffects.Add(new EffectData { Type = effect });
        else if (isChecked == false)
            block.BlockEffects.RemoveAll(e => e.Type == effect);
        // null = indeterminate = leave untouched
    }

    /// <summary>
    /// Dims selected blocks visually to indicate selection.
    /// </summary>
    public void UpdateSelectedColorsBackground()
    {
        if (_timeline.SelectedBlocks == null) return;
        foreach (var block in _timeline.SelectedBlocks)
        {
            var color  = block.BlockColor;
            var dimmed = new Color((byte)(color.A * 0.5), color.R, color.G, color.B);
            block.UpdateBackground(dimmed);
        }
    }

    public void Hide() => _viewModel.EditorVisible = false;
}