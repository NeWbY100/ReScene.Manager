namespace ReScene.NET.Services;

public class SRSEntryInfo
{
    public string SRSFileName { get; set; } = string.Empty;
    public string SampleFileName { get; set; } = string.Empty;
    public ulong SampleSize
    {
        get; set;
    }
    public uint ExpectedCRC
    {
        get; set;
    }
}
