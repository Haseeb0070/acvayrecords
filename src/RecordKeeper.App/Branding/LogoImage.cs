using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RecordKeeper.App.Branding;

/// <summary>
/// Turns the vector logo into a bitmap, for the places that cannot take vector art.
///
/// The only consumer is the PDF header. Rendering it from the same XAML geometry the
/// window uses, rather than keeping a separate image file around, means the printed
/// logo cannot drift out of step with the one on screen.
/// </summary>
public static class LogoImage
{
    private const string ResourceKey = "Brand.LogoMark";

    private static byte[]? _cachedPng;

    /// <summary>
    /// The logo as a PNG, rendered at a size that stays crisp in print. Cached, so the
    /// cost is paid once however many reports are exported.
    ///
    /// Returns null rather than throwing if anything goes wrong: a missing logo should
    /// cost you the decoration at the top of a report, not the report itself.
    /// </summary>
    public static byte[]? GetPng(int sizeInPixels = 256)
    {
        if (_cachedPng is not null) return _cachedPng;

        try
        {
            var application = Application.Current;
            if (application is null) return null;

            // RenderTargetBitmap has to run on a UI thread.
            if (!application.Dispatcher.CheckAccess())
                return application.Dispatcher.Invoke(() => GetPng(sizeInPixels));

            if (application.TryFindResource(ResourceKey) is not DrawingImage drawing) return null;

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(drawing, new Rect(0, 0, sizeInPixels, sizeInPixels));
            }

            var target = new RenderTargetBitmap(
                sizeInPixels, sizeInPixels, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));

            using var stream = new MemoryStream();
            encoder.Save(stream);

            _cachedPng = stream.ToArray();
            return _cachedPng;
        }
        catch (Exception exception)
        {
            App.Log("Rendering the logo failed", exception);
            return null;
        }
    }
}
