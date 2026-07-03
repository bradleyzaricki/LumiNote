using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using System.Collections.Generic;
using LumikitApp.Controls;

namespace LumikitApp;

public class LightEffectsComputer
{
    
    /// <param name="containerWidth">Pre-captured container width.</param>
    /// <param name="containerLeft">Pre-captured Canvas.GetLeft value.</param>
    /// <param name="elapsedMs">Milliseconds elapsed since the start of this block. </param>
    /// <param name="serialIntervalMs">The serial hardware update interval in ms (matches ColorUpdateIntervalMs).</param>
    public static Color[] ComputeBlockEffects(LightBlock block, double relPos, double brightnessScale,
        double containerWidth = -1, double containerLeft = -1, double elapsedMs = -1,
        double serialIntervalMs = 50.0)
    {
        int lightcount = 1000;
        Color[] stripColorsIndividual = new Color[lightcount];
        int safeBlockIntensity = (int)(block.Intensity * brightnessScale);
        safeBlockIntensity = Math.Clamp(safeBlockIntensity, 0, 255);
        byte entireIntensity = (byte)safeBlockIntensity;

        static bool Has(LightBlock b, LightBlock.Effect e) =>
            b.BlockEffects != null && b.BlockEffects.Any(x => x.Type == e);
        static EffectData? Get(LightBlock b, LightBlock.Effect e) =>
            b.BlockEffects?.FirstOrDefault(x => x.Type == e);

        // Shape (mutually exclusive: which pixels are lit), texture (mutually exclusive:
        // per-pixel surface modulation) and modifiers (stackable post-passes).
        LightBlock.Effect shape   = block.GetShape();
        LightBlock.Effect texture = block.GetTexture();
        bool hasStrobe           = Has(block, LightBlock.Effect.Strobe);
        bool hasFadeOut          = Has(block, LightBlock.Effect.FadeOut);
        bool hasFadeIn           = Has(block, LightBlock.Effect.FadeIn);
        bool hasRepeat           = Has(block, LightBlock.Effect.Repeat);
        bool hasChangeColor      = Has(block, LightBlock.Effect.ChangeColor);
        bool hasFillColor        = Has(block, LightBlock.Effect.FillColor);
        bool hasComet            = Has(block, LightBlock.Effect.Comet);

        // FillColor replaces "empty" (transparent) areas with a second colour instead of
        // leaving them dark, so e.g. Strobe flickers between two colours and Travel paints
        // the trailing gap. Applied as a final pass below.
        Color fillColor = block.FillColor;

        // During Strobe's off half-period the whole strip would normally go dark. With a fill
        // colour that off-phase becomes a flat fill frame instead of nothing.
        bool strobeOff = false;

        if (hasStrobe)
        {
            double flashesPerSecond = serialIntervalMs;

            // Use real elapsed time when provided (always accurate).
            // Fall back to pixel-based estimate only if elapsedMs wasn't passed.
            double elapsedMsLocal;
            if (elapsedMs >= 0)
            {
                elapsedMsLocal = elapsedMs;
            }
            else
            {
                double width = containerWidth >= 0 ? containerWidth : block.Container.Width;
                elapsedMsLocal = relPos * (width / 100.0) * 1000.0;
            }

            // Snap half-period to the nearest serialIntervalMs multiple so UI and hardware always agree.
            double snappedHalfPeriodMs = Math.Max(
                serialIntervalMs,
                Math.Round(1000.0 / (flashesPerSecond * 2.0) / serialIntervalMs) * serialIntervalMs);

            long halfPeriods = (long)(elapsedMsLocal / snappedHalfPeriodMs);
            if (halfPeriods % 2L == 1L)
            {
                // No fill colour → classic on/off strobe (dark off-phase).
                if (!hasFillColor) return null;
                // With a fill colour the off-phase is rendered as a flat fill frame: clear the
                // primary pattern so the fill pass below paints the whole strip.
                strobeOff = true;
            }
        }
        if (hasFadeOut)
        {
            if (relPos >= 0.5)
                entireIntensity = (byte)Math.Clamp(safeBlockIntensity * ((1.0 - relPos) / 0.5), 0, 255);
        }

        if (hasFadeIn)
        {
            if (relPos <= 0.5)
                entireIntensity = (byte)Math.Clamp(safeBlockIntensity * (relPos / 0.5), 0, 255);
        }

        Color baseColor = block.BlockColor;
        if (hasChangeColor)
        {
            var c0 = block.BlockColor;
            var c1 = block.SecondBlockColor;

            byte r = (byte)(c0.R + (c1.R - c0.R) * relPos);
            byte g = (byte)(c0.G + (c1.G - c0.G) * relPos);
            byte b = (byte)(c0.B + (c1.B - c0.B) * relPos);

            baseColor = Color.FromRgb(r, g, b);
        }

        // Comet tail requests emitted by moving shapes below. Each is (edge position, the
        // direction the tail extends: -1 left / +1 right, an optional wrap span, wrap flag).
        // A "trailing" edge is one the lit region is moving AWAY from, so its tail marks the
        // pixels just vacated — that's what makes a comet chase its own head.
        var cometTails = new List<(double Edge, int Dir, double WrapLo, double WrapHi, bool Wrap)>();

        // Given a segment's two edges (position + signed velocity each), emit a tail off
        // whichever edge is trailing: the left edge if it's moving right, the right edge if
        // it's moving left. Shrink-on-both-sides yields two tails; a translate yields one.
        void EmitEdgeTails(double posA, double velA, double posB, double velB)
        {
            double leftPos, leftVel, rightPos, rightVel;
            if (posA <= posB) { leftPos = posA; leftVel = velA; rightPos = posB; rightVel = velB; }
            else              { leftPos = posB; leftVel = velB; rightPos = posA; rightVel = velA; }
            if (leftVel  > 0) cometTails.Add((leftPos,  -1, 0, 0, false));
            if (rightVel < 0) cometTails.Add((rightPos, +1, 0, 0, false));
        }

        switch (shape)
        {
        case LightBlock.Effect.Travel:
        {
            double s0 = block.StartLight;
            double e0 = block.EndLight;

            double s1 = block.SecondaryStartLight;
            double e1 = block.SecondaryEndLight;

            double start = s0 + (s1 - s0) * relPos;
            double end = e0 + (e1 - e0) * relPos;

            if (hasComet) EmitEdgeTails(start, s1 - s0, end, e1 - e0);

            if (end < start)
            {
                double t = start;
                start = end;
                end = t;
            }

            for (int i = 0; i < lightcount; i++)
            {
                if (start > i || end < i)
                    stripColorsIndividual[i] = new Color(0, 0, 0, 0);
                else
                    stripColorsIndividual[i] = new Color(entireIntensity, baseColor.R, baseColor.G, baseColor.B);
            }
            break;
        }
        case LightBlock.Effect.Combine:
        case LightBlock.Effect.Seperate:
        {
    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    static (double start, double end) Normalize(double a, double b)
    {
        return a <= b ? (a, b) : (b, a);
    }

    double t = Math.Clamp(relPos, 0.0, 1.0);
    bool isSeparate = shape == LightBlock.Effect.Seperate;

    // Input 1 = left side, Input 2 = right side
    var left0 = Normalize(block.StartLight, block.EndLight);
    var right0 = Normalize(block.SecondaryStartLight, block.SecondaryEndLight);

    double leftStart0 = left0.start;
    double leftEnd0 = left0.end;
    double rightStart0 = right0.start;
    double rightEnd0 = right0.end;

    // Make sure they are ordered left-to-right
    double leftCenter0 = (leftStart0 + leftEnd0) * 0.5;
    double rightCenter0 = (rightStart0 + rightEnd0) * 0.5;

    if (leftCenter0 > rightCenter0)
    {
        (leftStart0, rightStart0) = (rightStart0, leftStart0);
        (leftEnd0, rightEnd0) = (rightEnd0, leftEnd0);
    }

    double leftWidth0 = Math.Max(0.0, leftEnd0 - leftStart0);
    double rightWidth0 = Math.Max(0.0, rightEnd0 - rightStart0);

    // Meeting point = midpoint between the inner edges
    double meet = (leftEnd0 + rightStart0) * 0.5;

    // Third field = desired final TOTAL width
    double targetTotalWidth = Math.Max(0.0,
        block.GetShapeData()?.Params.GetValueOrDefault("TargetWidth", 0) ?? 0);

    double totalInitialWidth = leftWidth0 + rightWidth0;

    double targetLeftWidth;
    double targetRightWidth;

    if (totalInitialWidth > 0.0)
    {
        double leftRatio = leftWidth0 / totalInitialWidth;
        double rightRatio = rightWidth0 / totalInitialWidth;

        targetLeftWidth = targetTotalWidth * leftRatio;
        targetRightWidth = targetTotalWidth * rightRatio;
    }
    else
    {
        targetLeftWidth = targetTotalWidth * 0.5;
        targetRightWidth = targetTotalWidth * 0.5;
    }

    // Final state: both segments meet in the middle
    double leftStart1 = meet - targetLeftWidth;
    double leftEnd1 = meet;

    double rightStart1 = meet;
    double rightEnd1 = meet + targetRightWidth;

    // Combine moves original -> meeting state
    // Separate moves meeting state -> original
    double leftStart = isSeparate ? Lerp(leftStart1, leftStart0, t) : Lerp(leftStart0, leftStart1, t);
    double leftEnd   = isSeparate ? Lerp(leftEnd1,   leftEnd0,   t) : Lerp(leftEnd0,   leftEnd1,   t);
    double rightStart= isSeparate ? Lerp(rightStart1,rightStart0,t) : Lerp(rightStart0,rightStart1,t);
    double rightEnd  = isSeparate ? Lerp(rightEnd1,  rightEnd0,  t) : Lerp(rightEnd0,  rightEnd1,  t);

    if (hasComet)
    {
        // Velocity sign = which way each edge lerps. Separate reverses combine's direction.
        double leftStartVel  = isSeparate ? leftStart0  - leftStart1  : leftStart1  - leftStart0;
        double leftEndVel    = isSeparate ? leftEnd0    - leftEnd1    : leftEnd1    - leftEnd0;
        double rightStartVel = isSeparate ? rightStart0 - rightStart1 : rightStart1 - rightStart0;
        double rightEndVel   = isSeparate ? rightEnd0   - rightEnd1   : rightEnd1   - rightEnd0;
        EmitEdgeTails(leftStart,  leftStartVel,  leftEnd,  leftEndVel);
        EmitEdgeTails(rightStart, rightStartVel, rightEnd, rightEndVel);
    }

    double lo1 = Math.Clamp(Math.Min(leftStart, leftEnd), 0, lightcount - 1);
    double hi1 = Math.Clamp(Math.Max(leftStart, leftEnd), 0, lightcount - 1);
    double lo2 = Math.Clamp(Math.Min(rightStart, rightEnd), 0, lightcount - 1);
    double hi2 = Math.Clamp(Math.Max(rightStart, rightEnd), 0, lightcount - 1);

    for (int i = 0; i < lightcount; i++)
    {
        bool inLeft = i >= lo1 && i <= hi1;
        bool inRight = i >= lo2 && i <= hi2;

        stripColorsIndividual[i] = (inLeft || inRight)
            ? new Color(entireIntensity, baseColor.R, baseColor.G, baseColor.B)
            : new Color(0, 0, 0, 0);
    }
    break;
        }
        case LightBlock.Effect.Scanner:
        {
            var sd = block.GetShapeData();
            double width  = Math.Clamp(sd?.Params.GetValueOrDefault("Width", 50) ?? 50, 1, lightcount);
            double cycles = Math.Max(0.0001, sd?.Params.GetValueOrDefault("Cycles", 4) ?? 4);
            bool   wrap   = (sd?.Params.GetValueOrDefault("Wrap", 0) ?? 0) != 0;

            double lo = Math.Min(block.StartLight, block.EndLight);
            double hi = Math.Max(block.StartLight, block.EndLight);
            double spanLen = hi - lo;
            if (spanLen <= 0) break; // zero-width span → nothing lit
            width = Math.Min(width, spanLen);

            double tt = relPos * cycles;

            if (!wrap)
            {
                // Bounce: triangle wave sweeps the bar to a far edge and back.
                double phase = tt - 2.0 * Math.Floor(tt / 2.0); // [0,2)
                double tri   = phase <= 1.0 ? phase : 2.0 - phase; // [0,1]
                double barLo = lo + tri * (spanLen - width);
                double barHi = barLo + width;

                for (int i = 0; i < lightcount; i++)
                    stripColorsIndividual[i] = (i >= barLo && i <= barHi)
                        ? new Color(entireIntensity, baseColor.R, baseColor.G, baseColor.B)
                        : new Color(0, 0, 0, 0);

                if (hasComet)
                {
                    // Moving right on the rising half (tail behind = left), left on the falling half.
                    if (phase < 1.0) cometTails.Add((barLo, -1, 0, 0, false));
                    else             cometTails.Add((barHi, +1, 0, 0, false));
                }
            }
            else
            {
                // Wrap: sawtooth slides the bar one direction; it re-enters the start edge while
                // still exiting the end, so it can straddle both edges — the thing Travel can't do.
                double saw = tt - Math.Floor(tt); // [0,1)
                double offset = saw * spanLen;

                for (int i = 0; i < lightcount; i++)
                {
                    double L = i - lo;
                    bool lit = i >= lo && i <= hi
                               && (((L - offset) % spanLen) + spanLen) % spanLen < width;
                    stripColorsIndividual[i] = lit
                        ? new Color(entireIntensity, baseColor.R, baseColor.G, baseColor.B)
                        : new Color(0, 0, 0, 0);
                }

                // Bar moves toward +; its trailing (back) edge is at the offset, tail wraps the span.
                if (hasComet) cometTails.Add((lo + offset, -1, lo, hi, true));
            }
            break;
        }
        default: // static span
        {
            for (int i = 0; i < lightcount; i++)
            {
                if (block.StartLight > i || block.EndLight < i)
                    stripColorsIndividual[i] = new Color(0, 0, 0, 0);
                else
                    stripColorsIndividual[i] = new Color(entireIntensity, baseColor.R, baseColor.G, baseColor.B);
            }
            break;
        }
        }

        // Comet: paint a fading tail into the pixels each moving edge is vacating. Runs after
        // the shape (needs its motion) but before FillColor (so fill occupies whatever the tail
        // didn't) and before textures (so surface modulation covers the tail too).
        if (hasComet && cometTails.Count > 0)
        {
            int tailLen = (int)Math.Clamp(
                Get(block, LightBlock.Effect.Comet)?.Params.GetValueOrDefault("TailLength", 80) ?? 80,
                1, lightcount);

            foreach (var tail in cometTails)
            {
                int edgeIdx = (int)Math.Round(tail.Edge);
                // Clamp the wrap span to valid pixel indices — EndLight can be 1000 while the
                // strip is indexed 0..999, so an unclamped wrap index would overrun the array.
                int wlo = Math.Clamp((int)Math.Round(tail.WrapLo), 0, lightcount - 1);
                int whi = Math.Clamp((int)Math.Round(tail.WrapHi), 0, lightcount - 1);
                int wspan = whi - wlo + 1;

                for (int d = 1; d <= tailLen; d++)
                {
                    int idx = edgeIdx + tail.Dir * d;
                    if (tail.Wrap)
                    {
                        if (wspan <= 0) break;
                        idx = wlo + (((idx - wlo) % wspan) + wspan) % wspan;
                    }
                    else if (idx < 0 || idx >= lightcount) break;

                    double falloff = 1.0 - (double)d / tailLen; // bright at the edge → 0 at the tip
                    byte a = (byte)Math.Clamp((int)(entireIntensity * falloff), 0, 255);
                    // Max-blend: never dims a lit segment pixel, and where two tails overlap the
                    // brighter wins.
                    if (a > stripColorsIndividual[idx].A)
                        stripColorsIndividual[idx] = new Color(a, baseColor.R, baseColor.G, baseColor.B);
                }
            }
        }

        // FillColor runs before the texture pass so the texture can (optionally) modulate
        // fill pixels too; the mask remembers which pixels the fill painted.
        bool[]? fillMask = null;
        if (hasFillColor)
        {
            // Strobe off-phase: drop the primary pattern so the whole strip becomes the fill.
            if (strobeOff)
                Array.Clear(stripColorsIndividual, 0, lightcount);

            // Paint the fill colour into every empty (transparent) pixel, leaving the lit
            // primary pattern untouched.
            fillMask = new bool[lightcount];
            for (int i = 0; i < lightcount; i++)
            {
                if (stripColorsIndividual[i].A == 0)
                {
                    stripColorsIndividual[i] =
                        new Color(entireIntensity, fillColor.R, fillColor.G, fillColor.B);
                    fillMask[i] = true;
                }
            }
        }

        if (texture != LightBlock.Effect.None)
        {
            var td = block.GetTextureData();

            // AffectFill (default on) lets the texture modulate the fill pixels as well;
            // off restores the pre-texture look of a solid fill behind the textured pattern.
            bool affectFill = (td?.Params.GetValueOrDefault("AffectFill", 1) ?? 1) != 0;

            double left = containerLeft >= 0 ? containerLeft : Canvas.GetLeft(block.Container);
            uint blockSeed = (uint)(left * 1000.0) ^
                             (uint)(block.StartLight * 2654435761u) ^
                             (uint)(block.EndLight * 1597334677u);

            // Skip fill pixels when AffectFill is off; skip transparent (unlit) pixels always.
            bool ModulatesPixel(int i) =>
                stripColorsIndividual[i].A != 0 && (affectFill || fillMask == null || !fillMask[i]);

            if (texture == LightBlock.Effect.Twinkle)
            {
                // Independent per-pixel flicker, re-rolled each frame from a time-seeded hash.
                uint seed = blockSeed ^ (uint)((int)(relPos * 100000.0));
                for (int i = 0; i < lightcount; i++)
                {
                    if (!ModulatesPixel(i)) continue;
                    var c = stripColorsIndividual[i];
                    double flicker = Math.Pow(Rand01(seed ^ (uint)(i * 374761393u)), 2.2);
                    byte a = (byte)Math.Clamp((int)(c.A * flicker), 0, entireIntensity);
                    stripColorsIndividual[i] = new Color(a, c.R, c.G, c.B);
                }
            }
            else if (texture == LightBlock.Effect.Shimmer)
            {
                // A brightness sine wave scrolling along the strip — smooth where Twinkle is noisy.
                double wavelength = Math.Max(1.0, td?.Params.GetValueOrDefault("Wavelength", 120) ?? 120);
                double speed      = td?.Params.GetValueOrDefault("Speed", 2) ?? 2;
                double depth      = Math.Clamp((td?.Params.GetValueOrDefault("Depth", 60) ?? 60) / 100.0, 0.0, 1.0);
                double scroll     = relPos * speed * 2.0 * Math.PI;

                for (int i = 0; i < lightcount; i++)
                {
                    if (!ModulatesPixel(i)) continue;
                    var c = stripColorsIndividual[i];
                    double wave = (Math.Sin(i / wavelength * 2.0 * Math.PI - scroll) + 1.0) * 0.5; // [0,1]
                    double mult = 1.0 - depth * (1.0 - wave);                                      // [1-depth,1]
                    byte a = (byte)Math.Clamp((int)(c.A * mult), 0, entireIntensity);
                    stripColorsIndividual[i] = new Color(a, c.R, c.G, c.B);
                }
            }
            else if (texture == LightBlock.Effect.Sparkle)
            {
                // Occasional white-hot glints over the lit pattern. Sparkles are clustered into
                // small cells so they survive the 1000→N-LED downsample instead of vanishing.
                double density = Math.Clamp((td?.Params.GetValueOrDefault("Density", 10) ?? 10) / 100.0, 0.0, 1.0);
                double speed   = Math.Max(1.0, td?.Params.GetValueOrDefault("Speed", 20) ?? 20);
                const int cell = 6;
                uint seed = blockSeed ^ (uint)((int)(relPos * speed) * 2246822519u);

                for (int i = 0; i < lightcount; i++)
                {
                    if (!ModulatesPixel(i)) continue;
                    if (Rand01(seed ^ (uint)((i / cell) * 2654435761u)) >= density) continue;
                    var c = stripColorsIndividual[i];
                    byte nr = (byte)(c.R + (255 - c.R) * 0.85);
                    byte ng = (byte)(c.G + (255 - c.G) * 0.85);
                    byte nb = (byte)(c.B + (255 - c.B) * 0.85);
                    stripColorsIndividual[i] = new Color(c.A, nr, ng, nb);
                }
            }
        }

        int repeatCount = (int)(Get(block, LightBlock.Effect.Repeat)?.Params.GetValueOrDefault("Count", 1) ?? 1);
        if (hasRepeat && repeatCount > 1)
        {
            int repeats = repeatCount;
            repeats = Math.Clamp(repeats, 1, lightcount);

            int segmentSize = lightcount / repeats;

            Color[] repeated = new Color[lightcount];

            for (int r = 0; r < repeats; r++)
            {
                int segStart = r * segmentSize;
                int segEnd = (r == repeats - 1) ? lightcount : segStart + segmentSize;

                for (int i = segStart; i < segEnd; i++)
                {
                    double localNorm = (double)(i - segStart) / (segEnd - segStart);
                    int virtualIndex = (int)(localNorm * (lightcount - 1));

                    var c = stripColorsIndividual[virtualIndex];
                    if (c.A != 0)
                        repeated[i] = c;
                }
            }

            return repeated;
        }

        return stripColorsIndividual;
    }

