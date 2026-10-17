using PdfSharp.Fonts;
using PdfSharp.WPFonts;

namespace Fitzobit.Infrastructure.Exports;

/// <summary>
/// Supplies the TrueType font for PDF exports. PDFsharp's cross-platform build has no font access of its own, so the
/// regular and bold faces of a common sans-serif font are read from the operating system: Arial/Segoe UI on Windows,
/// Liberation Sans or DejaVu Sans on Linux (the Docker image installs <c>fonts-liberation</c>), Arial on macOS. When none is
/// installed, the Segoe WP font embedded in PDFsharp is used, so PDF export never fails for lack of a font.
/// Every family name resolves to that font, so documents never fail on a missing face.
/// </summary>
public static class PdfFonts
{
    public const string Family = "Fitzobit Sans";
    private const string RegularFace = "FitzobitSans-Regular";
    private const string BoldFace = "FitzobitSans-Bold";

    private static readonly Lazy<(byte[] Regular, byte[] Bold, string Source)> Fonts = new(Load);
    private static readonly object Gate = new();
    private static bool registered;

    /// <summary>Where the PDF font comes from (a file path, or "embedded Segoe WP").</summary>
    public static string Source => Fonts.Value.Source;

    /// <summary>Candidate (regular, bold) font files, most preferred first.</summary>
    private static IEnumerable<(string, string)> Candidates()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrEmpty(windows))
        {
            yield return (Path.Combine(windows, "arial.ttf"), Path.Combine(windows, "arialbd.ttf"));
            yield return (Path.Combine(windows, "segoeui.ttf"), Path.Combine(windows, "segoeuib.ttf"));
        }
        foreach (var dir in new[] { "/usr/share/fonts/truetype/liberation", "/usr/share/fonts/truetype/liberation2", "/usr/share/fonts/liberation-sans" })
            yield return (Path.Combine(dir, "LiberationSans-Regular.ttf"), Path.Combine(dir, "LiberationSans-Bold.ttf"));
        foreach (var dir in new[] { "/usr/share/fonts/truetype/dejavu", "/usr/share/fonts/dejavu", "/usr/share/fonts/TTF" })
            yield return (Path.Combine(dir, "DejaVuSans.ttf"), Path.Combine(dir, "DejaVuSans-Bold.ttf"));
        yield return ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf");
        yield return ("/Library/Fonts/Arial.ttf", "/Library/Fonts/Arial Bold.ttf");
    }

    /// <summary>Installs the font resolver once per process (PDFsharp does not allow changing it later).</summary>
    public static void EnsureRegistered()
    {
        if (registered) return;
        lock (Gate)
        {
            if (registered) return;
            _ = Fonts.Value;
            GlobalFontSettings.FontResolver ??= new Resolver();
            registered = true;
        }
    }

    /// <summary>The fonts embedded in PDFsharp (used when the operating system has none of the candidates).</summary>
    public static (byte[] Regular, byte[] Bold) Embedded() => (FontDataHelper.SegoeWP, FontDataHelper.SegoeWPBold);

    private static (byte[] Regular, byte[] Bold, string Source) Load()
    {
        foreach (var (regular, bold) in Candidates())
            if (File.Exists(regular))
                return (File.ReadAllBytes(regular), File.ReadAllBytes(File.Exists(bold) ? bold : regular), regular);
        var (embeddedRegular, embeddedBold) = Embedded();
        return (embeddedRegular, embeddedBold, "embedded Segoe WP");
    }

    private sealed class Resolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(isBold ? BoldFace : RegularFace, false, isItalic && !isBold);

        public byte[] GetFont(string faceName) => faceName == BoldFace ? Fonts.Value.Bold : Fonts.Value.Regular;
    }
}
