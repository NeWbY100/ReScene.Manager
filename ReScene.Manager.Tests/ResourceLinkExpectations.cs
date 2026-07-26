namespace ReScene.Manager.Tests;

/// <summary>
/// The (accessible name, URL) pairs every surface offering the WinRAR-pack downloads must show —
/// currently the RAR Reconstructor tab header and the Beginner Reconstruct wizard's step 1. Both
/// views' tests assert against THIS list so editing one surface without the other fails its twin
/// test instead of silently diverging the identification (WCAG 3.2.4 Consistent Identification).
/// The FTP-originals archive contains only the Windows binaries, hence its qualifier.
/// </summary>
internal static class ResourceLinkExpectations
{
    public static readonly IReadOnlyList<(string Label, string Url)> WinRarPackLinks =
    [
        ("Extracted files for Windows (ready to use)", "https://drive.google.com/file/d/1of053kS2Wxk-foHN_ALRu-u6Tcck58yn/view?usp=drive_link"),
        ("Extracted files for Linux (ready to use)", "https://drive.google.com/file/d/1TcpA7RXoTUEr3pHZ8-4YTcQFRGZYP7v_/view?usp=drive_link"),
        ("Original files from RAR FTP (Windows)", "https://drive.google.com/file/d/1hvgzSY6YH_ZS3cpy7bHcw2zpjiwuP_Xi/view?usp=drive_link"),
    ];
}