    // Fast integer hash (fmix32-style) for the stateless per-pixel textures.
    private static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352d;
        x ^= x >> 15; x *= 0x846ca68b;
        x ^= x >> 16;
        return x;
    }

    private static double Rand01(uint x) => (Hash(x) & 0xFFFFFF) / (double)0x1000000;


    /// <summary>
    /// Pure computation — no Avalonia calls. Safe to run on any thread.
    /// </summary>
    public static Color[] ComputePreviewFrame(
        double currentMs,
        List<(LightBlock Block, double Left, double Width)> blocks,
        double slotWidth, double serialIntervalMs = 50.0)
    {
        double slotMs = TimelineView.MsPerSlot;
        var finalLeds = new Color[1000];

        foreach (var (block, left, width) in blocks)
        {
            double blockTimeOffset = (left - blocks[0].Left) * slotMs / slotWidth;
            double localTime       = currentMs - blockTimeOffset;
            if (localTime < 0) continue;

            double relPos = Math.Clamp(localTime / (width * slotMs / slotWidth), 0.0, 1.0);

            Color[] blockLeds = ComputeBlockEffects(
                block, relPos, 100,
                containerWidth:   width,
                containerLeft:    left,
                elapsedMs:        localTime,
                serialIntervalMs: serialIntervalMs);

            if (blockLeds == null) continue; // strobe off-phase — leave LEDs dark

            for (int i = 0; i < finalLeds.Length; i++)
                finalLeds[i] = blockLeds[i];
        }

        return finalLeds;
    }
}
