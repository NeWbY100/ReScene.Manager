namespace ReScene.App.Core.ViewModels;

/// <summary>
/// Identifies the kind of node in the file comparison tree view.
/// </summary>
public enum CompareNodeType
{
    /// <summary>
    /// Root node of the comparison tree.
    /// </summary>
    Root,

    /// <summary>
    /// Archive-level information node.
    /// </summary>
    ArchiveInfo,

    /// <summary>
    /// Container node for RAR volume entries.
    /// </summary>
    RARVolumes,

    /// <summary>
    /// Individual RAR volume entry.
    /// </summary>
    RARVolume,

    /// <summary>
    /// Container node for stored file entries.
    /// </summary>
    StoredFiles,

    /// <summary>
    /// Individual stored file entry.
    /// </summary>
    StoredFile,

    /// <summary>
    /// Container node for archived file entries.
    /// </summary>
    ArchivedFiles,

    /// <summary>
    /// Individual archived file entry.
    /// </summary>
    ArchivedFile,

    /// <summary>
    /// Container node for OSO hash entries.
    /// </summary>
    OSOHashes,

    /// <summary>
    /// Individual OSO hash entry.
    /// </summary>
    OSOHash,

    /// <summary>
    /// Detailed RAR block header entry.
    /// </summary>
    DetailedBlock,

    /// <summary>
    /// SRS file-level information node.
    /// </summary>
    SRSFileInfo,

    /// <summary>
    /// SRS track data entry.
    /// </summary>
    SRSTrack,

    /// <summary>
    /// Container node for SRS container chunk entries.
    /// </summary>
    SRSContainerChunks,

    /// <summary>
    /// Individual EBML element node in an MKV comparison tree.
    /// </summary>
    MKVElement
}
