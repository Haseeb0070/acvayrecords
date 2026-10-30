namespace RecordKeeper.Core.Models;

/// <summary>
/// The acvaysoft brand, in one place.
///
/// Brand colours are deliberately fixed rather than theme resources: a logo that
/// changes colour with the theme stops being a logo. The one exception is Windows
/// high contrast, where the startup screen defers to the system palette, because a
/// user who has asked for high contrast needs it everywhere.
/// </summary>
public static class Brand
{
    /// <summary>The company name, always lower case.</summary>
    public const string Company = "acvaysoft";

    /// <summary>The two halves of the wordmark, set in different weights.</summary>
    public const string CompanyStrong = "acvay";
    public const string CompanyLight = "soft";

    public const string Product = "acvayrecords";

    public const string FullName = "acvayrecords";

    public const string Version = "1.0";

    /// <summary>The developer's credit, shown on the startup screen, in About and on reports.</summary>
    public const string PoweredBy = "Powered by Haseeb";

    /// <summary>Deep blue. The logo tile, and the strong half of the wordmark.</summary>
    public const string DeepBlue = "#00507A";

    /// <summary>Mid blue. The light half of the wordmark.</summary>
    public const string MidBlue = "#00648F";

    /// <summary>Brand cyan. A fill only; it cannot carry text.</summary>
    public const string Cyan = "#00A9E0";

    public const string OnDark = "#FFFFFF";

    /// <summary>The line printed under the wordmark on a report.</summary>
    public const string PdfFooterNote = "Produced by acvayrecords  ·  Powered by Haseeb";

    /// <summary>What a screen reader is told when the startup screen appears.</summary>
    public const string StartupAnnouncement = "acvayrecords is starting.";
}
