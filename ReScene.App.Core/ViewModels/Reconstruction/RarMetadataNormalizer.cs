namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// Shared normalization for RAR metadata values that can arrive in more than one encoding,
/// used by both <see cref="SRRSwitchMapper"/> (building the switch diff) and
/// <see cref="SRRImportParser"/> (building the import step's display text) so a RAR5-sourced SRR
/// maps to the same switches and log/display text as an equivalent RAR4 one.
/// </summary>
internal static class RarMetadataNormalizer
{
    /// <summary>
    /// Normalizes a raw RAR compression-method value to the <c>0..5</c> method index. RAR4
    /// archives report the method directly as <c>0..5</c>; RAR5 archives instead report it as the
    /// ASCII digit <c>'0'..'5'</c> (<c>0x30..0x35</c>). Any other value is not a valid method and
    /// yields <c>-1</c> (#11).
    /// </summary>
    public static int NormalizeCompressionMethod(int raw)
    {
        int method = raw is >= 0x30 and <= 0x35 ? raw - 0x30 : raw;
        return method is >= 0 and <= 5 ? method : -1;
    }

    /// <summary>
    /// Maps a dictionary size in KB to its <see cref="SRRSwitchMapper.DictionarySwitch"/>, covering
    /// both the small (<c>64K..4096K</c>) and large (<c>8M..1G</c>, i.e. <c>8192..1048576</c> KB)
    /// switches RAR5/RAR7 archives can use. Returns <see cref="SRRSwitchMapper.DictionarySwitch.None"/>
    /// for any size that matches no known switch (#12).
    /// </summary>
    public static SRRSwitchMapper.DictionarySwitch DictionarySwitchFor(int sizeKb) => sizeKb switch
    {
        64 => SRRSwitchMapper.DictionarySwitch.MD64K,
        128 => SRRSwitchMapper.DictionarySwitch.MD128K,
        256 => SRRSwitchMapper.DictionarySwitch.MD256K,
        512 => SRRSwitchMapper.DictionarySwitch.MD512K,
        1024 => SRRSwitchMapper.DictionarySwitch.MD1024K,
        2048 => SRRSwitchMapper.DictionarySwitch.MD2048K,
        4096 => SRRSwitchMapper.DictionarySwitch.MD4096K,
        8192 => SRRSwitchMapper.DictionarySwitch.MD8M,
        16384 => SRRSwitchMapper.DictionarySwitch.MD16M,
        32768 => SRRSwitchMapper.DictionarySwitch.MD32M,
        65536 => SRRSwitchMapper.DictionarySwitch.MD64M,
        131072 => SRRSwitchMapper.DictionarySwitch.MD128M,
        262144 => SRRSwitchMapper.DictionarySwitch.MD256M,
        524288 => SRRSwitchMapper.DictionarySwitch.MD512M,
        1048576 => SRRSwitchMapper.DictionarySwitch.MD1G,
        _ => SRRSwitchMapper.DictionarySwitch.None,
    };
}
