using ReScene.NET.ViewModels.Reconstruction;
using ReScene.SRR;

namespace ReScene.NET.Tests;

public class SRRImportParserArchiveSetTests
{
    [Fact]
    public void Parse_MultiSetSRR_ExposesArchiveSets()
    {
        string srrPath = Path.Combine(AppContext.BaseDirectory, "TestData",
            "cleanup_script",
            "007.A.View.To.A.Kill.1985.UE.iNTERNAL.DVDRip.XviD-iNCiTE.fine_2cd.srr");

        Assert.True(File.Exists(srrPath), $"Fixture not found: {srrPath}");

        SRRFile srr = SRRFile.Load(srrPath);
        ImportedSRRInfo info = SRRImportParser.Parse(srr, srrPath);

        Assert.Equal(2, info.ArchiveSets.Count);
    }
}
