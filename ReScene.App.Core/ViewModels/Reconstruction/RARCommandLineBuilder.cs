using ReScene.Core;
using ReScene.Core.Diagnostics;

namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// Builds the RAR version ranges and the brute-force command-line argument matrix from a
/// <see cref="RARSwitchSettings"/> snapshot. Pure: no WPF binding, no I/O — output matches the
/// view-model's previous inline computation exactly.
/// </summary>
internal static class RARCommandLineBuilder
{
    private const long DefaultVolumeSizeKb = 15000;

    /// <summary>The highest thread count any WinRAR version accepts for <c>-mt</c>.</summary>
    internal const int MaxThreadCount = 64;

    /// <summary>
    /// The cardinality cap for the cartesian-product matrix. Each combination spawns a WinRAR
    /// process, so a matrix this large (hundreds of thousands of runs) is virtually always an
    /// extreme/mistaken switch combination or <c>-mt</c> range rather than an intentional run.
    /// </summary>
    internal const long MaxMatrixCardinality = 100_000;

    /// <summary>Builds the enabled RAR version ranges, in the same order the UI lists them.</summary>
    public static List<VersionRange> BuildVersionRanges(RARSwitchSettings s)
    {
        // A completed folder scan makes the per-version selection authoritative: one tight range
        // per chosen version. Before any scan (beginner wizard / pre-folder editing) fall back to
        // the broad major-version ranges so behaviour matches the pre-tree UI.
        if (s.HasScannedVersions)
        {
            return [.. s.SelectedRARVersions
                .Distinct()
                .OrderBy(v => v)
                .Select(v => new VersionRange(v, v + 1))];
        }

        List<VersionRange> rarVersions = [];
        if (s.Version2)
        {
            rarVersions.Add(new(200, 300));
        }

        if (s.Version3)
        {
            rarVersions.Add(new(300, 400));
        }

        if (s.Version4)
        {
            rarVersions.Add(new(400, 500));
        }

        if (s.Version5)
        {
            rarVersions.Add(new(500, 600));
        }

        if (s.Version6)
        {
            rarVersions.Add(new(600, 700));
        }

        if (s.Version7)
        {
            rarVersions.Add(new(700, 800));
        }

        return rarVersions;
    }

