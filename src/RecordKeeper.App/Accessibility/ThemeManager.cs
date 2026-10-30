using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.Accessibility;

/// <summary>
/// Applies the colour theme, the base font size and the focus-outline appearance.
///
/// The theme dictionary sits at index 0 of the application's merged dictionaries and
/// is swapped wholesale; the shared styles in Common.xaml at index 1 reference its
/// brushes through DynamicResource, so a swap repaints the running window with no restart.
/// </summary>
public static class ThemeManager
{
    private const int ThemeDictionaryIndex = 0;

    /// <summary>The theme actually in force, once System has been resolved.</summary>
    public static AppTheme EffectiveTheme { get; private set; } = AppTheme.Light;

    public static void Apply(AppSettings settings)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        EffectiveTheme = Resolve(settings);

        var themeUri = EffectiveTheme switch
        {
            AppTheme.Dark => "Themes/Dark.xaml",
            AppTheme.HighContrast => "Themes/HighContrast.xaml",
            _ => "Themes/Light.xaml"
        };

        // An absolute pack URI rather than a relative one. A relative source is resolved
        // against the entry assembly, so it only works while this assembly is the one
        // that started the process; anything that hosts these windows from elsewhere
        // gets "cannot locate resource" instead of a theme.
        var assembly = typeof(ThemeManager).Assembly.GetName().Name;

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{assembly};component/{themeUri}", UriKind.Absolute)
        };

        if (resources.MergedDictionaries.Count > ThemeDictionaryIndex)
            resources.MergedDictionaries[ThemeDictionaryIndex] = dictionary;
        else
            resources.MergedDictionaries.Insert(ThemeDictionaryIndex, dictionary);

        ApplyFocusAppearance(settings, resources);
        ApplyFontSize(settings, resources);
    }

    /// <summary>The focus-outline colour used until the user picks one themselves.</summary>
    private static string DefaultFocusColor(AppTheme theme) => theme switch
    {
        // Yellow is the clearest thing to put on a black page.
        AppTheme.Dark => "#FFD400",

        // On the white-and-blue theme the outline has to be a different hue from the
        // blues around it, or a low-vision user cannot tell the focused control from
        // a selected one. Orange reaches 5.1 to 1 on white and reads as "you are here".
        _ => "#C2410C"
    };

    /// <summary>Turns the requested theme into a concrete one.</summary>
    private static AppTheme Resolve(AppSettings settings)
    {
        // Windows high contrast always wins, whatever anyone has chosen. Ignoring it is
        // the single most common way an application becomes unusable for a low-vision user.
        if (SystemParameters.HighContrast) return AppTheme.HighContrast;

        // Until the user has been to Settings, the app chooses: white page, blue accents.
        if (!settings.AppearanceChosenByUser) return AppTheme.Light;

        if (settings.Theme != AppTheme.System) return settings.Theme;

        return IsWindowsInDarkMode() ? AppTheme.Dark : AppTheme.Light;
    }

    private static bool IsWindowsInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ApplyFocusAppearance(AppSettings settings, ResourceDictionary resources)
    {
        var thickness = settings.FocusOutlineThickness;

        // In high contrast the system decides the colours, so a custom outline colour
        // is set aside in favour of the system highlight. Otherwise it is the user's
        // choice if they have made one, and a theme-appropriate default if not.
        var requestedColor = settings.AppearanceChosenByUser
            ? settings.FocusOutlineColor
            : DefaultFocusColor(EffectiveTheme);

        var brush = EffectiveTheme == AppTheme.HighContrast
            ? new SolidColorBrush(SystemColors.HighlightColor)
            : new SolidColorBrush(ParseColor(requestedColor, Color.FromRgb(0xC2, 0x41, 0x0C)));

        brush.Freeze();

        resources["App.FocusBrush"] = brush;
        resources["App.FocusThickness"] = thickness;
        resources["App.FocusBorderThickness"] = new Thickness(thickness);
    }

    private static void ApplyFontSize(AppSettings settings, ResourceDictionary resources)
    {
        resources["App.FontSize"] = settings.FontSize;

        // Derived sizes keep headings proportional as the base size grows.
        resources["App.FontSize.Heading"] = Math.Round(settings.FontSize * 1.35);
        resources["App.FontSize.Small"] = Math.Max(10, Math.Round(settings.FontSize * 0.9));
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color parsed) return parsed;
        }
        catch (FormatException) { }
        catch (NotSupportedException) { }

        return fallback;
    }

    /// <summary>
    /// Re-applies the theme when Windows switches high contrast or light/dark on,
    /// but only while the user has left the choice on "System".
    /// </summary>
    public static void WatchSystemChanges(Func<AppSettings> currentSettings)
    {
        SystemParameters.StaticPropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(SystemParameters.HighContrast)) return;

            var settings = currentSettings();
            if (ShouldFollowWindows(settings)) Apply(settings);
        };

        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category != UserPreferenceCategory.General &&
                args.Category != UserPreferenceCategory.Color) return;

            var settings = currentSettings();
            if (ShouldFollowWindows(settings))
            {
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(settings));
            }
        };
    }

    /// <summary>
    /// Whether a Windows appearance change should repaint the app. True while the user
    /// has asked to follow Windows, and also while they have made no choice at all —
    /// in that second case high contrast being switched on still has to reach us.
    /// </summary>
    private static bool ShouldFollowWindows(AppSettings settings) =>
        !settings.AppearanceChosenByUser || settings.Theme == AppTheme.System;
}
