using System;
using System.Drawing;

namespace SonoroScore.Scanner;

/// <summary>
/// Pixel-based pattern matching for echo stat panel elements where OCR may drop short tokens
/// (such as 2-letter "HP" labels that lack surrounding word context).
/// </summary>
public static class StatPixelMatcher
{
    /// <summary>
    /// Checks whether the given row crop (or slot within a substats block) contains the "HP" label.
    /// In 1080p, "HP" is ~27px wide with an unmistakable H-crossbar and P-loop pattern.
    /// </summary>
    public static bool DetectHp(Bitmap blockBmp, int slotIndex = -1, int totalSlots = 5)
    {
        int rowY, rowH;
        if (slotIndex >= 0)
        {
            rowY = (int)(blockBmp.Height * ((float)slotIndex / totalSlots));
            rowH = (int)(blockBmp.Height / (float)totalSlots);
        }
        else
        {
            rowY = 0;
            rowH = blockBmp.Height;
        }

        int w = Math.Min(blockBmp.Width, 100);
        bool[,] grid = new bool[w, rowH];

        // Binarize: find bright pixels (lightness > 100 on dark background)
        for (int y = 0; y < rowH; y++)
        {
            int srcY = Math.Clamp(rowY + y, 0, blockBmp.Height - 1);
            for (int x = 0; x < w; x++)
            {
                var c = blockBmp.GetPixel(x, srcY);
                int brightness = (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
                grid[x, y] = brightness > 100;
            }
        }

        // Find the start of the first letter after the leading star/cross icon (x > 20)
        int startX = -1;
        for (int x = 20; x < w; x++)
        {
            int colCount = 0;
            for (int y = 4; y < rowH - 4; y++)
            {
                if (grid[x, y]) colCount++;
            }
            if (colCount >= 4)
            {
                startX = x;
                break;
            }
        }

        if (startX < 0) return false;

        // Find the end of the first word (group of non-blank columns)
        int endX = startX;
        int blankCols = 0;
        for (int x = startX; x < w; x++)
        {
            int colCount = 0;
            for (int y = 4; y < rowH - 4; y++)
            {
                if (grid[x, y]) colCount++;
            }

            if (colCount == 0)
            {
                blankCols++;
                if (blankCols >= 4)
                {
                    endX = x - 4;
                    break;
                }
            }
            else
            {
                blankCols = 0;
                endX = x;
            }
        }

        int wordWidth = endX - startX + 1;
        // In 1080p panel crop:
        // "HP" is 22..35px wide.
        // "ATK" and "DEF" are 38..46px wide.
        // Multi-word stats are > 60px wide.
        return wordWidth is >= 22 and <= 35;
    }
}
