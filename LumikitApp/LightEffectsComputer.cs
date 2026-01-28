using System;
using Avalonia.Media;

namespace LumikitApp;

public class LightEffectsComputer
{
    public static Color[] ComputeBlockEffects(LightBlock block, double relPos, double brightnessScale)
{
    int lightcount = 1000;
    Color[] stripColorsIndividual = new Color[lightcount];
    int safeBlockIntensity = (int)(block.Intensity * brightnessScale);
    byte entireIntensity = Convert.ToByte(safeBlockIntensity);

    bool hasTravel = block.BlockEffects != null &&
                     block.BlockEffects.Contains(LightBlock.Effect.Travel);
    bool hasCombineOrSeperate = block.BlockEffects != null &&
                     (block.BlockEffects.Contains(LightBlock.Effect.Combine) || block.BlockEffects.Contains(LightBlock.Effect.Seperate));
    bool hasStrobe = block.BlockEffects != null && (block.BlockEffects.Contains(LightBlock.Effect.Strobe));
    bool hasFadeOut = block.BlockEffects != null && (block.BlockEffects.Contains(LightBlock.Effect.FadeOut));
    bool hasFadeIn = block.BlockEffects != null && (block.BlockEffects.Contains(LightBlock.Effect.FadeIn));
    bool hasRepeat = block.BlockEffects != null && (block.BlockEffects.Contains(LightBlock.Effect.Repeat));

    if (hasStrobe)
    {
        if (int.IsEvenInteger((int)(relPos * block.Container.Width / 4)))
            return null;
    }

    if (hasFadeOut)
    {
        if (relPos >= 0.5)
            entireIntensity = (byte)(safeBlockIntensity * ((1.0 - relPos) / 0.5));
    }

    if (hasFadeIn)
    {
        if (relPos <= 0.5)
            entireIntensity = (byte)(safeBlockIntensity * (relPos / 0.5));
    }

    if (!hasTravel && !hasCombineOrSeperate)
    {
        for (int i = 0; i < lightcount; i++)
        {
            if (block.StartLight > i || block.EndLight < i)
                stripColorsIndividual[i] = new Color(0, 0, 0, 0);
            else
                stripColorsIndividual[i] = new Color(entireIntensity, block.BlockColor.R, block.BlockColor.G, block.BlockColor.B);
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
                stripColorsIndividual[i] = new Color(entireIntensity, block.BlockColor.R, block.BlockColor.G, block.BlockColor.B);
        }
    }
    else if (hasCombineOrSeperate)
    {
        double s0 = block.StartLight;
        double e0 = block.EndLight;

        double s2 = block.SecondaryStartLight;
        double e2 = block.SecondaryEndLight;

        double wTargetTotal = block.AdditionalIndividualInput1;

        double w0 = Math.Abs(e0 - s0);
        double w2 = Math.Abs(e2 - s2);

        double c0 = (s0 + e0) * 0.5;
        double c2 = (s2 + e2) * 0.5;

        double t = Math.Clamp(relPos, 0, 1);

        bool isSeparate = block.BlockEffects.Contains(LightBlock.Effect.Seperate);

        bool firstIsLeft = c0 <= c2;

        double leftS = firstIsLeft ? Math.Min(s0, e0) : Math.Min(s2, e2);
        double leftE = firstIsLeft ? Math.Max(s0, e0) : Math.Max(s2, e2);
        double rightS = firstIsLeft ? Math.Min(s2, e2) : Math.Min(s0, e0);
        double rightE = firstIsLeft ? Math.Max(s2, e2) : Math.Max(s0, e0);

        double wL = firstIsLeft ? w0 : w2;
        double wR = firstIsLeft ? w2 : w0;

        double cL0 = (leftS + leftE) * 0.5;
        double cR0 = (rightS + rightE) * 0.5;

        double M = (cL0 + cR0) * 0.5;

        double gap0 = rightS - leftE;

        double desiredGap = Math.Min(0.0, wTargetTotal - (wL + wR));

        double gap;
        if (isSeparate)
            gap = desiredGap + (gap0 - desiredGap) * t;
        else
            gap = gap0 + (desiredGap - gap0) * t;

        double dCenters = (wL + wR) * 0.5 + gap;

        double cLp = M - dCenters * 0.5;
        double cRp = M + dCenters * 0.5;

        double sLp = cLp - wL * 0.5;
        double eLp = cLp + wL * 0.5;

        double sRp = cRp - wR * 0.5;
        double eRp = cRp + wR * 0.5;

        double s0p, e0p, s2p, e2p;

        if (firstIsLeft)
        {
            s0p = sLp; e0p = eLp;
            s2p = sRp; e2p = eRp;
        }
        else
        {
            s2p = sLp; e2p = eLp;
            s0p = sRp; e0p = eRp;
        }

        double lo1 = Math.Clamp(Math.Min(s0p, e0p), 0, lightcount - 1);
        double hi1 = Math.Clamp(Math.Max(s0p, e0p), 0, lightcount - 1);
        double lo2 = Math.Clamp(Math.Min(s2p, e2p), 0, lightcount - 1);
        double hi2 = Math.Clamp(Math.Max(s2p, e2p), 0, lightcount - 1);

        for (int i = 0; i < lightcount; i++)
        {
            bool inFirst = (i >= lo1 && i <= hi1);
            bool inSecond = (i >= lo2 && i <= hi2);

            if (inFirst || inSecond)
                stripColorsIndividual[i] = new Color(entireIntensity, block.BlockColor.R, block.BlockColor.G, block.BlockColor.B);
            else
                stripColorsIndividual[i] = new Color(0, 0, 0, 0);
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

    
}