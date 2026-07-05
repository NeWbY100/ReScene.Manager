using ReScene.SRR;

namespace ReScene.NET.Services;

/// <summary>
/// Default <see cref="ISRRVerifyService"/> implementation that runs
/// <see cref="SRRVerifier.Verify"/> on a thread-pool thread.
/// </summary>
public class SRRVerifyService : ISRRVerifyService
{
    public Task<SRRVerifyResult> VerifyAsync(string srrFilePath, CancellationToken ct = default)
        => Task.Run(() => SRRVerifier.Verify(srrFilePath), ct);
}
