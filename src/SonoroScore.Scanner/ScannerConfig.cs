// © 2026 RadianceNoMore (Sonoro-Score, MIT).
// Sonata matcher / OCR strategy adapted from Tacet-Lab (https://github.com/DJ12421/Tacet-Lab, GPL-3.0).
// See NOTICES.md.

namespace SonoroScore.Scanner;

/// <summary>
/// Central QA-tunable knobs for the scanner pipeline.
///
/// Calibration basis (see integration_todo.md Priority 3):
/// test corpus at <c>publish\AlephalSonata\aleph_images</c> (session_20260927_204453).
/// Thresholds below are the values the pipeline actually enforces; tune here,
/// re-run the Scanner.Cli test suite, and compare detection rates before changing them.
/// </summary>
public static class ScannerConfig
{
    // ── OCR engine selection (Priority 4 "WinOcr removal gate") ────────────
    /// <summary>
    /// When true (default), Windows.Media.Ocr is used as fallback when Tesseract
    /// is unavailable, throws (e.g. missing tessdata/eng.traineddata), or returns
    /// an empty result. Set to false for QA Tesseract-only A/B runs.
    /// CLI: <c>--no-winocr-fallback</c>. Env: <c>SONORO_NO_WINOCR=1</c>.
    /// </summary>
    public static bool UseWindowsOcrFallback { get; set; } = true;

    /// <summary>
    /// When true (default), an empty/whitespace Tesseract result also triggers
    /// the Windows OCR fallback (subject to <see cref="UseWindowsOcrFallback"/>).
    /// This is the "Tesseract vs WinOcr fallback threshold" in operational form:
    /// Tesseract often returns "" on stylized headers instead of low-confidence text.
    /// </summary>
    public static bool FallbackOnEmptyTesseractResult { get; set; } = true;

    /// <summary>
    /// Minimum trimmed text length considered a "real" Tesseract result.
    /// Results shorter than this fall back to Windows OCR (when allowed).
    /// Calibrated default: 2 (single-char OCR hits are almost always noise).
    /// </summary>
    public static int OcrMinTextLength { get; set; } = 2;

    // ── Confidence thresholds (Priority 3) ─────────────────────────────────
    /// <summary>Per-region OCR engine routing.</summary>
    public enum OcrEnginePreference
    {
        /// <summary>Tesseract primary with Windows fallback (standard path).</summary>
        Auto,
        /// <summary>Tesseract only (pure QA mode; empty results stay empty).</summary>
        TesseractOnly,
        /// <summary>Windows OCR only.</summary>
        WindowsOnly,
    }

    /// <summary>
    /// Engine used for the echo-name strip. Default <c>WindowsOnly</c>: measured
    /// 91.7% vs 66.0% for Tesseract on the 300-image corpus (stylized name font).
    /// CLI: <c>--name-engine auto|tesseract|windows</c>.
    /// </summary>
    public static OcrEnginePreference NameEngine { get; set; } = OcrEnginePreference.WindowsOnly;

    /// <summary>Icon pixel-signature win threshold (EchoRecognizer step 9). Default 0.70.</summary>
    public static double SonataIconMinConfidence { get; set; } = 0.70;

    /// <summary>
    /// Tesseract PSM knobs (mirrors Tacet ocr-pool per-kind modes; A/B-testable).
    /// Name defaults to SingleBlock (their name-kind mode); substats block too.
    /// </summary>
    public static Tesseract.PageSegMode NameRegionPsm { get; set; } = Tesseract.PageSegMode.SingleBlock;

    public static Tesseract.PageSegMode SubstatBlockPsm { get; set; } = Tesseract.PageSegMode.SingleBlock;

    /// <summary>Echo-name fuzzy catalog match floor (FuzzyMatcher.ClosestMatch). Default 0.68.</summary>
    public static float EchoNameMinConfidence { get; set; } = 0.68f;

    /// <summary>Sonata OCR-text fuzzy match floor. Default 0.55 (lower: OCR garbles set names).</summary>
    public static float SonataTextMinConfidence { get; set; } = 0.55f;

    /// <summary>
    /// Reads the <c>SONORO_NO_WINOCR</c> env var (1/true → disable fallback).
    /// Called by CLI/GUI entry points before scanning.
    /// </summary>
    public static void ApplyEnvironment()
    {
        string? v = System.Environment.GetEnvironmentVariable("SONORO_NO_WINOCR");
        if (!string.IsNullOrWhiteSpace(v) &&
            (v == "1" || v.Equals("true", System.StringComparison.OrdinalIgnoreCase)))
        {
            UseWindowsOcrFallback = false;
        }
    }
}
