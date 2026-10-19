# Native PeerTube provider

The C# provider recognizes UUID, short-ID, and numeric video links across PeerTube instances, along with supported playlist links. It reads native REST video metadata, public media files, fragmented MP4 alternatives, HLS manifests, captions, and finite chapters. Identities, resource URLs, response sizes, format counts, and timelines are validated. Missing optional endpoints on older instances do not discard media metadata.

Playlist enumeration uses bounded offsets/counts, preserves unavailable entries and original positions, and rejects changed totals, duplicate positions, exhausted responses, and silent truncation. Playlist clips with start/stop ranges currently fail explicitly instead of becoming full-video downloads. Accounts, channels, watch-link playlist context, range downloading, password/OAuth workflows, older instance variations, and full upstream parity remain pending.

On October 7, 2026, native inspection of Framatube's public `73556243-7a9b-496c-a740-f80e42ee0ad9` (`ff2EVqgHrQX5WJyJJc7Uax`) found seven formats and one exposed automatic caption track. Both the direct fragmented MP4 and HLS paths downloaded complete 819,934-byte output, which fully decoded as H.264/AAC at 1280×888 and 9.237333 seconds. The automatic VTT caption document was saved as well. One successful instance/clip does not establish compatibility with every instance or workflow.

Protocol/field references: [PeerTube REST quick start](https://docs.joinpeertube.org/api/rest-getting-started) and the [official API schema](https://github.com/Chocobozzz/PeerTube/blob/develop/support/doc/api/openapi.yaml). The runtime uses local C# parsing and HTTP transfer.
