# Known limitations

## Sample reconstruction of certain AVI / MP4 / WMV(ASF) SRS files

The SRS sample rebuilders currently reconstruct byte-exactly only for samples whose
container layout matches this codebase's own SRS *writer*. Rebuilding **pyrescene-created**
SRS files of these three container types can fail (the rebuilt sample's CRC will not match,
so reconstruction is reported as **failed** — it never produces a silently-wrong sample).

Surfaced by the 2026-07 code audit as findings #12 / #13 / #14. These are **fail-safe** (loud
failure, not data corruption), so they are documented here rather than fixed with a rushed
byte-exact rewrite, which would risk introducing corruption. A full pyrescene-compatible
rebuild is future work.

### #12 — AVI (`ReScene.Lib/ReScene/SRS/Rebuilders/AVIContainerRebuilder.cs`)
`IndexMediaRiffChunks` enqueues every `movi` chunk from the global minimum track offset with no
per-track match-offset guard and no mid-chunk slicing, and `RebuildRiffChunks` copies the media
chunk's size rather than the SRS chunk's declared length. pyrescene's `_avi_normal_chunk_extract`
skips chunks whose end is `<= track.match_offset`, slices from `match_offset - chunk_data_start`
for the boundary chunk, and copies exactly the SRS chunk length. Affects re-muxed samples and
tracks that start mid-chunk or have interleaved chunks before their match offset.

### #13 — MP4 (`ReScene.Lib/ReScene/SRS/Rebuilders/MP4ContainerRebuilder.cs`)
On `mdat`, each track's data is written as one contiguous run in track order, ignoring the
`stco`/`stsc`/`stsz` chunk interleaving. pyrescene interleaves via `order_chunks` (sorted by chunk
offset) and reads each track's data through a per-track chunk stream built from the main file's
chunk tables. Any normal multi-track (audio+video) MP4 sample interleaves chunks in `mdat`, so a
pyrescene SRS rebuilds into a byte-scrambled `mdat`. Only single-track / synthetic MP4s round-trip.

### #14 — WMV / ASF (`ReScene.Lib/ReScene/SRS/SRSFile.cs` `ParseASF`, and the WMV rebuilder)
`ParseASF` assumes the Data Object retains only its 26-byte header before the injected SRSF/SRST
objects — matching only this codebase's own writer, which strips whole packets. pyrescene's
`wmv_create_srs` retains per-packet header bytes after the 50-byte data-object header, so loading a
pyrescene WMV SRS reads packet header bytes as a GUID + size (garbage) and never finds SRSF/SRST
(`SRSRebuilder` then throws "SRS file does not contain file data"). The parser must walk the
retained packet/payload headers (as pyrescene's `AsfReader` does), and the WMV rebuilder must
reconstruct packets from those stored headers + payload data.
