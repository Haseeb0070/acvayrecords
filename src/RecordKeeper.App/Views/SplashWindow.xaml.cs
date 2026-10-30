using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.Views;

/// <summary>
/// The acvaysoft startup screen.
///
/// Three rules govern it, all of them there because the person using this app may not
/// be able to see it:
///   it never lasts long, and never blocks for more than a second and a half;
///   any key or click dismisses it at once;
///   it does not appear at all for a screen-reader user, or when Windows has been told
///   to turn animations off.
/// </summary>
public partial class SplashWindow : Window
{
    private const int TotalMilliseconds = 1400;
    private const int StaticMilliseconds = 650;

    private bool _closing;

    public SplashWindow()
    {
        InitializeComponent();

        PreviewKeyDown += (_, _) => Dismiss();
        PreviewMouseDown += (_, _) => Dismiss();

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Whether the startup screen should be shown at all.
    /// </summary>
    public static bool ShouldShow(AppSettings settings)
    {
        if (!settings.ShowStartupLogo) return false;

        // A splash screen tells a screen-reader user nothing and delays everything.
        if (IsScreenReaderRunning()) return false;

        return true;
    }

    /// <summary>
    /// Whether to animate, as opposed to showing the finished frame briefly.
    /// Respects the Windows setting for turning animations off, which some people use
    /// because motion makes them ill and others because it makes text hard to track.
    /// </summary>
    private static bool ShouldAnimate() => SystemParameters.ClientAreaAnimation;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ShouldAnimate())
        {
            ShowFinishedFrame();
            CloseAfter(StaticMilliseconds, fade: false);
            return;
        }

        Animate();
        CloseAfter(TotalMilliseconds, fade: true);
    }

    /// <summary>Puts every element straight into its end state, with no motion.</summary>
    private void ShowFinishedFrame()
    {
        Mark.Opacity = 1;
        MarkScale.ScaleX = 1;
        MarkScale.ScaleY = 1;

        Wordmark.Opacity = 1;
        WordmarkShift.Y = 0;

        ProductName.Opacity = 1;
        PoweredBy.Opacity = 1;
        AccentLine.Width = 320;
    }

    private void Animate()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // The mark fades up and settles to full size.
        Begin(Mark, OpacityProperty, 0, 1, 0, 420, ease);
        Begin(MarkScale, ScaleTransform.ScaleXProperty, 0.82, 1, 0, 420, ease);
        Begin(MarkScale, ScaleTransform.ScaleYProperty, 0.82, 1, 0, 420, ease);

        // The wordmark follows it up from slightly below.
        Begin(Wordmark, OpacityProperty, 0, 1, 180, 420, ease);
        Begin(WordmarkShift, TranslateTransform.YProperty, 14, 0, 180, 420, ease);

        Begin(ProductName, OpacityProperty, 0, 1, 380, 360, ease);
        Begin(PoweredBy, OpacityProperty, 0, 1, 520, 360, ease);

        // A line drawing itself across, rather than a spinner that says nothing.
        Begin(AccentLine, WidthProperty, 0, 320, 440, 760, ease);
    }

    private static void Begin(System.Windows.Media.Animation.IAnimatable target,
                              DependencyProperty property,
                              double from, double to,
                              int delayMilliseconds, int durationMilliseconds,
                              IEasingFunction ease)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };

        target.BeginAnimation(property, animation);
    }

    private void CloseAfter(int milliseconds, bool fade)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (fade) FadeOutAndClose();
            else CloseOnce();
        };
        timer.Start();
    }

    /// <summary>Called when the user presses a key or clicks: stop everything, close now.</summary>
    private void Dismiss()
    {
        if (_closing) return;
        CloseOnce();
    }

    private void FadeOutAndClose()
    {
        if (_closing) return;
        _closing = true;

        var fade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(220)
        };

        fade.Completed += (_, _) => ReallyClose();
        BeginAnimation(OpacityProperty, fade);
    }

    private void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        ReallyClose();
    }

    private void ReallyClose()
    {
        try { Close(); }
        catch (InvalidOperationException) { /* already closing */ }
    }

    // ----------------------------------------------------------------- screen reader

    private const uint SpiGetScreenReader = 0x0046;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action, uint param, ref bool result, uint update);

    /// <summary>
    /// Asks Windows whether an assistive screen reader is running. Narrator and the
    /// common third-party readers set this flag. If the call fails we assume no reader,
    /// which only costs a short splash screen.
    /// </summary>
    private static bool IsScreenReaderRunning()
    {
        try
        {
            var running = false;
            return SystemParametersInfo(SpiGetScreenReader, 0, ref running, 0) && running;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
