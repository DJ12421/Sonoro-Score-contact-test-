using System.Drawing;
using System.Drawing.Imaging;

namespace SonoroScore.Scanner;

/// <summary>
/// Pixel-based rarity classifier — ported from Tacet-Lab visual.ts classifyRarityPixels.
/// Votes by HSL hue of bright, saturated pixels in the rarity star band.
/// </summary>
public static class RarityClassifier
{
    public static int Classify(Bitmap rarityBand)
    {
        int[] votes = new int[6]; // index = rarity (1–5)
        int total = 0;

        for (int y = 0; y < rarityBand.Height; y++)
        {
            for (int x = 0; x < rarityBand.Width; x++)
            {
                var c = rarityBand.GetPixel(x, y);
                if (c.A <= 180) continue;

                float l = c.GetBrightness();
                if (l < 0.58f) continue;

                float h = c.GetHue(); // 0–360
                float s = c.GetSaturation();

                int rarity;
                if (s < 0.20f)
                    rarity = 1;          // grey/white → 1-star
                else if (h >= 75 && h < 175)
                    rarity = 2;          // green
                else if (h >= 175 && h < 250)
                    rarity = 3;          // blue
                else if (h >= 250 && h < 335)
                    rarity = 4;          // purple
                else if (h >= 32 && h < 75)
                    rarity = 5;          // gold/amber
                else
                    continue;

                votes[rarity]++;
                total++;
            }
        }

        if (total == 0) return 0;

        int bestRarity = 1, bestVotes = 0;
        for (int r = 1; r <= 5; r++)
        {
            if (votes[r] > bestVotes) { bestVotes = votes[r]; bestRarity = r; }
        }
        return bestRarity;
    }
}
