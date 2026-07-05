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
/// Effect param rows are generated dynamically from the EffectCatalog schema.
/// </summary>
public class BlockEditorPanel
{
    private readonly BlockEditorViewModel _viewModel;
    private readonly TimelineView _timeline;

    private string _loadedIntensity = "";

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
        nameof(BlockEditorViewModel.SelectedShape),
        nameof(BlockEditorViewModel.SelectedTexture),
        nameof(BlockEditorViewModel.FadeIn),    nameof(BlockEditorViewModel.FadeOut),
        nameof(BlockEditorViewModel.Strobe),    nameof(BlockEditorViewModel.Repeat),
        nameof(BlockEditorViewModel.ChangeColor), nameof(BlockEditorViewModel.Comet),
        nameof(BlockEditorViewModel.FillColor),
        nameof(BlockEditorViewModel.LightRangeLower),    nameof(BlockEditorViewModel.LightRangeUpper),
        nameof(BlockEditorViewModel.Range2Slider1Lower), nameof(BlockEditorViewModel.Range2Slider1Upper),
        nameof(BlockEditorViewModel.Range2Slider2Lower), nameof(BlockEditorViewModel.Range2Slider2Upper),
        nameof(BlockEditorViewModel.IntensityText),
    };

    // Subset whose changes add/remove effects, so the dynamic param rows must be rebuilt.
    private static readonly HashSet<string> EffectPresenceProps = new()
    {
        nameof(BlockEditorViewModel.SelectedShape),
        nameof(BlockEditorViewModel.SelectedTexture),
        nameof(BlockEditorViewModel.FadeIn),    nameof(BlockEditorViewModel.FadeOut),
        nameof(BlockEditorViewModel.Strobe),    nameof(BlockEditorViewModel.Repeat),
        nameof(BlockEditorViewModel.ChangeColor), nameof(BlockEditorViewModel.Comet),
        nameof(BlockEditorViewModel.FillColor),
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
        if (EffectPresenceProps.Contains(e.PropertyName))
            RebuildEffectParams();
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
        _viewModel.FillColorBrush        = new SolidColorBrush(block.FillColor);
        _viewModel.StrobeColorBrush      = new SolidColorBrush(block.StrobeColor);

        // Load shape + texture + all modifier states at once — bypasses setter side effects
        // so saved data is restored exactly as stored.
        _viewModel.LoadEffects(
            shape:       ShapeState(selectedBlocks),
            texture:     TextureState(selectedBlocks),
            fadeIn:      EffectState(selectedBlocks, LightBlock.Effect.FadeIn),
            fadeOut:     EffectState(selectedBlocks, LightBlock.Effect.FadeOut),
            strobe:      EffectState(selectedBlocks, LightBlock.Effect.Strobe),
            repeat:      EffectState(selectedBlocks, LightBlock.Effect.Repeat),
            changeColor: EffectState(selectedBlocks, LightBlock.Effect.ChangeColor),
            fillColor:   EffectState(selectedBlocks, LightBlock.Effect.FillColor),
            comet:       EffectState(selectedBlocks, LightBlock.Effect.Comet)
        );

        _viewModel.IntensityText = block.Intensity.ToString();
        _loadedIntensity         = _viewModel.IntensityText;

        RebuildEffectParams();

        _isLoading = false;
        _viewModel.RequestPreview();
    }

    /// <summary>
    /// Returns the shape shared by all selected blocks (Effect.None = static span),
    /// or null when the selection mixes different shapes.
    /// </summary>
    private static LightBlock.Effect? ShapeState(List<LightBlock> blocks)
    {
        var shape = blocks[0].GetShape();
        return blocks.All(b => b.GetShape() == shape) ? shape : null;
    }

    /// <summary>
    /// Returns the texture shared by all selected blocks (Effect.None = no texture),
    /// or null when the selection mixes different textures.
    /// </summary>
    private static LightBlock.Effect? TextureState(List<LightBlock> blocks)
    {
        var texture = blocks[0].GetTexture();
        return blocks.All(b => b.GetTexture() == texture) ? texture : null;
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
    /// Regenerates the dynamic param rows from the reference block's active effects,
    /// pulling titles/control kinds from the EffectCatalog schema.
    /// </summary>
    private void RebuildEffectParams()
    {
        foreach (var old in _viewModel.EffectParams)
            old.ValueChanged -= OnEffectParamChanged;
        _viewModel.EffectParams.Clear();

        var blocks = _timeline.SelectedBlocks;
        if (blocks == null || blocks.Count == 0) return;
        var reference = blocks.Last();

        foreach (var data in ActiveEffectEntries(reference))
        {
            var def = EffectCatalog.Get(data.Type);
            if (def == null) continue;
            foreach (var p in def.Parameters)
            {
                var row = new EffectParamViewModel(data.Type, p,
                    data.Params.GetValueOrDefault(p.Key, p.Default));
                row.ValueChanged += OnEffectParamChanged;
                _viewModel.EffectParams.Add(row);
            }
        }
    }

    /// <summary>Active effect entries in a stable order: shape, then texture, then modifiers in catalog order.</summary>
    private static IEnumerable<EffectData> ActiveEffectEntries(LightBlock block)
    {
        var shape = block.GetShapeData();
        if (shape != null) yield return shape;

        var texture = block.GetTextureData();
        if (texture != null) yield return texture;

        foreach (var def in EffectCatalog.Modifiers)
        {
            var data = block.BlockEffects?.FirstOrDefault(e => e.Type == def.Type);
            if (data != null) yield return data;
        }
    }

    /// <summary>Writes an edited param value into every selected block that has the effect.</summary>
    private void OnEffectParamChanged(EffectParamViewModel param)
    {
        if (_isLoading) return;
        if (param.Value is not double value) return;
        if (_timeline.SelectedBlocks == null) return;

        if (_pendingEditPush && _preEditSnapshot != null)
        {
            _timeline.PushUndo(_preEditSnapshot);
            _pendingEditPush = false;
        }

        // Params of exclusive categories (shape/texture) with the same key apply across the
        // whole category (e.g. Combine/Seperate TargetWidth), so a mixed multi-select still
        // updates every block.
        var category = EffectCatalog.Get(param.Effect)?.Category;
        bool crossCategory = category is EffectCategory.Shape or EffectCategory.Texture;

        foreach (var block in _timeline.SelectedBlocks)
        {
            foreach (var e in block.BlockEffects)
            {
                bool matches = e.Type == param.Effect
                    || (crossCategory && EffectCatalog.Get(e.Type)?.Category == category
                        && EffectCatalog.Get(e.Type)!.Parameters.Any(p => p.Key == param.Definition.Key));
                if (matches)
                    e.Params[param.Definition.Key] = value;
            }
        }

        _viewModel.RequestPreview();
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

        bool dualRange = _viewModel.SelectedShape is LightBlock.Effect.Travel
                                                  or LightBlock.Effect.Combine
                                                  or LightBlock.Effect.Seperate;

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

            // Shape/texture: null = mixed selection, leave each block untouched.
            // The setters are idempotent — an unchanged value keeps its entry and params.
            if (_viewModel.SelectedShape is { } shape)
                block.SetShape(shape);
            if (_viewModel.SelectedTexture is { } texture)
                block.SetTexture(texture);

            ApplyEffect(block, LightBlock.Effect.FadeIn,      _viewModel.FadeIn);
            ApplyEffect(block, LightBlock.Effect.FadeOut,     _viewModel.FadeOut);
            ApplyEffect(block, LightBlock.Effect.Strobe,      _viewModel.Strobe);
            ApplyEffect(block, LightBlock.Effect.Repeat,      _viewModel.Repeat);
            ApplyEffect(block, LightBlock.Effect.ChangeColor, _viewModel.ChangeColor);
            ApplyEffect(block, LightBlock.Effect.Comet,       _viewModel.Comet);
            ApplyEffect(block, LightBlock.Effect.FillColor,   _viewModel.FillColor);
        }
    }

    private static void ApplyEffect(LightBlock block, LightBlock.Effect effect, bool? isChecked)
    {
        bool has = block.BlockEffects.Any(e => e.Type == effect);
        if (isChecked == true && !has)
            block.BlockEffects.Add(EffectCatalog.CreateData(effect));
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