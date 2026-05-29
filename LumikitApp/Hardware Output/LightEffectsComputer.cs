using System;
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

        bool hasTravel = block.BlockEffects != null &&
                         block.BlockEffects.Contains(LightBlock.Effect.Travel);
        bool hasCombineOrSeperate = block.BlockEffects != null &&
                                   (block.BlockEffects.Contains(LightBlock.Effect.Combine) ||
                                    block.BlockEffects.Contains(LightBlock.Effect.Seperate));
        bool hasStrobe = block.BlockEffects != null && block.BlockEffects.Contains(LightBlock.Effect.Strobe);
        bool hasFadeOut = block.BlockEffects != null && block.BlockEffects.Contains(LightBlock.Effect.FadeOut);
        bool hasFadeIn = block.BlockEffects != null && block.BlockEffects.Contains(LightBlock.Effect.FadeIn);
        bool hasRepeat = block.BlockEffects != null && block.BlockEffects.Contains(LightBlock.Effect.Repeat);
        bool hasChangeColor = block.BlockEffects != null && block.BlockEffects.Contains(LightBlock.Effect.ChangeColor);
        bool hasTwinkle = block.BlockEffects != null && block.BlockEffects.Contains(LightBlock.Effect.Twinkle);

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
                return null;
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

        if (!hasTravel && !hasCombineOrSeperate)
        {
            for (int i = 0; i < lightcount; i++)
            {
                if (block.StartLight > i || block.EndLight < i)
                    stripColorsIndividual[i] = new Color(0, 0, 0, 0);
                else
                    stripColorsIndividual[i] = new Color(entireIntensity, baseColor.R, baseColor.G, baseColor.B);
            }
        }
        else if (hasTravel)
        {
            double s0 = block.StartLight;
            double e0 = block.EndLight;

            double s1 = block.SecondaryStartLight;
            double e1 = block.SecondaryEndLight;

            double start = s0 + (s1 - s0) * relPos;
            double end = e0 + (e1 - e0) * relPos;

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
        }
else if (hasCombineOrSeperate)
{
    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    static (double start, double end) Normalize(double a, double b)
    {
        return a <= b ? (a, b) : (b, a);
    }

    double t = Math.Clamp(relPos, 0.0, 1.0);
    bool isSeparate = block.BlockEffects.Contains(LightBlock.Effect.Seperate);

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
    double targetTotalWidth = Math.Max(0.0, block.AdditionalIndividualInput1);

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
}
        if (hasTwinkle)
        {
            double left = containerLeft >= 0 ? containerLeft : Canvas.GetLeft(block.Container);
            uint seed = (uint)(left * 1000.0) ^
                        (uint)(block.StartLight * 2654435761u) ^
                        (uint)(block.EndLight * 1597334677u) ^
                        (uint)((int)(relPos * 100000.0));

            static uint Hash(uint x)
            {
                x ^= x >> 16;
                x *= 0x7feb352d;
                x ^= x >> 15;
                x *= 0x846ca68b;
                x ^= x >> 16;
                return x;
            }

            static double Rand01(uint x) => (Hash(x) & 0xFFFFFF) / (double)0x1000000;

            for (int i = 0; i < lightcount; i++)
            {
                var c = stripColorsIndividual[i];
                if (c.A == 0) continue;

                double r = Rand01(seed ^ (uint)(i * 374761393u));
                double flicker = Math.Pow(r, 2.2);
                byte a = (byte)Math.Clamp((int)(c.A * flicker), 0, entireIntensity);

                stripColorsIndividual[i] = new Color(a, c.R, c.G, c.B);
            }
        }

        if (hasRepeat && block.AdditionalIndividualInput2 > 1)
        {
            int repeats = (int)block.AdditionalIndividualInput2;
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
