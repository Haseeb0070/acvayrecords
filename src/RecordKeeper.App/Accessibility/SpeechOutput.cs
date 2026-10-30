using System.Speech.Synthesis;

namespace RecordKeeper.App.Accessibility;

/// <summary>
/// The application's own voice, used for the optional spoken confirmations.
///
/// This is deliberately separate from Narrator. A user who keeps Narrator switched off,
/// or who runs only Magnifier, still gets told out loud when a record saved or a delete
/// needs confirming.
/// </summary>
public sealed class SpeechOutput : IDisposable
{
    private readonly SpeechSynthesizer _synthesizer;

    public SpeechOutput()
    {
        _synthesizer = new SpeechSynthesizer();
        _synthesizer.SetOutputToDefaultAudioDevice();
    }

    /// <summary>
    /// Speaks a message, cutting off whatever was being said. Queueing would make the
    /// app talk over itself during rapid arrow-key navigation.
    /// </summary>
    public void Speak(string message)
    {
        _synthesizer.SpeakAsyncCancelAll();
        _synthesizer.SpeakAsync(message);
    }

    public void Dispose()
    {
        try
        {
            _synthesizer.SpeakAsyncCancelAll();
            _synthesizer.Dispose();
        }
        catch (Exception)
        {
            // Disposing a synthesizer whose audio device vanished can throw; ignore it.
        }
    }
}
