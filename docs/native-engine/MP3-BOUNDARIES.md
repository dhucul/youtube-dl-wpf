# Preserving copied MP3 boundary samples

Native engine 0.88 corrects a metadata/remux issue found in live Audioboom playback. A public MP3 without initial skip-sample metadata was copied with all encoded audio packets intact, but ffmpeg's new Xing header introduced an encoder-delay trim of 529 samples. The original remained unchanged; the processed decoded PCM hash differed.

Before copying a local MP3 into another MP3, the processor now inspects exactly its first audio packet with a fixed local MP3 demuxer. If that packet has no skip-sample metadata, the output disables creation of a new Xing header. Existing gapless metadata uses the normal copy behavior. This applies to remux, metadata/cover embedding and copied-audio extraction. It does not change newly encoded audio or other container transfers.

The probe is bounded, uses local file/pipe protocols, has a 30-second limit and drains/kills its child process on timeout or cancellation. Empty or failed inspection cannot silently produce processed audio.

Four generated-media checks compare complete decoded PCM hashes for remux, copied audio and metadata embedding without a source Xing header, plus embedding with existing gapless metadata. They also decode both files fully and verify source bytes remain unchanged. The Audioboom generated workflow exercises the same behavior through the ordinary native engine.

Live source packet hashes, first-packet side data and a separate ffmpeg probe confirmed the original issue and the remedy. Fresh native-engine downloads verify processed audio retains all 2,181 encoded payloads in order and exactly the original decoded PCM samples. Broader MP3-in-other-container and uncommon boundary metadata layouts remain separate coverage work.
