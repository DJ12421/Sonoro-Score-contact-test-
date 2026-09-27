using Xunit;

// Tesseract's shared engine takes per-call SetVariable state: OCR tests must run serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
