# Static HLS timeline changes

The native HLS parser now retains discontinuity sequence numbers separately from media sequence numbers. Completed MPEG-TS and compatible fragmented-MP4 presentations with timeline resets can be downloaded into individual periods, validated with ffprobe, and joined by ffmpeg without video/audio re-encoding.

## Supported boundaries

- Ordered discontinuity identities, bounded period counts and finite positive segment durations. Repeated, late and overflowing sequence declarations fail.
- Existing byte-range and AES-128 transfer behavior within each period. Cancellation and failed joins leave the published destination intact.
- Fragmented MP4 initialization is restored for each period. Each period must use one initialization declaration, and all periods must expose the same decoder-configuration hashes. Unverifiable or changed configurations fail. Input containers are probed instead of trusting filename extensions.
- Stable stream kinds, codecs, dimensions, channels, sample rates and time bases across periods. Frame rate may change; the complete frame sequence is retained. Known timed-ID3 streams are accepted alongside audio/video.
- Manifest durations drive the concat timeline. Matching separate audio/video period identities use a shared duration schedule, and a constant original audio/video start offset is restored during merging.
- Completed output is checked for the expected stream layout before publication. Whole-file provider captions can be used when the provider supplies captions already timed to the full presentation.

ffmpeg and ffprobe are required before segment transfer for a multi-period join. Mixed segment containers, initialization changes within a period, changed/unverifiable decoder configuration, changing stream layouts or audio/video clock relationships, mismatched period identities, segmented captions across resets, live timelines and broader synchronization cases fail explicitly. Output muxing may rescale the time base while preserving media timing. This is a supported static subset, not full HLS discontinuity parity.

## Verification

Parser fixtures check discontinuity/media numbering and malformed declarations. Four media checks verify exact decoded video-frame preservation, variable frame rates, retained separate-track start offsets, and failure without replacing an existing output when stream dimensions change. The generated output is fully decoded.

Six additional fragmented-MP4 checks cover combined H.264/AAC, AAC-only output, byte ranges, separate video/audio with a 200 ms offset and retained components, changed configuration, and missing configuration. Successful combined outputs preserve every decoded video frame and every encoded audio packet from the generated periods. A preflight fixture verifies that unsupported initialization layouts fetch no resources and leave the existing destination untouched. The fragmented timeline cases are generated-media verification; a live fragmented-MP4 discontinuity sample has not yet been established.

The current public TED presentation in `TED.md` provided a live two-period example with separate audio, a 23.976-fps introduction and 29.97-fps main video. Its complete native output retained all 5,156 video frames and fully decoded. Full English/French caption documents preserved every saved cue's text and timing when embedded.

Protocol reference: [RFC 8216 discontinuity sequences](https://www.rfc-editor.org/rfc/rfc8216#section-4.3.3.3). The parser and download coordination are original C#; ffmpeg remains the agreed media-processing dependency.