    /// <summary>
    /// Builds the cartesian-product matrix of RAR argument sets to brute-force. Synchronous but
    /// cancellable — callers with UI-thread concerns should invoke it via
    /// <c>Task.Run(() =&gt; BuildCommandLineArguments(s, ct), ct)</c>.
    /// </summary>
    /// <exception cref="RARCommandLineMatrixTooLargeException">
    /// The cartesian product would exceed <see cref="MaxMatrixCardinality"/> combinations — thrown
    /// before the result list is allocated.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public static IReadOnlyList<RARCommandLineArgument[]> BuildCommandLineArguments(RARSwitchSettings s, CancellationToken ct)
    {
        List<RARCommandLineArgument> compressionLevels = [];
        if (s.SwitchM0)
        {
            compressionLevels.Add(new("-m0", 200));
        }

        if (s.SwitchM1)
        {
            compressionLevels.Add(new("-m1", 200));
        }

        if (s.SwitchM2)
        {
            compressionLevels.Add(new("-m2", 200));
        }

        if (s.SwitchM3)
        {
            compressionLevels.Add(new("-m3", 200));
        }

        if (s.SwitchM4)
        {
            compressionLevels.Add(new("-m4", 200));
        }

        if (s.SwitchM5)
        {
            compressionLevels.Add(new("-m5", 200));
        }

        List<RARCommandLineArgument> archiveFormats = [];
        if (s.SwitchMA4)
        {
            archiveFormats.Add(new("-ma4", 500, 699));
        }

        if (s.SwitchMA5)
        {
            archiveFormats.Add(new("-ma5", 500, 699));
        }

        List<RARCommandLineArgument> dictSizes = [];
        if (s.SwitchMD64K)
        {
            dictSizes.Add(new("-md64k", 200, RARArchiveVersion.RAR4));
        }

        if (s.SwitchMD128K)
        {
            dictSizes.Add(new("-md128k", 200, RARArchiveVersion.RAR4 | RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD256K)
        {
            dictSizes.Add(new("-md256k", 200, RARArchiveVersion.RAR4 | RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD512K)
        {
            dictSizes.Add(new("-md512k", 200, RARArchiveVersion.RAR4 | RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD1024K)
        {
            dictSizes.Add(new("-md1024k", 200, RARArchiveVersion.RAR4 | RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD2048K)
        {
            dictSizes.Add(new("-md2048k", 200, RARArchiveVersion.RAR4 | RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD4096K)
        {
            dictSizes.Add(new("-md4096k", 200, RARArchiveVersion.RAR4 | RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD8M)
        {
            dictSizes.Add(new("-md8m", 500, RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD16M)
        {
            dictSizes.Add(new("-md16m", 500, RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD32M)
        {
            dictSizes.Add(new("-md32m", 500, RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD64M)
        {
            dictSizes.Add(new("-md64m", 500, RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD128M)
        {
            dictSizes.Add(new("-md128m", 500, RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD256M)
        {
            dictSizes.Add(new("-md256m", 500, RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD512M)
        {
            dictSizes.Add(new("-md512m", 500, RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchMD1G)
        {
            dictSizes.Add(new("-md1g", 500, RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        List<RARCommandLineArgument> mtimes = [];
        if (s.SwitchTSM0)
        {
            mtimes.Add(new("-tsm0", 320, RARArchiveVersion.RAR4 | RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchTSM1)
        {
            mtimes.Add(new("-tsm1", 320, RARArchiveVersion.RAR4 | RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchTSM2)
        {
            mtimes.Add(new("-tsm2", 320, RARArchiveVersion.RAR4));
        }

        if (s.SwitchTSM3)
        {
            mtimes.Add(new("-tsm3", 320, RARArchiveVersion.RAR4));
        }

        if (s.SwitchTSM4)
        {
            mtimes.Add(new("-tsm4", 320, RARArchiveVersion.RAR4));
        }

        List<RARCommandLineArgument> ctimes = [];
        if (s.SwitchTSC0)
        {
            ctimes.Add(new("-tsc0", 320, RARArchiveVersion.RAR4 | RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchTSC1)
        {
            ctimes.Add(new("-tsc1", 320, RARArchiveVersion.RAR4 | RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchTSC2)
        {
            ctimes.Add(new("-tsc2", 320, RARArchiveVersion.RAR4));
        }

        if (s.SwitchTSC3)
        {
            ctimes.Add(new("-tsc3", 320, RARArchiveVersion.RAR4));
        }

        if (s.SwitchTSC4)
        {
            ctimes.Add(new("-tsc4", 320, RARArchiveVersion.RAR4));
        }

        List<RARCommandLineArgument> atimes = [];
        if (s.SwitchTSA0)
        {
            atimes.Add(new("-tsa0", 320, RARArchiveVersion.RAR4 | RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchTSA1)
        {
            atimes.Add(new("-tsa1", 320, RARArchiveVersion.RAR4 | RARArchiveVersion.RAR5 | RARArchiveVersion.RAR7));
        }

        if (s.SwitchTSA2)
        {
            atimes.Add(new("-tsa2", 320, RARArchiveVersion.RAR4));
        }

        if (s.SwitchTSA3)
        {
            atimes.Add(new("-tsa3", 320, RARArchiveVersion.RAR4));
        }

        if (s.SwitchTSA4)
        {
            atimes.Add(new("-tsa4", 320, RARArchiveVersion.RAR4));
        }

        // Normalise the thread-count (-mt) range: clamp EACH endpoint to 0..MaxThreadCount (the
        // highest thread count any WinRAR version accepts) BEFORE ordering, so a reversed pair
        // (Start > End, or a typo/imported config), an out-of-range value, or int.MaxValue never
        // overflows or collapses the whole matrix to zero combinations — e.g. 100..200 clamps to
        // 64..64 (a single -mt64 row), never an empty loop. 0 is a valid, byte-significant value
        // (-mt0) and is no longer floored away.
        int mtLo = 0;
        int mtHi = 0;
        if (s.SwitchMT)
        {
            int clampedStart = Math.Clamp(s.SwitchMTStart, 0, MaxThreadCount);
            int clampedEnd = Math.Clamp(s.SwitchMTEnd, 0, MaxThreadCount);
            mtLo = Math.Min(clampedStart, clampedEnd);
            mtHi = Math.Max(clampedStart, clampedEnd);
        }

        // Cardinality is computed with checked arithmetic (defensive against overflow if these
        // dimensions grow) and rejected via a typed exception BEFORE the result list is allocated.
        long cardinality = checked(
            (long)Math.Max(compressionLevels.Count, 1)
            * Math.Max(archiveFormats.Count, 1)
            * Math.Max(dictSizes.Count, 1)
            * Math.Max(mtimes.Count, 1)
            * Math.Max(ctimes.Count, 1)
            * Math.Max(atimes.Count, 1)
            * (s.SwitchAI ? 2 : 1)
            * (mtHi - mtLo + 1));

        if (cardinality > MaxMatrixCardinality)
        {
            throw new RARCommandLineMatrixTooLargeException(cardinality, MaxMatrixCardinality);
        }

        List<RARCommandLineArgument[]> result = new((int)cardinality);

        for (int a = 0; a < Math.Max(compressionLevels.Count, 1); a++)
        {
            for (int b = 0; b < Math.Max(archiveFormats.Count, 1); b++)
            {
                for (int c = 0; c < Math.Max(dictSizes.Count, 1); c++)
                {
                    for (int d = 0; d < Math.Max(mtimes.Count, 1); d++)
                    {
                        for (int e = 0; e < Math.Max(ctimes.Count, 1); e++)
                        {
                            for (int f = 0; f < Math.Max(atimes.Count, 1); f++)
                            {
                                for (int x = 0; x < (s.SwitchAI ? 2 : 1); x++)
                                {
                                    for (int z = mtLo; z <= mtHi; z++)
                                    {
                                        // Checked once per generated combination so a cancel during
                                        // a large (but under-cap) matrix is honoured promptly.
                                        ct.ThrowIfCancellationRequested();

                                        List<RARCommandLineArgument> switches = [new("a", 200)];

                                        if (x == 0 && s.SwitchAI)
                                        {
                                            switches.Add(new("-ai", 390));
                                        }

                                        if (s.SwitchR)
                                        {
                                            switches.Add(new("-r", 200));
                                        }

                                        if (s.SwitchDS)
                                        {
                                            switches.Add(new("-ds", 200));
                                        }

                                        if (s.SwitchS)
                                        {
                                            switches.Add(new("-s", 200));
                                        }
                                        else if (s.SwitchSDash)
                                        {
                                            switches.Add(new("-s-", 201));
                                        }

                                        if (compressionLevels.Count > 0)
                                        {
                                            switches.Add(compressionLevels[a]);
                                        }

                                        if (archiveFormats.Count > 0)
                                        {
                                            switches.Add(archiveFormats[b]);
                                        }

                                        if (dictSizes.Count > 0)
                                        {
                                            switches.Add(dictSizes[c]);
                                        }

                                        if (mtimes.Count > 0)
                                        {
                                            switches.Add(mtimes[d]);
                                        }

                                        if (ctimes.Count > 0)
                                        {
                                            switches.Add(ctimes[e]);
                                        }

                                        if (atimes.Count > 0)
                                        {
                                            switches.Add(atimes[f]);
                                        }

                                        if (s.SwitchV)
                                        {
                                            string volumeArg = BuildVolumeArgument(s);
                                            switches.Add(new(volumeArg, 200));
                                            if (s.UseOldVolumeNaming)
                                            {
                                                switches.Add(new("-vn", 300, 699));
                                            }
                                        }

                                        if (s.SwitchMT)
                                        {
                                            switches.Add(new($"-mt{z}", 360));
                                        }

                                        result.Add([.. switches]);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return result;
    }

    public static string BuildVolumeArgument(RARSwitchSettings s)
    {
        if (!long.TryParse(s.VolumeSize, out long sizeValue))
        {
            sizeValue = DefaultVolumeSizeKb;
        }

        return s.VolumeSizeUnitIndex switch
        {
            0 => $"-v{sizeValue}b",       // Bytes
            1 => $"-v{sizeValue}",         // KB (no suffix, ×1000)
            2 => $"-v{sizeValue * 1000}",  // MB → KB
            3 => $"-v{sizeValue * 1000 * 1000}", // GB → KB
            4 => $"-v{sizeValue}k",        // KiB (k suffix, ×1024)
            5 => $"-v{sizeValue * 1024}k", // MiB → KiB
            6 => $"-v{sizeValue * 1024 * 1024}k", // GiB → KiB
            _ => $"-v{sizeValue}"
        };
    }
}
