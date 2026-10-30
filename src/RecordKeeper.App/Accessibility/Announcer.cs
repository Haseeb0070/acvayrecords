using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.Accessibility;

/// <summary>What kind of event the user is being told about.</summary>
public enum AnnouncementKind
{
    Information,
    Success,
    Warning,
    Error
}

/// <summary>
/// Speaks status messages without moving focus.
///
/// Three things happen for every announcement:
///  1. The text lands in the status bar, which stays on screen. Nothing auto-dismisses,
///     so a user who missed it can go back and read it with F6.
///  2. The status bar is a UI Automation live region, and raising the live-region-changed
///     event makes Narrator read the new text while keyboard focus stays put.
///  3. Optionally the app speaks it in its own voice, for users who do not run Narrator
///     all the time, and plays a short distinct sound.
/// </summary>
public sealed class Announcer
{
    private TextBlock? _liveRegion;
    private AppSettings _settings;
    private SpeechOutput? _speech;

    public Announcer(AppSettings settings) => _settings = settings;

    /// <summary>The most recent message, kept so the status bar and the help screen agree.</summary>
    public string LastMessage { get; private set; } = string.Empty;

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        if (!settings.SpeakConfirmations) _speech?.Dispose();
        if (!settings.SpeakConfirmations) _speech = null;
    }

    /// <summary>Called once by the main window to hand over its status-bar text block.</summary>
    public void AttachLiveRegion(TextBlock liveRegion)
    {
        _liveRegion = liveRegion;
        AutomationProperties.SetLiveSetting(liveRegion, AutomationLiveSetting.Polite);
    }

    public void Say(string message, AnnouncementKind kind = AnnouncementKind.Information)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        LastMessage = message;

        var target = _liveRegion;
        if (target is null) return;

        // Announcements can arrive from a background operation; the UI must be touched
        // on the dispatcher thread.
        if (!target.Dispatcher.CheckAccess())
        {
            target.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Deliver(target, message, kind));
            return;
        }

        Deliver(target, message, kind);
    }

    public void SayError(string message) => Say(message, AnnouncementKind.Error);

    public void SaySuccess(string message) => Say(message, AnnouncementKind.Success);

    private void Deliver(TextBlock target, string message, AnnouncementKind kind)
    {
        target.Text = message;
        AutomationProperties.SetName(target, message);

        // An assertive live region interrupts, which suits an error. Anything else
        // waits politely for the reader to finish its current sentence.
        AutomationProperties.SetLiveSetting(target,
            kind == AnnouncementKind.Error ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);

        RaiseLiveRegionChanged(target);
        PlayCue(kind);
        Speak(message);
    }

    private static void RaiseLiveRegionChanged(TextBlock target)
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;

        var peer = UIElementAutomationPeer.FromElement(target) ??
                   UIElementAutomationPeer.CreatePeerForElement(target);

        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void PlayCue(AnnouncementKind kind)
    {
        if (!_settings.PlaySoundCues) return;

        try
        {
            switch (kind)
            {
                case AnnouncementKind.Success: System.Media.SystemSounds.Asterisk.Play(); break;
                case AnnouncementKind.Warning: System.Media.SystemSounds.Exclamation.Play(); break;
                case AnnouncementKind.Error: System.Media.SystemSounds.Hand.Play(); break;
            }
        }
        catch (Exception)
        {
            // A machine with no audio device must not take the app down.
        }
    }

    private void Speak(string message)
    {
        if (!_settings.SpeakConfirmations) return;

        try
        {
            _speech ??= new SpeechOutput();
            _speech.Speak(message);
        }
        catch (Exception)
        {
            // No voice installed, or the speech platform is missing. Narrator and the
            // status bar still carry the message, so this is not worth interrupting for.
        }
    }

    /// <summary>
    /// Announces the position of the item the user has just moved to. Narrator reports
    /// this for list boxes automatically, but saying it again on demand helps a user who
    /// has arrowed around and lost their place.
    /// </summary>
    public void SayPosition(string itemName, int oneBasedIndex, int total)
    {
        if (total <= 0)
        {
            Say("The list is empty.");
            return;
        }
        Say($"{itemName}. {oneBasedIndex} of {total}.");
    }

    public void Dispose() => _speech?.Dispose();
}
