# Native replacement coverage backlog

App 1.1.0 / native 0.220.0 delivers the user-approved smaller release: a self-contained Windows app with the built-in C# backend, YouTube video/audio workflow, Facebook public HD/SD/audio and common player-page support, including formats used by adult sites. Production startup no longer selects yt-dlp. All 3,508 core checks, 13 new media cases, 192 app checks and 98 affected YouTube checks pass; nine additional distinct affected media give 3,530 current native checks. The build and self-contained publish have no warnings/errors. Five final live app preview/download/archive workflows and thirteen retained generated workflows independently verify 42 fully decoded files and 65 comparisons. The package launches with a fresh isolated profile and Python/dotnet absent from PATH. Individual adult sites and every YouTube quality profile are not claimed verified. Full catalog/option parity and advanced HDR are preserved as deferred reference work, not first-release blockers. See [practical native release](PRACTICAL-RELEASE.md).

Use the practical native build and prioritize concrete failing YouTube/Facebook/adult video URLs or quality choices. Preserve all existing work. Do not resume unrelated provider-family, exhaustive option or advanced HDR batches unless the user explicitly expands the release scope. Common adult-player support is broad but individual domains remain unverified. Real-account verification remains user-deferred; do not inspect saved accounts or ask again. Historical frozen catalog coverage remains deferred, with no universal parity claim.

Audited October 12, 2026. This is the execution backlog for the original full replacement scope. Every captured key and option has a row; partial evidence is not complete parity.

## Current audited scope

- extractorKeys: 1731
- implementedSubsetKeys: 172
- noKeySpecificEvidenceKeys: 1424
- brokenDecisionPendingKeys: 135
- upstreamBrokenKeys: 136
- registeredProviders: 82
- appOptions: 126
- explicitlyRefusedSettings: 3
- providerScopedAuthenticationSettings: 5
- providerScopedNetworkSettings: 1
- capturedLongOptions: 276
- typedLongOptionHandlers: 141
- completeParityClaims: 0

## Dependency-ordered batches

### P0-01 � Native argument and analysis control plane

Status: in_progress. Dependencies: .

Close shared typed argument mappings and analysis filtering gaps across existing providers; validate before discovery; produce an authoritative option audit.

- Finish arity/short-alias/default grammar audit for all captured options
- Preserve ordered saved/job/argument precedence and explicit rejection during analysis
- Separate media overwrite, related-file overwrite, continuation and source retention policies
- Complete output/format/audio grammar, including quality 10 and multi-format rules
- Verify currently untraced app-managed settings and settings migration

Acceptance: Grouped argument/preflight tests, generated media and live adapter workflows; one final build/regression run.

### P0-02 � Authentication and session lifecycle

Status: in_progress. Dependencies: P0-01.

Provider-declared login/netrc/password/2FA/TV access, cookie source boundaries, expiry/refresh and redirect-safe credential scope.

- Define provider-declared credential/login capabilities and per-job session ownership
- Implement and verify video-password and username/password flows for supported providers
- Add read-only netrc selection with precise host/machine matching and credential privacy
- Handle two-factor and TV-provider access without assuming one universal login protocol
- Audit regional verification/cookie redirects and expired-session refresh
- Record permitted live account evidence separately from deterministic fixtures

Acceptance: Real permitted account workflows and failure/cleanup/credential privacy evidence; account evidence tracked explicitly.

### P0-03 � Live and adaptive transport completion

Status: in_progress. Dependencies: P0-01.

Bounded/indefinite live HLS/DASH lifecycle, media-window refresh, discontinuities, missing-fragment semantics and source changes.

- Distinguish static playlists, ended events, live windows and live-from-start scope
- Refresh HLS/DASH windows and persist stable segment identities and clocks
- Handle discontinuities, changed initialization, source switches and end-of-event
- Complete missing-fragment/retry semantics with explicit incomplete-output status
- Verify cancellation, duration limits, streaming size limits and prior output restoration
- Decide external downloader settings in the independent native runtime contract

Acceptance: Complete recording-window verification and explicit partial/failure status with prior-data protection.

### P0-04 � YouTube/Vimeo core account and playback matrix

Status: in_progress. Dependencies: P0-02, P0-03.

Close session refresh, private/account, collection/search/live and format/role gaps in core platforms.

- YouTube long-form session/authorization refresh and browser/network scope
- YouTube private/account/search/collection routes and live recording workflows
- Vimeo password/account/unlisted/signed-resource refresh and clear/protected source boundaries
- Per-key formats, roles, subtitles, chapters, archives and option interactions
- Require complete-duration live playback evidence before marking workflows passed

Acceptance: Per-key/per-workflow fixture and live evidence, including access conditions.

### P1-01 � Shared hosting and consumer delegation

Status: in_progress. Dependencies: P0-01.

Batch Brightcove/JW/Kaltura/Wistia embedding, playlists, access and refresh, then map consumer extractor keys with evidence.

- Expand reusable hosting identity/playlists/token/refresh contracts
- Cover consumer embed parameters, access scopes and selected-item refresh
- Assign exact consumer keys only after fixture and live URL evidence
- Batch related consumer families around validated shared hosts

Acceptance: Native platform and consumer URL workflows verified; generic delegation is not assumed site parity.

### P1-02 � Collections, feeds, metadata and post-processing

Status: pending. Dependencies: P0-01.

Complete reusable pagination/snapshots, option grammars, captions/chapters/thumbnails, fixup/postprocessor/SponsorBlock/exec contracts.

- Complete collection pagination, inspected snapshot persistence and unavailable entries
- Close metadata/output template/filter/format grammar and stages
- Complete caption, chapter, thumbnail and related-resource format interactions
- Implement fixup/postprocessor/exec/SponsorBlock responsibilities with explicit preflight
- Verify transactional output publication and original retention across processing failures

Acceptance: Complete inventories and selected media; precedence/rollback and format/container verification.

### P1-03 � Catalog provider families

Status: pending. Dependencies: P0-01, P1-01.

Implement uncaptured and partial key families in dependency-based groups with shared discovery/transport, not one provider per checkpoint.

- Rank exact remaining families by shared platform/transport reuse and route count
- Specify routes/workflows before mapping aliases or claiming evidence
- Implement grouped native adapters with fixtures and real selected playback
- Close remaining workflows for existing partial providers as well as new keys

Acceptance: Exact extractor rows updated with fixture/live/workflow evidence.

### P1-04 � Known-broken key decisions

Status: pending. Dependencies: .

Audit all 136 frozen broken keys: current public behavior, native implementation opportunity and justified unresolved/scope decisions.

- Retain all 136 frozen broken keys in the inventory
- Verify current public availability and whether an independent native implementation is possible
- Record unavailable/account/retired cases and explicit scope decisions
- Do not infer completion from upstream broken status

Acceptance: No broken key silently removed or counted as implemented.

### P0-05 � WPF cutover and standalone release

Status: blocked_by_coverage. Dependencies: P0-02, P0-03, P0-04, P1-01, P1-02, P1-03, P1-04.

Native startup and dependency removal, packaging and clean-machine app verification after required compatibility gates.

- Verify all required key/option compatibility gates
- Switch production startup and queue/history/settings behavior to native
- Remove yt-dlp installer/update/help/execution and fallback paths
- Package ffmpeg and optional managed/browser dependencies under the agreed contract
- Verify a clean packaged application and preserved user settings/history/media

Acceptance: Packaged WPF passes agreed coverage without Python/yt-dlp and preserves settings/history/media.

## Full extractor-key backlog

| Exact frozen key | Family | Status | Native evidence | Batch |
| --- | --- | --- | --- | --- |
| 10play | 10play | no_key_specific_evidence |  | P1-03 |
| 10play:season | 10play | no_key_specific_evidence |  | P1-03 |
| 17live | 17live | no_key_specific_evidence |  | P0-03 |
| 17live:clip | 17live | no_key_specific_evidence |  | P0-03 |
| 17live:vod | 17live | no_key_specific_evidence |  | P0-03 |
| 1News | 1news | no_key_specific_evidence |  | P1-03 |
| 1tv | 1tv | no_key_specific_evidence |  | P1-03 |
| 1tv:live | 1tv | no_key_specific_evidence |  | P0-03 |
| 20min | 20min | broken_decision_pending |  | P1-04 |
| 23video | 23video | no_key_specific_evidence |  | P1-03 |
| 247sports | 247sports | broken_decision_pending |  | P1-04 |
| 24tv.ua | 24tv.ua | no_key_specific_evidence |  | P1-03 |
| 3qsdn | 3qsdn | no_key_specific_evidence |  | P1-03 |
| 3sat | 3sat | no_key_specific_evidence |  | P1-03 |
| 4tube | 4tube | no_key_specific_evidence |  | P1-03 |
| 56.com | 56.com | no_key_specific_evidence |  | P1-03 |
| 7plus | 7plus | no_key_specific_evidence |  | P1-03 |
| 9c9media | 9c9media | no_key_specific_evidence |  | P1-03 |
| 9gag | 9gag | no_key_specific_evidence |  | P1-03 |
| 9News | 9news | no_key_specific_evidence |  | P1-03 |
| 9now.com.au | 9now.com.au | no_key_specific_evidence |  | P1-03 |
| abc.net.au | abc.net.au | no_key_specific_evidence |  | P1-03 |
| abc.net.au:iview | abc.net.au | no_key_specific_evidence |  | P1-03 |
| abc.net.au:​iview:showseries | abc.net.au | no_key_specific_evidence |  | P1-03 |
| abcnews | abcnews | no_key_specific_evidence |  | P1-03 |
| abcnews:video | abcnews | no_key_specific_evidence |  | P1-03 |
| abcotvs | abcotvs | no_key_specific_evidence |  | P1-03 |
| abcotvs:clips | abcotvs | no_key_specific_evidence |  | P1-03 |
| AbemaTV | abematv | no_key_specific_evidence |  | P1-03 |
| AbemaTVTitle | abematvtitle | no_key_specific_evidence |  | P1-03 |
| AcademicEarth:Course | academicearth | no_key_specific_evidence |  | P1-03 |
| acast | acast | implemented_subset | ACAST.md | P1-03 |
| acast:channel | acast | implemented_subset | ACAST.md | P1-03 |
| AcFunBangumi | acfunbangumi | no_key_specific_evidence |  | P1-03 |
| AcFunVideo | acfunvideo | no_key_specific_evidence |  | P1-03 |
| ADN | adn | no_key_specific_evidence |  | P1-03 |
| ADNSeason | adnseason | no_key_specific_evidence |  | P1-03 |
| AdobeConnect | adobeconnect | broken_decision_pending |  | P1-04 |
| adobetv | adobetv | no_key_specific_evidence |  | P1-03 |
| AdultSwim | adultswim | no_key_specific_evidence |  | P1-03 |
| aenetworks | aenetworks | no_key_specific_evidence |  | P1-03 |
| aenetworks:collection | aenetworks | no_key_specific_evidence |  | P1-03 |
| aenetworks:show | aenetworks | no_key_specific_evidence |  | P1-03 |
| AeonCo | aeonco | no_key_specific_evidence |  | P1-03 |
| agalega:videos | agalega | no_key_specific_evidence |  | P1-03 |
| AitubeKZVideo | aitubekzvideo | no_key_specific_evidence |  | P1-03 |
| Alibaba | alibaba | no_key_specific_evidence |  | P1-03 |
| AliExpressLive | aliexpresslive | no_key_specific_evidence |  | P0-03 |
| AlJazeera | aljazeera | implemented_subset | CONSUMER-DASH.md | P1-01 |
| Allocine | allocine | no_key_specific_evidence |  | P1-03 |
| Allstar | allstar | no_key_specific_evidence |  | P1-03 |
| AllstarProfile | allstarprofile | no_key_specific_evidence |  | P1-03 |
| AlphaPorno | alphaporno | no_key_specific_evidence |  | P1-03 |
| altcensored | altcensored | no_key_specific_evidence |  | P1-03 |
| altcensored:channel | altcensored | no_key_specific_evidence |  | P1-03 |
| Alura | alura | no_key_specific_evidence |  | P1-03 |
| AluraCourse | aluracourse | no_key_specific_evidence |  | P1-03 |
| AmadeusTV | amadeustv | no_key_specific_evidence |  | P1-03 |
| Amara | amara | no_key_specific_evidence |  | P1-03 |
| AmazonMiniTV | amazonminitv | no_key_specific_evidence |  | P1-03 |
| amazonminitv:season | amazonminitv | no_key_specific_evidence |  | P1-03 |
| amazonminitv:series | amazonminitv | no_key_specific_evidence |  | P1-03 |
| AmazonReviews | amazonreviews | no_key_specific_evidence |  | P1-03 |
| AmazonStore | amazonstore | no_key_specific_evidence |  | P1-03 |
| AMCNetworks | amcnetworks | no_key_specific_evidence |  | P1-03 |
| AmericasTestKitchen | americastestkitchen | no_key_specific_evidence |  | P1-03 |
| AmericasTestKitchenSeason | americastestkitchenseason | no_key_specific_evidence |  | P1-03 |
| AmHistoryChannel | amhistorychannel | no_key_specific_evidence |  | P1-03 |
| anderetijden | anderetijden | no_key_specific_evidence |  | P1-03 |
| Angel | angel | no_key_specific_evidence |  | P1-03 |
| AnimalPlanet | animalplanet | no_key_specific_evidence |  | P1-03 |
| ant1newsgr:article | ant1newsgr | no_key_specific_evidence |  | P1-03 |
| ant1newsgr:embed | ant1newsgr | no_key_specific_evidence |  | P1-03 |
| antenna:watch | antenna | no_key_specific_evidence |  | P1-03 |
| Anvato | anvato | no_key_specific_evidence |  | P1-03 |
| aol.com | aol.com | broken_decision_pending |  | P1-04 |
| APA | apa | no_key_specific_evidence |  | P1-03 |
| Aparat | aparat | no_key_specific_evidence |  | P1-03 |
| apple:​music:connect | apple | no_key_specific_evidence |  | P1-03 |
| apple:podcasts | apple | implemented_subset | APPLE-PODCASTS.md | P1-03 |
| archive.org | archive.org | implemented_subset | INTERNET-ARCHIVE.md | P1-03 |
| ArcPublishing | arcpublishing | no_key_specific_evidence |  | P1-03 |
| ARDAudiothek | ardaudiothek | implemented_subset | AUDIO-CATALOG.md | P1-03 |
| ARDAudiothekPlaylist | ardaudiothekplaylist | implemented_subset | AUDIO-CATALOG.md | P1-03 |
| ARDMediathek | ardmediathek | implemented_subset | PUBLIC-BROADCAST.md | P1-03 |
| ARDMediathekCollection | ardmediathekcollection | implemented_subset | PUBLIC-BROADCAST.md | P1-03 |
| Art19 | art19 | no_key_specific_evidence |  | P1-03 |
| Art19Show | art19show | no_key_specific_evidence |  | P1-03 |
| arte.sky.it | arte.sky.it | no_key_specific_evidence |  | P1-03 |
| ArteTV | artetv | implemented_subset | PROVIDER-FAMILIES.md | P1-03 |
| ArteTVCategory | artetvcategory | no_key_specific_evidence |  | P1-03 |
| ArteTVEmbed | artetvembed | implemented_subset | PROVIDER-FAMILIES.md | P1-03 |
| ArteTVPlaylist | artetvplaylist | implemented_subset | PROVIDER-FAMILIES.md | P1-03 |
| asobichannel | asobichannel | no_key_specific_evidence |  | P1-03 |
| asobichannel:tag | asobichannel | no_key_specific_evidence |  | P1-03 |
| AsobiStage | asobistage | no_key_specific_evidence |  | P1-03 |
| AtresPlayer | atresplayer | no_key_specific_evidence |  | P1-03 |
| AtScaleConfEvent | atscaleconfevent | no_key_specific_evidence |  | P1-03 |
| AudiMedia | audimedia | no_key_specific_evidence |  | P1-03 |
| AudioBoom | audioboom | implemented_subset | AUDIOBOOM.md | P1-03 |
| Audiodraft:custom | audiodraft | no_key_specific_evidence |  | P1-03 |
| Audiodraft:generic | audiodraft | no_key_specific_evidence |  | P1-03 |
| audiomack | audiomack | implemented_subset | AUDIOMACK.md | P1-03 |
| audiomack:album | audiomack | implemented_subset | AUDIOMACK.md | P1-03 |
| Audius | audius | implemented_subset | AUDIUS.md | P1-03 |
| audius:artist | audius | implemented_subset | AUDIUS.md | P1-03 |
| audius:playlist | audius | implemented_subset | AUDIUS.md | P1-03 |
| audius:track | audius | implemented_subset | AUDIUS.md | P1-03 |
| AZMedien | azmedien | no_key_specific_evidence |  | P1-03 |
| BaiduVideo | baiduvideo | no_key_specific_evidence |  | P1-03 |
| BanBye | banbye | no_key_specific_evidence |  | P1-03 |
| BanByeChannel | banbyechannel | no_key_specific_evidence |  | P1-03 |
| Bandcamp | bandcamp | implemented_subset | BANDCAMP.md | P1-03 |
| Bandcamp:album | bandcamp | implemented_subset | BANDCAMP.md | P1-03 |
| Bandcamp:user | bandcamp | no_key_specific_evidence |  | P1-03 |
| Bandcamp:weekly | bandcamp | no_key_specific_evidence |  | P1-03 |
| Bandlab | bandlab | implemented_subset | BANDLAB.md | P1-03 |
| BandlabPlaylist | bandlabplaylist | implemented_subset | BANDLAB.md | P1-03 |
| BannedVideo | bannedvideo | no_key_specific_evidence |  | P1-03 |
| bbc | bbc | implemented_subset | GLOBAL-BROADCAST.md | P1-03 |
| bbc.co.uk | bbc.co.uk | no_key_specific_evidence |  | P1-03 |
| bbc.co.uk:article | bbc.co.uk | no_key_specific_evidence |  | P1-03 |
| bbc.co.uk:​iplayer:episodes | bbc.co.uk | no_key_specific_evidence |  | P1-03 |
| bbc.co.uk:​iplayer:group | bbc.co.uk | no_key_specific_evidence |  | P1-03 |
| bbc.co.uk:playlist | bbc.co.uk | no_key_specific_evidence |  | P1-03 |
| BBVTV | bbvtv | no_key_specific_evidence |  | P1-03 |
| BBVTVLive | bbvtvlive | no_key_specific_evidence |  | P0-03 |
| BBVTVRecordings | bbvtvrecordings | no_key_specific_evidence |  | P1-03 |
| BeaconTv | beacontv | no_key_specific_evidence |  | P1-03 |
| Beatport | beatport | no_key_specific_evidence |  | P1-03 |
| Beeg | beeg | no_key_specific_evidence |  | P1-03 |
| BehindKink | behindkink | broken_decision_pending |  | P1-04 |
| BerufeTV | berufetv | no_key_specific_evidence |  | P1-03 |
| Bet | bet | no_key_specific_evidence |  | P1-03 |
| bfi:player | bfi | broken_decision_pending |  | P1-04 |
| bfmtv | bfmtv | no_key_specific_evidence |  | P1-03 |
| bfmtv:article | bfmtv | no_key_specific_evidence |  | P1-03 |
| bfmtv:live | bfmtv | no_key_specific_evidence |  | P0-03 |
| bibeltv:live | bibeltv | no_key_specific_evidence |  | P0-03 |
| bibeltv:series | bibeltv | no_key_specific_evidence |  | P1-03 |
| bibeltv:video | bibeltv | no_key_specific_evidence |  | P1-03 |
| Bigo | bigo | no_key_specific_evidence |  | P1-03 |
| Bild | bild | no_key_specific_evidence |  | P1-03 |
| BiliBili | bilibili | implemented_subset | BILIBILI.mdLIVE-OUTPUT-INVENTORIES.md | P1-03 |
| Bilibili category extractor | bilibili category extractor | no_key_specific_evidence |  | P1-03 |
| BilibiliAudio | bilibiliaudio | no_key_specific_evidence |  | P1-03 |
| BilibiliAudioAlbum | bilibiliaudioalbum | no_key_specific_evidence |  | P1-03 |
| BiliBiliBangumi | bilibilibangumi | no_key_specific_evidence | LIVE-OUTPUT-INVENTORIES.md | P1-03 |
| BiliBiliBangumiMedia | bilibilibangumimedia | no_key_specific_evidence |  | P1-03 |
| BiliBiliBangumiSeason | bilibilibangumiseason | no_key_specific_evidence |  | P1-03 |
| BilibiliCheese | bilibilicheese | no_key_specific_evidence |  | P1-03 |
| BilibiliCheeseSeason | bilibilicheeseseason | no_key_specific_evidence |  | P1-03 |
| BilibiliCollectionList | bilibilicollectionlist | no_key_specific_evidence |  | P1-03 |
| BiliBiliDynamic | bilibilidynamic | no_key_specific_evidence |  | P1-03 |
| BilibiliFavoritesList | bilibilifavoriteslist | no_key_specific_evidence |  | P1-03 |
| BiliBiliPlayer | bilibiliplayer | implemented_subset | BILIBILI.md | P1-03 |
| BilibiliPlaylist | bilibiliplaylist | no_key_specific_evidence |  | P1-03 |
| BiliBiliSearch | bilibilisearch | no_key_specific_evidence |  | P1-03 |
| BilibiliSeriesList | bilibiliserieslist | no_key_specific_evidence |  | P1-03 |
| BilibiliSpaceAudio | bilibilispaceaudio | no_key_specific_evidence |  | P1-03 |
| BilibiliSpaceVideo | bilibilispacevideo | no_key_specific_evidence |  | P1-03 |
| BilibiliWatchlater | bilibiliwatchlater | no_key_specific_evidence |  | P1-03 |
| BiliIntl | biliintl | no_key_specific_evidence |  | P1-03 |
| biliIntl:series | biliintl | no_key_specific_evidence |  | P1-03 |
| BiliLive | bililive | no_key_specific_evidence |  | P0-03 |
| BioBioChileTV | biobiochiletv | no_key_specific_evidence |  | P1-03 |
| Biography | biography | no_key_specific_evidence |  | P1-03 |
| BitChute | bitchute | no_key_specific_evidence |  | P1-03 |
| BitChuteChannel | bitchutechannel | no_key_specific_evidence |  | P1-03 |
| Bitmovin | bitmovin | no_key_specific_evidence |  | P1-03 |
| BlackboardCollaborate | blackboardcollaborate | no_key_specific_evidence |  | P1-03 |
| BlackboardCollaborateLaunch | blackboardcollaboratelaunch | no_key_specific_evidence |  | P1-03 |
| BleacherReport | bleacherreport | broken_decision_pending |  | P1-04 |
| BleacherReportCMS | bleacherreportcms | broken_decision_pending |  | P1-04 |
| blerp | blerp | no_key_specific_evidence |  | P1-03 |
| blogger.com | blogger.com | no_key_specific_evidence |  | P1-03 |
| Bloomberg | bloomberg | no_key_specific_evidence |  | P1-03 |
| Bluesky | bluesky | implemented_subset | BLUESKY.md | P1-03 |
| BongaCams | bongacams | no_key_specific_evidence |  | P1-03 |
| Boosty | boosty | no_key_specific_evidence |  | P1-03 |
| BostonGlobe | bostonglobe | no_key_specific_evidence |  | P1-03 |
| Box | box | no_key_specific_evidence |  | P1-03 |
| BoxCastVideo | boxcastvideo | no_key_specific_evidence |  | P1-03 |
| Bpb | bpb | no_key_specific_evidence |  | P1-03 |
| BR | br | broken_decision_pending |  | P1-04 |
| BrainPOP | brainpop | no_key_specific_evidence |  | P1-03 |
| BrainPOPELL | brainpopell | no_key_specific_evidence |  | P1-03 |
| BrainPOPEsp | brainpopesp | no_key_specific_evidence |  | P1-03 |
| BrainPOPFr | brainpopfr | no_key_specific_evidence |  | P1-03 |
| BrainPOPIl | brainpopil | no_key_specific_evidence |  | P1-03 |
| BrainPOPJr | brainpopjr | no_key_specific_evidence |  | P1-03 |
| BravoTV | bravotv | no_key_specific_evidence |  | P1-03 |
| BreitBart | breitbart | no_key_specific_evidence |  | P1-03 |
| brightcove:legacy | brightcove | no_key_specific_evidence |  | P1-01 |
| brightcove:new | brightcove | implemented_subset | BRIGHTCOVE.mdWRITE-CLEANUP-RECOVERY.md | P1-01 |
| Brilliantpala:Classes | brilliantpala | no_key_specific_evidence |  | P1-03 |
| Brilliantpala:Elearn | brilliantpala | no_key_specific_evidence |  | P1-03 |
| bt:article | bt | no_key_specific_evidence |  | P1-03 |
| bt:vestlendingen | bt | no_key_specific_evidence |  | P1-03 |
| BTVPlus | btvplus | no_key_specific_evidence |  | P1-03 |
| Bundesliga | bundesliga | no_key_specific_evidence |  | P1-03 |
| Bundestag | bundestag | no_key_specific_evidence |  | P1-03 |
| BunnyCdn | bunnycdn | no_key_specific_evidence |  | P1-03 |
| BusinessInsider | businessinsider | implemented_subset | CONSUMER-DASH.md | P1-01 |
| BuzzFeed | buzzfeed | no_key_specific_evidence |  | P1-03 |
| BYUtv | byutv | broken_decision_pending |  | P1-04 |
| Caltrans | caltrans | no_key_specific_evidence |  | P1-03 |
| CAM4 | cam4 | no_key_specific_evidence |  | P1-03 |
| CamFMEpisode | camfmepisode | no_key_specific_evidence |  | P1-03 |
| CamFMShow | camfmshow | no_key_specific_evidence |  | P1-03 |
| CamModels | cammodels | no_key_specific_evidence |  | P1-03 |
| Camsoda | camsoda | no_key_specific_evidence |  | P1-03 |
| CamtasiaEmbed | camtasiaembed | no_key_specific_evidence |  | P1-03 |
| Canal1 | canal1 | no_key_specific_evidence |  | P1-03 |
| CanalAlpha | canalalpha | no_key_specific_evidence |  | P1-03 |
| canalc2.tv | canalc2.tv | no_key_specific_evidence |  | P1-03 |
| Canalplus | canalplus | no_key_specific_evidence |  | P1-03 |
| Canalsurmas | canalsurmas | no_key_specific_evidence |  | P1-03 |
| CaracolTvPlay | caracoltvplay | no_key_specific_evidence |  | P1-03 |
| cbc.ca | cbc.ca | no_key_specific_evidence |  | P1-03 |
| cbc.ca:listen | cbc.ca | no_key_specific_evidence |  | P1-03 |
| cbc.ca:player | cbc.ca | no_key_specific_evidence |  | P1-03 |
| cbc.ca:​player:playlist | cbc.ca | no_key_specific_evidence |  | P1-03 |
| CBS | cbs | broken_decision_pending |  | P1-04 |
| CBSLocal | cbslocal | no_key_specific_evidence |  | P1-03 |
| CBSLocalArticle | cbslocalarticle | no_key_specific_evidence |  | P1-03 |
| CBSLocalLive | cbslocallive | no_key_specific_evidence |  | P0-03 |
| cbsnews | cbsnews | no_key_specific_evidence |  | P1-03 |
| cbsnews:embed | cbsnews | no_key_specific_evidence |  | P1-03 |
| cbsnews:live | cbsnews | no_key_specific_evidence |  | P0-03 |
| cbsnews:livevideo | cbsnews | no_key_specific_evidence |  | P0-03 |
| cbssports | cbssports | broken_decision_pending |  | P1-04 |
| cbssports:embed | cbssports | broken_decision_pending |  | P1-04 |
| CCMA | ccma | no_key_specific_evidence |  | P1-03 |
| CCTV | cctv | no_key_specific_evidence |  | P1-03 |
| CDA | cda | no_key_specific_evidence |  | P1-03 |
| CDAFolder | cdafolder | no_key_specific_evidence |  | P1-03 |
| Cellebrite | cellebrite | no_key_specific_evidence |  | P1-03 |
| CeskaTelevize | ceskatelevize | no_key_specific_evidence |  | P1-03 |
| CGTN | cgtn | no_key_specific_evidence |  | P1-03 |
| CharlieRose | charlierose | no_key_specific_evidence |  | P1-03 |
| Chaturbate | chaturbate | no_key_specific_evidence |  | P1-03 |
| Chilloutzone | chilloutzone | no_key_specific_evidence |  | P1-03 |
| chzzk:live | chzzk | no_key_specific_evidence |  | P0-03 |
| chzzk:video | chzzk | no_key_specific_evidence |  | P1-03 |
| cielotv.it | cielotv.it | no_key_specific_evidence |  | P1-03 |
| Cinemax | cinemax | broken_decision_pending |  | P1-04 |
| CinetecaMilano | cinetecamilano | no_key_specific_evidence |  | P1-03 |
| Cineverse | cineverse | no_key_specific_evidence |  | P1-03 |
| CineverseDetails | cineversedetails | no_key_specific_evidence |  | P1-03 |
| CiscoLiveSearch | ciscolivesearch | no_key_specific_evidence |  | P0-03 |
| CiscoLiveSession | ciscolivesession | no_key_specific_evidence |  | P0-03 |
| ciscowebex | ciscowebex | no_key_specific_evidence |  | P1-03 |
| CJSW | cjsw | no_key_specific_evidence |  | P1-03 |
| Clipchamp | clipchamp | no_key_specific_evidence |  | P1-03 |
| ClipRs | cliprs | broken_decision_pending |  | P1-04 |
| CloserToTruth | closertotruth | broken_decision_pending |  | P1-04 |
| CloudflareStream | cloudflarestream | implemented_subset | CLOUDFLARE-STREAM.md | P0-03 |
| CloudyCDN | cloudycdn | no_key_specific_evidence |  | P1-03 |
| Clubic | clubic | broken_decision_pending |  | P1-04 |
| Clyp | clyp | implemented_subset | CLYP.md | P1-03 |
| CNBCVideo | cnbcvideo | no_key_specific_evidence |  | P1-03 |
| CNN | cnn | no_key_specific_evidence |  | P1-03 |
| CNNIndonesia | cnnindonesia | no_key_specific_evidence |  | P1-03 |
| ComedyCentral | comedycentral | no_key_specific_evidence |  | P1-03 |
| ConanClassic | conanclassic | broken_decision_pending |  | P1-04 |
| CondeNast | condenast | no_key_specific_evidence |  | P1-03 |
| CookingChannel | cookingchannel | no_key_specific_evidence |  | P1-03 |
| Corus | corus | no_key_specific_evidence |  | P1-03 |
| Coub | coub | implemented_subset | COUB.md | P1-03 |
| CozyTV | cozytv | no_key_specific_evidence |  | P1-03 |
| cp24 | cp24 | no_key_specific_evidence |  | P1-03 |
| cpac | cpac | no_key_specific_evidence |  | P1-03 |
| cpac:playlist | cpac | no_key_specific_evidence |  | P1-03 |
| Cracked | cracked | no_key_specific_evidence |  | P1-03 |
| Craftsy | craftsy | no_key_specific_evidence |  | P1-03 |
| croatian.film | croatian.film | no_key_specific_evidence |  | P1-03 |
| CrooksAndLiars | crooksandliars | no_key_specific_evidence |  | P1-03 |
| CrowdBunker | crowdbunker | no_key_specific_evidence |  | P1-03 |
| CrowdBunkerChannel | crowdbunkerchannel | no_key_specific_evidence |  | P1-03 |
| Crtvg | crtvg | no_key_specific_evidence |  | P1-03 |
| CSpan | cspan | no_key_specific_evidence |  | P1-03 |
| CSpanCongress | cspancongress | no_key_specific_evidence |  | P1-03 |
| CtsNews | ctsnews | no_key_specific_evidence |  | P1-03 |
| CTVNews | ctvnews | no_key_specific_evidence |  | P1-03 |
| cu.ntv.co.jp | cu.ntv.co.jp | no_key_specific_evidence |  | P1-03 |
| CultureUnplugged | cultureunplugged | no_key_specific_evidence |  | P1-03 |
| curiositystream | curiositystream | no_key_specific_evidence |  | P0-03 |
| curiositystream:collections | curiositystream | no_key_specific_evidence |  | P0-03 |
| curiositystream:series | curiositystream | no_key_specific_evidence |  | P0-03 |
| Cybrary | cybrary | no_key_specific_evidence |  | P1-03 |
| CybraryCourse | cybrarycourse | no_key_specific_evidence |  | P1-03 |
| DacastPlaylist | dacastplaylist | no_key_specific_evidence |  | P1-03 |
| DacastVOD | dacastvod | no_key_specific_evidence |  | P1-03 |
| DagelijkseKost | dagelijksekost | no_key_specific_evidence |  | P1-03 |
| DailyMail | dailymail | no_key_specific_evidence |  | P1-03 |
| dailymotion | dailymotion | implemented_subset | DAILYMOTION.md | P1-03 |
| dailymotion:playlist | dailymotion | implemented_subset | DAILYMOTION.md | P1-03 |
| dailymotion:search | dailymotion | no_key_specific_evidence |  | P1-03 |
| dailymotion:user | dailymotion | no_key_specific_evidence |  | P1-03 |
| DailyWire | dailywire | no_key_specific_evidence |  | P1-03 |
| DailyWirePodcast | dailywirepodcast | no_key_specific_evidence |  | P1-03 |
| damtomo:record | damtomo | no_key_specific_evidence |  | P1-03 |
| damtomo:video | damtomo | no_key_specific_evidence |  | P1-03 |
| dangalplay | dangalplay | no_key_specific_evidence |  | P1-03 |
| dangalplay:season | dangalplay | no_key_specific_evidence |  | P1-03 |
| daum.net | daum.net | no_key_specific_evidence |  | P1-03 |
| daum.net:clip | daum.net | no_key_specific_evidence |  | P1-03 |
| daum.net:playlist | daum.net | no_key_specific_evidence |  | P1-03 |
| daum.net:user | daum.net | no_key_specific_evidence |  | P1-03 |
| daystar:clip | daystar | no_key_specific_evidence |  | P1-03 |
| DBTV | dbtv | no_key_specific_evidence |  | P1-03 |
| DctpTv | dctptv | no_key_specific_evidence |  | P1-03 |
| democracynow | democracynow | no_key_specific_evidence |  | P1-03 |
| DestinationAmerica | destinationamerica | no_key_specific_evidence |  | P1-03 |
| DetikEmbed | detikembed | no_key_specific_evidence |  | P1-03 |
| DeuxM | deuxm | no_key_specific_evidence |  | P1-03 |
| DeuxMNews | deuxmnews | no_key_specific_evidence |  | P1-03 |
| DHM | dhm | broken_decision_pending |  | P1-04 |
| DigitalConcertHall | digitalconcerthall | no_key_specific_evidence |  | P1-03 |
| DigitallySpeaking | digitallyspeaking | broken_decision_pending |  | P1-04 |
| Digiteka | digiteka | no_key_specific_evidence |  | P1-03 |
| Digiview | digiview | no_key_specific_evidence |  | P1-03 |
| DiscogsReleasePlaylist | discogsreleaseplaylist | no_key_specific_evidence |  | P1-03 |
| DiscoveryLife | discoverylife | no_key_specific_evidence |  | P1-03 |
| DiscoveryNetworksDe | discoverynetworksde | no_key_specific_evidence |  | P1-03 |
| DiscoveryPlus | discoveryplus | no_key_specific_evidence |  | P1-03 |
| DiscoveryPlusIndia | discoveryplusindia | no_key_specific_evidence |  | P1-03 |
| DiscoveryPlusIndiaShow | discoveryplusindiashow | no_key_specific_evidence |  | P1-03 |
| DiscoveryPlusItaly | discoveryplusitaly | no_key_specific_evidence |  | P1-03 |
| DiscoveryPlusItalyShow | discoveryplusitalyshow | no_key_specific_evidence |  | P1-03 |
| Disney | disney | no_key_specific_evidence |  | P1-03 |
| dlf | dlf | no_key_specific_evidence |  | P1-03 |
| dlf:corpus | dlf | no_key_specific_evidence |  | P1-03 |
| dlive:stream | dlive | no_key_specific_evidence |  | P0-03 |
| dlive:vod | dlive | no_key_specific_evidence |  | P0-03 |
| Douyin | douyin | no_key_specific_evidence |  | P1-03 |
| DouyuShow | douyushow | no_key_specific_evidence |  | P1-03 |
| DouyuTV | douyutv | no_key_specific_evidence |  | P1-03 |
| DPlay | dplay | no_key_specific_evidence |  | P1-03 |
| DRBonanza | drbonanza | no_key_specific_evidence |  | P1-03 |
| Dropbox | dropbox | no_key_specific_evidence |  | P1-03 |
| Dropout | dropout | no_key_specific_evidence |  | P1-03 |
| DropoutSeason | dropoutseason | no_key_specific_evidence |  | P1-03 |
| DrTalks | drtalks | no_key_specific_evidence |  | P1-03 |
| DrTuber | drtuber | no_key_specific_evidence |  | P1-03 |
| drtv | drtv | no_key_specific_evidence |  | P1-03 |
| drtv:live | drtv | no_key_specific_evidence |  | P0-03 |
| drtv:season | drtv | no_key_specific_evidence |  | P1-03 |
| drtv:series | drtv | no_key_specific_evidence |  | P1-03 |
| DTube | dtube | broken_decision_pending |  | P1-04 |
| Dumpert | dumpert | no_key_specific_evidence |  | P1-03 |
| Duoplay | duoplay | no_key_specific_evidence |  | P1-03 |
| dvtv | dvtv | no_key_specific_evidence |  | P1-03 |
| dw | dw | broken_decision_pending |  | P1-04 |
| dw:article | dw | broken_decision_pending |  | P1-04 |
| dzen.ru | dzen.ru | no_key_specific_evidence |  | P1-03 |
| dzen.ru:channel | dzen.ru | no_key_specific_evidence |  | P1-03 |
| EbaumsWorld | ebaumsworld | no_key_specific_evidence |  | P1-03 |
| Ebay | ebay | no_key_specific_evidence |  | P1-03 |
| egghead:course | egghead | no_key_specific_evidence |  | P1-03 |
| egghead:lesson | egghead | no_key_specific_evidence |  | P1-03 |
| eggs:artist | eggs | no_key_specific_evidence |  | P1-03 |
| eggs:single | eggs | no_key_specific_evidence |  | P1-03 |
| EinsUndEinsTV | einsundeinstv | no_key_specific_evidence |  | P1-03 |
| EinsUndEinsTVLive | einsundeinstvlive | no_key_specific_evidence |  | P0-03 |
| EinsUndEinsTVRecordings | einsundeinstvrecordings | no_key_specific_evidence |  | P1-03 |
| ElementorEmbed | elementorembed | no_key_specific_evidence |  | P1-03 |
| Elonet | elonet | no_key_specific_evidence |  | P1-03 |
| ElPais | elpais | no_key_specific_evidence |  | P1-03 |
| ElTreceTV | eltrecetv | no_key_specific_evidence |  | P1-03 |
| Embedly | embedly | no_key_specific_evidence |  | P1-03 |
| EMPFlix | empflix | no_key_specific_evidence |  | P1-03 |
| Epicon | epicon | no_key_specific_evidence |  | P1-03 |
| EpiconSeries | epiconseries | no_key_specific_evidence |  | P1-03 |
| EpidemicSound | epidemicsound | no_key_specific_evidence |  | P1-03 |
| eplus | eplus | no_key_specific_evidence |  | P1-03 |
| Epoch | epoch | no_key_specific_evidence |  | P1-03 |
| Eporner | eporner | no_key_specific_evidence |  | P1-03 |
| Erocast | erocast | no_key_specific_evidence |  | P1-03 |
| EroProfile | eroprofile | no_key_specific_evidence |  | P1-03 |
| EroProfile:album | eroprofile | no_key_specific_evidence |  | P1-03 |
| ERRArhiiv | errarhiiv | no_key_specific_evidence |  | P1-03 |
| ERRJupiter | errjupiter | no_key_specific_evidence |  | P1-03 |
| ertflix | ertflix | no_key_specific_evidence |  | P1-03 |
| ertflix:codename | ertflix | no_key_specific_evidence |  | P1-03 |
| ertwebtv:embed | ertwebtv | no_key_specific_evidence |  | P1-03 |
| ESPN | espn | no_key_specific_evidence |  | P1-03 |
| ESPNArticle | espnarticle | no_key_specific_evidence |  | P1-03 |
| ESPNCricInfo | espncricinfo | no_key_specific_evidence |  | P1-03 |
| EttuTv | ettutv | no_key_specific_evidence |  | P1-03 |
| Europa | europa | broken_decision_pending |  | P1-04 |
| EuroParlWebstream | europarlwebstream | no_key_specific_evidence |  | P0-03 |
| EuropeanTour | europeantour | no_key_specific_evidence |  | P1-03 |
| Eurosport | eurosport | no_key_specific_evidence |  | P1-03 |
| EUScreen | euscreen | no_key_specific_evidence |  | P1-03 |
| EWETV | ewetv | no_key_specific_evidence |  | P1-03 |
| EWETVLive | ewetvlive | no_key_specific_evidence |  | P0-03 |
| EWETVRecordings | ewetvrecordings | no_key_specific_evidence |  | P1-03 |
| Expressen | expressen | no_key_specific_evidence |  | P1-03 |
| facebook | facebook | implemented_subset | PRACTICAL-RELEASE.md | P1-03 |
| facebook:ads | facebook | no_key_specific_evidence |  | P1-03 |
| facebook:reel | facebook | implemented_subset | PRACTICAL-RELEASE.md | P1-03 |
| FacebookPluginsVideo | facebookpluginsvideo | implemented_subset | PRACTICAL-RELEASE.md | P1-03 |
| fancode:live | fancode | broken_decision_pending |  | P1-04 |
| fancode:vod | fancode | broken_decision_pending |  | P1-04 |
| Fathom | fathom | no_key_specific_evidence |  | P1-03 |
| Faulio | faulio | no_key_specific_evidence |  | P1-03 |
| FaulioLive | fauliolive | no_key_specific_evidence |  | P0-03 |
| faz.net | faz.net | no_key_specific_evidence |  | P1-03 |
| fc2 | fc2 | no_key_specific_evidence |  | P1-03 |
| fc2:embed | fc2 | no_key_specific_evidence |  | P1-03 |
| fc2:live | fc2 | no_key_specific_evidence |  | P0-03 |
| Fczenit | fczenit | no_key_specific_evidence |  | P1-03 |
| Fifa | fifa | no_key_specific_evidence |  | P1-03 |
| FilmArchiv | filmarchiv | no_key_specific_evidence |  | P1-03 |
| filmon | filmon | no_key_specific_evidence |  | P1-03 |
| filmon:channel | filmon | no_key_specific_evidence |  | P1-03 |
| Filmweb | filmweb | no_key_specific_evidence |  | P1-03 |
| FiveThirtyEight | fivethirtyeight | no_key_specific_evidence |  | P1-03 |
| FiveTV | fivetv | no_key_specific_evidence |  | P1-03 |
| Flickr | flickr | implemented_subset | FLICKR.md | P1-03 |
| Floatplane | floatplane | no_key_specific_evidence |  | P1-03 |
| FloatplaneChannel | floatplanechannel | no_key_specific_evidence |  | P1-03 |
| Folketinget | folketinget | broken_decision_pending |  | P1-04 |
| FoodNetwork | foodnetwork | no_key_specific_evidence |  | P1-03 |
| FootyRoom | footyroom | no_key_specific_evidence |  | P1-03 |
| Formula1 | formula1 | no_key_specific_evidence |  | P1-03 |
| FOX | fox | no_key_specific_evidence |  | P1-03 |
| FOX9 | fox9 | no_key_specific_evidence |  | P1-03 |
| FOX9News | fox9news | no_key_specific_evidence |  | P1-03 |
| foxnews | foxnews | no_key_specific_evidence |  | P1-03 |
| foxnews:article | foxnews | no_key_specific_evidence |  | P1-03 |
| FoxNewsVideo | foxnewsvideo | no_key_specific_evidence |  | P1-03 |
| FoxSports | foxsports | no_key_specific_evidence |  | P1-03 |
| fptplay | fptplay | no_key_specific_evidence |  | P1-03 |
| FrancaisFacile | francaisfacile | no_key_specific_evidence |  | P1-03 |
| FranceCulture | franceculture | implemented_subset | RADIO-COMPLETION.md | P1-03 |
| franceinfo | franceinfo | no_key_specific_evidence |  | P1-03 |
| francetv | francetv | no_key_specific_evidence |  | P1-03 |
| francetv:site | francetv | no_key_specific_evidence |  | P1-03 |
| Freesound | freesound | no_key_specific_evidence |  | P1-03 |
| freespeech.org | freespeech.org | no_key_specific_evidence |  | P1-03 |
| freetv:series | freetv | no_key_specific_evidence |  | P1-03 |
| FreeTvMovies | freetvmovies | no_key_specific_evidence |  | P1-03 |
| FrontendMasters | frontendmasters | no_key_specific_evidence |  | P1-03 |
| FrontendMastersCourse | frontendmasterscourse | no_key_specific_evidence |  | P1-03 |
| FrontendMastersLesson | frontendmasterslesson | no_key_specific_evidence |  | P1-03 |
| Funk | funk | no_key_specific_evidence |  | P1-03 |
| Funker530 | funker530 | no_key_specific_evidence |  | P1-03 |
| Fux | fux | no_key_specific_evidence |  | P1-03 |
| FuyinTV | fuyintv | no_key_specific_evidence |  | P1-03 |
| Gab | gab | no_key_specific_evidence |  | P1-03 |
| Gaia | gaia | no_key_specific_evidence |  | P1-03 |
| GameDevTVDashboard | gamedevtvdashboard | no_key_specific_evidence |  | P1-03 |
| GameJolt | gamejolt | no_key_specific_evidence |  | P1-03 |
| GameJoltCommunity | gamejoltcommunity | no_key_specific_evidence |  | P1-03 |
| GameJoltGame | gamejoltgame | no_key_specific_evidence |  | P1-03 |
| GameJoltGameSoundtrack | gamejoltgamesoundtrack | no_key_specific_evidence |  | P1-03 |
| GameJoltSearch | gamejoltsearch | no_key_specific_evidence |  | P1-03 |
| GameJoltUser | gamejoltuser | no_key_specific_evidence |  | P1-03 |
| GameSpot | gamespot | no_key_specific_evidence |  | P1-03 |
| GameStar | gamestar | no_key_specific_evidence |  | P1-03 |
| Gaskrank | gaskrank | no_key_specific_evidence |  | P1-03 |
| Gazeta | gazeta | broken_decision_pending |  | P1-04 |
| GBNews | gbnews | no_key_specific_evidence |  | P1-03 |
| GDCVault | gdcvault | broken_decision_pending |  | P1-04 |
| GediDigital | gedidigital | no_key_specific_evidence |  | P1-03 |
| gem.cbc.ca | gem.cbc.ca | no_key_specific_evidence |  | P1-03 |
| gem.cbc.ca:live | gem.cbc.ca | no_key_specific_evidence |  | P0-03 |
| gem.cbc.ca:olympics | gem.cbc.ca | no_key_specific_evidence |  | P1-03 |
| gem.cbc.ca:playlist | gem.cbc.ca | no_key_specific_evidence |  | P1-03 |
| Genius | genius | no_key_specific_evidence |  | P1-03 |
| GeniusLyrics | geniuslyrics | no_key_specific_evidence |  | P1-03 |
| Germanupa | germanupa | no_key_specific_evidence |  | P1-03 |
| GetCourseRu | getcourseru | no_key_specific_evidence |  | P1-03 |
| GetCourseRuPlayer | getcourseruplayer | no_key_specific_evidence |  | P1-03 |
| Gettr | gettr | no_key_specific_evidence |  | P1-03 |
| GettrStreaming | gettrstreaming | no_key_specific_evidence |  | P0-03 |
| GiantBomb | giantbomb | no_key_specific_evidence |  | P1-03 |
| GlattvisionTV | glattvisiontv | no_key_specific_evidence |  | P1-03 |
| GlattvisionTVLive | glattvisiontvlive | no_key_specific_evidence |  | P0-03 |
| GlattvisionTVRecordings | glattvisiontvrecordings | no_key_specific_evidence |  | P1-03 |
| Glide | glide | no_key_specific_evidence |  | P1-03 |
| GlobalPlayerAudio | globalplayeraudio | no_key_specific_evidence |  | P1-03 |
| GlobalPlayerAudioEpisode | globalplayeraudioepisode | no_key_specific_evidence |  | P1-03 |
| GlobalPlayerLive | globalplayerlive | no_key_specific_evidence |  | P0-03 |
| GlobalPlayerLivePlaylist | globalplayerliveplaylist | no_key_specific_evidence |  | P0-03 |
| GlobalPlayerVideo | globalplayervideo | no_key_specific_evidence |  | P1-03 |
| Globo | globo | no_key_specific_evidence |  | P1-03 |
| GloboArticle | globoarticle | no_key_specific_evidence |  | P1-03 |
| glomex | glomex | no_key_specific_evidence |  | P1-03 |
| glomex:embed | glomex | no_key_specific_evidence |  | P1-03 |
| GMANetworkVideo | gmanetworkvideo | no_key_specific_evidence |  | P1-03 |
| Go | go | no_key_specific_evidence |  | P1-03 |
| GoDiscovery | godiscovery | no_key_specific_evidence |  | P1-03 |
| GodResource | godresource | no_key_specific_evidence |  | P1-03 |
| GodTube | godtube | broken_decision_pending |  | P1-04 |
| Golem | golem | no_key_specific_evidence |  | P1-03 |
| goodgame:stream | goodgame | no_key_specific_evidence |  | P0-03 |
| GoogleDrive | googledrive | no_key_specific_evidence |  | P1-03 |
| GoogleDrive:Folder | googledrive | no_key_specific_evidence |  | P1-03 |
| GoPro | gopro | no_key_specific_evidence |  | P1-03 |
| GoToStage | gotostage | no_key_specific_evidence |  | P1-03 |
| Graspop | graspop | no_key_specific_evidence |  | P1-03 |
| Gronkh | gronkh | no_key_specific_evidence |  | P1-03 |
| gronkh:feed | gronkh | no_key_specific_evidence |  | P1-03 |
| gronkh:vods | gronkh | no_key_specific_evidence |  | P1-03 |
| Groupon | groupon | no_key_specific_evidence |  | P1-03 |
| Harpodeon | harpodeon | no_key_specific_evidence |  | P1-03 |
| hbo | hbo | no_key_specific_evidence |  | P1-03 |
| HearThisAt | hearthisat | implemented_subset | HEARTHIS.md | P1-03 |
| Heise | heise | no_key_specific_evidence |  | P1-03 |
| HellPorno | hellporno | no_key_specific_evidence |  | P1-03 |
| hetklokhuis | hetklokhuis | no_key_specific_evidence |  | P1-03 |
| hgtv.com:show | hgtv.com | no_key_specific_evidence |  | P1-03 |
| HGTVDe | hgtvde | no_key_specific_evidence |  | P1-03 |
| HGTVUsa | hgtvusa | no_key_specific_evidence |  | P1-03 |
| HiDive | hidive | no_key_specific_evidence |  | P1-03 |
| HistoricFilms | historicfilms | no_key_specific_evidence |  | P1-03 |
| history:player | history | no_key_specific_evidence |  | P1-03 |
| history:topic | history | no_key_specific_evidence |  | P1-03 |
| HitRecord | hitrecord | no_key_specific_evidence |  | P1-03 |
| HollywoodReporter | hollywoodreporter | no_key_specific_evidence |  | P1-03 |
| HollywoodReporterPlaylist | hollywoodreporterplaylist | no_key_specific_evidence |  | P1-03 |
| Holodex | holodex | no_key_specific_evidence |  | P1-03 |
| HotNewHipHop | hotnewhiphop | broken_decision_pending |  | P1-04 |
| hotstar | hotstar | no_key_specific_evidence |  | P1-03 |
| hotstar:series | hotstar | no_key_specific_evidence |  | P1-03 |
| hrfernsehen | hrfernsehen | no_key_specific_evidence |  | P1-03 |
| HRTi | hrti | no_key_specific_evidence |  | P1-03 |
| HRTiPlaylist | hrtiplaylist | no_key_specific_evidence |  | P1-03 |
| HSEProduct | hseproduct | no_key_specific_evidence |  | P1-03 |
| HSEShow | hseshow | no_key_specific_evidence |  | P1-03 |
| html5 | html5 | no_key_specific_evidence |  | P1-03 |
| Huajiao | huajiao | no_key_specific_evidence |  | P1-03 |
| HuffPost | huffpost | no_key_specific_evidence |  | P1-03 |
| Hungama | hungama | no_key_specific_evidence |  | P1-03 |
| HungamaAlbumPlaylist | hungamaalbumplaylist | no_key_specific_evidence |  | P1-03 |
| HungamaSong | hungamasong | no_key_specific_evidence |  | P1-03 |
| huya:live | huya | no_key_specific_evidence |  | P0-03 |
| huya:video | huya | no_key_specific_evidence |  | P1-03 |
| Hypem | hypem | no_key_specific_evidence |  | P1-03 |
| Hytale | hytale | no_key_specific_evidence |  | P1-03 |
| Icareus | icareus | no_key_specific_evidence |  | P1-03 |
| IdagioAlbum | idagioalbum | no_key_specific_evidence |  | P1-03 |
| IdagioPersonalPlaylist | idagiopersonalplaylist | no_key_specific_evidence |  | P1-03 |
| IdagioPlaylist | idagioplaylist | no_key_specific_evidence |  | P1-03 |
| IdagioRecording | idagiorecording | no_key_specific_evidence |  | P1-03 |
| IdagioTrack | idagiotrack | no_key_specific_evidence |  | P1-03 |
| iflix:episode | iflix | no_key_specific_evidence |  | P1-03 |
| IflixSeries | iflixseries | no_key_specific_evidence |  | P1-03 |
| ign.com | ign.com | no_key_specific_evidence |  | P1-03 |
| IGNArticle | ignarticle | no_key_specific_evidence |  | P1-03 |
| IGNVideo | ignvideo | no_key_specific_evidence |  | P1-03 |
| iheartradio | iheartradio | implemented_subset | IHEART.md | P1-03 |
| iheartradio:podcast | iheartradio | implemented_subset | IHEART.md | P1-03 |
| IlPost | ilpost | no_key_specific_evidence |  | P1-03 |
| Iltalehti | iltalehti | no_key_specific_evidence |  | P1-03 |
| imdb | imdb | no_key_specific_evidence |  | P1-03 |
| imdb:list | imdb | no_key_specific_evidence |  | P1-03 |
| Imgur | imgur | implemented_subset | IMGUR.md | P1-03 |
| imgur:album | imgur | implemented_subset | IMGUR.md | P1-03 |
| imgur:gallery | imgur | no_key_specific_evidence |  | P1-03 |
| Ina | ina | no_key_specific_evidence |  | P1-03 |
| Inc | inc | no_key_specific_evidence |  | P1-03 |
| IndavideoEmbed | indavideoembed | no_key_specific_evidence |  | P1-03 |
| InfoQ | infoq | no_key_specific_evidence |  | P1-03 |
| Instagram | instagram | no_key_specific_evidence |  | P1-03 |
| instagram:story | instagram | no_key_specific_evidence |  | P1-03 |
| instagram:tag | instagram | no_key_specific_evidence |  | P1-03 |
| instagram:user | instagram | broken_decision_pending |  | P1-04 |
| InstagramIOS | instagramios | no_key_specific_evidence |  | P1-03 |
| Internazionale | internazionale | no_key_specific_evidence |  | P1-03 |
| InvestigationDiscovery | investigationdiscovery | no_key_specific_evidence |  | P1-03 |
| IPrima | iprima | no_key_specific_evidence |  | P1-03 |
| IPrimaCNN | iprimacnn | no_key_specific_evidence |  | P1-03 |
| iq.com | iq.com | no_key_specific_evidence |  | P1-03 |
| iq.com:album | iq.com | no_key_specific_evidence |  | P1-03 |
| iqiyi | iqiyi | no_key_specific_evidence |  | P1-03 |
| IslamChannel | islamchannel | no_key_specific_evidence |  | P1-03 |
| IslamChannelSeries | islamchannelseries | no_key_specific_evidence |  | P1-03 |
| IsraelNationalNews | israelnationalnews | no_key_specific_evidence |  | P1-03 |
| ITProTV | itprotv | no_key_specific_evidence |  | P1-03 |
| ITProTVCourse | itprotvcourse | no_key_specific_evidence |  | P1-03 |
| ITV | itv | no_key_specific_evidence |  | P1-03 |
| ITVBTCC | itvbtcc | no_key_specific_evidence |  | P1-03 |
| ivi | ivi | no_key_specific_evidence |  | P1-03 |
| ivi:compilation | ivi | no_key_specific_evidence |  | P1-03 |
| ivideon | ivideon | no_key_specific_evidence |  | P1-03 |
| Ivoox | ivoox | no_key_specific_evidence |  | P1-03 |
| iwara | iwara | no_key_specific_evidence |  | P1-03 |
| iwara:playlist | iwara | no_key_specific_evidence |  | P1-03 |
| iwara:user | iwara | no_key_specific_evidence |  | P1-03 |
| Ixigua | ixigua | no_key_specific_evidence |  | P1-03 |
| Jamendo | jamendo | implemented_subset | JAMENDO.md | P1-03 |
| JamendoAlbum | jamendoalbum | implemented_subset | JAMENDO.md | P1-03 |
| JeuxVideo | jeuxvideo | broken_decision_pending |  | P1-04 |
| jiosaavn:album | jiosaavn | implemented_subset | JIOSAAVN.md | P1-03 |
| jiosaavn:artist | jiosaavn | implemented_subset | JIOSAAVN.md | P1-03 |
| jiosaavn:playlist | jiosaavn | implemented_subset | JIOSAAVN.md | P1-03 |
| jiosaavn:show | jiosaavn | implemented_subset | JIOSAAVN.md | P1-03 |
| jiosaavn:​show:playlist | jiosaavn | implemented_subset | JIOSAAVN.md | P1-03 |
| jiosaavn:song | jiosaavn | implemented_subset | JIOSAAVN.md | P1-03 |
| Joj | joj | no_key_specific_evidence |  | P1-03 |
| Jove | jove | no_key_specific_evidence |  | P1-03 |
| JStream | jstream | no_key_specific_evidence |  | P0-03 |
| JTBC | jtbc | no_key_specific_evidence |  | P1-03 |
| JTBC:program | jtbc | no_key_specific_evidence |  | P1-03 |
| JWPlatform | jwplatform | implemented_subset | HOSTING.mdMIXED-COLLECTIONS.mdADAPTIVE-RECOVERY.mdPUBLICATION-SESSION-RECOVERY.mdWRITE-CLEANUP-RECOVERY.mdCHANGED-JOB-SESSIONS.mdNO-OVERWRITE-TRANSFERS.mdADAPTIVE-NO-OVERWRITE.mdLIVE-OUTPUT-INVENTORIES.md | P1-01 |
| Kakao | kakao | no_key_specific_evidence |  | P1-03 |
| Kaltura | kaltura | implemented_subset | KALTURA.mdWRITE-CLEANUP-RECOVERY.mdCHANGED-JOB-SESSIONS.mdADAPTIVE-NO-OVERWRITE.mdLIVE-OUTPUT-INVENTORIES.md | P1-01 |
| KankaNews | kankanews | broken_decision_pending |  | P1-04 |
| Karaoketv | karaoketv | broken_decision_pending |  | P1-04 |
| Katsomo | katsomo | broken_decision_pending |  | P1-04 |
| KelbyOne | kelbyone | broken_decision_pending |  | P1-04 |
| Kenh14Playlist | kenh14playlist | no_key_specific_evidence |  | P1-03 |
| Kenh14Video | kenh14video | no_key_specific_evidence |  | P1-03 |
| khanacademy | khanacademy | no_key_specific_evidence |  | P1-03 |
| khanacademy:unit | khanacademy | no_key_specific_evidence |  | P1-03 |
| kick:clips | kick | no_key_specific_evidence |  | P1-03 |
| kick:live | kick | no_key_specific_evidence |  | P0-03 |
| kick:vod | kick | no_key_specific_evidence |  | P1-03 |
| Kicker | kicker | no_key_specific_evidence |  | P1-03 |
| KickStarter | kickstarter | no_key_specific_evidence |  | P1-03 |
| Kika | kika | no_key_specific_evidence |  | P1-03 |
| KikaPlaylist | kikaplaylist | no_key_specific_evidence |  | P1-03 |
| KinoPoisk | kinopoisk | no_key_specific_evidence |  | P1-03 |
| Kommunetv | kommunetv | no_key_specific_evidence |  | P1-03 |
| KompasVideo | kompasvideo | no_key_specific_evidence |  | P1-03 |
| KrasView | krasview | broken_decision_pending |  | P1-04 |
| KTH | kth | no_key_specific_evidence |  | P1-03 |
| Ku6 | ku6 | no_key_specific_evidence |  | P1-03 |
| KukuluLive | kukululive | no_key_specific_evidence |  | P0-03 |
| kuwo:album | kuwo | broken_decision_pending |  | P1-04 |
| kuwo:category | kuwo | broken_decision_pending |  | P1-04 |
| kuwo:chart | kuwo | broken_decision_pending |  | P1-04 |
| kuwo:mv | kuwo | broken_decision_pending |  | P1-04 |
| kuwo:singer | kuwo | broken_decision_pending |  | P1-04 |
| kuwo:song | kuwo | broken_decision_pending |  | P1-04 |
| la7.it | la7.it | no_key_specific_evidence |  | P1-03 |
| la7.it:​pod:episode | la7.it | no_key_specific_evidence |  | P1-03 |
| la7.it:podcast | la7.it | no_key_specific_evidence |  | P1-03 |
| laracasts | laracasts | no_key_specific_evidence |  | P1-03 |
| laracasts:series | laracasts | no_key_specific_evidence |  | P1-03 |
| LastFM | lastfm | no_key_specific_evidence |  | P1-03 |
| LastFMPlaylist | lastfmplaylist | no_key_specific_evidence |  | P1-03 |
| LastFMUser | lastfmuser | no_key_specific_evidence |  | P1-03 |
| LaXarxaMes | laxarxames | no_key_specific_evidence |  | P1-03 |
| lbry | lbry | implemented_subset | ODYSEE.md | P1-03 |
| lbry:channel | lbry | implemented_subset | ODYSEE.md | P1-03 |
| lbry:playlist | lbry | implemented_subset | ODYSEE.md | P1-03 |
| LCI | lci | no_key_specific_evidence |  | P1-03 |
| Lcp | lcp | broken_decision_pending |  | P1-04 |
| LcpPlay | lcpplay | broken_decision_pending |  | P1-04 |
| Le | le | no_key_specific_evidence |  | P1-03 |
| LearningOnScreen | learningonscreen | no_key_specific_evidence |  | P1-03 |
| Lecture2Go | lecture2go | broken_decision_pending |  | P1-04 |
| Lecturio | lecturio | no_key_specific_evidence |  | P1-03 |
| LecturioCourse | lecturiocourse | no_key_specific_evidence |  | P1-03 |
| LecturioDeCourse | lecturiodecourse | no_key_specific_evidence |  | P1-03 |
| LeFigaroVideoEmbed | lefigarovideoembed | no_key_specific_evidence |  | P1-03 |
| LeFigaroVideoSection | lefigarovideosection | no_key_specific_evidence |  | P1-03 |
| LEGO | lego | no_key_specific_evidence |  | P1-03 |
| Lemonde | lemonde | no_key_specific_evidence |  | P1-03 |
| Lenta | lenta | broken_decision_pending |  | P1-04 |
| LePlaylist | leplaylist | no_key_specific_evidence |  | P1-03 |
| Libsyn | libsyn | implemented_subset | LIBSYN.md | P1-03 |
| life | life | no_key_specific_evidence |  | P1-03 |
| life:embed | life | no_key_specific_evidence |  | P1-03 |
| likee | likee | no_key_specific_evidence |  | P1-03 |
| likee:user | likee | no_key_specific_evidence |  | P1-03 |
| LinkedIn | linkedin | no_key_specific_evidence |  | P1-03 |
| linkedin:events | linkedin | no_key_specific_evidence |  | P1-03 |
| linkedin:learning | linkedin | no_key_specific_evidence |  | P1-03 |
| linkedin:​learning:course | linkedin | no_key_specific_evidence |  | P1-03 |
| Liputan6 | liputan6 | no_key_specific_evidence |  | P1-03 |
| ListenNotes | listennotes | no_key_specific_evidence |  | P1-03 |
| LiTV | litv | no_key_specific_evidence |  | P1-03 |
| LiveJournal | livejournal | broken_decision_pending |  | P1-04 |
| Livestreamfails | livestreamfails | no_key_specific_evidence |  | P0-03 |
| Lnk | lnk | no_key_specific_evidence |  | P1-03 |
| loc | loc | no_key_specific_evidence |  | P1-03 |
| Locipo | locipo | no_key_specific_evidence |  | P1-03 |
| LocipoPlaylist | locipoplaylist | no_key_specific_evidence |  | P1-03 |
| Loco | loco | no_key_specific_evidence |  | P1-03 |
| loom | loom | implemented_subset | LOOM.md | P1-03 |
| loom:folder | loom | broken_decision_pending |  | P1-04 |
| LoveHomePorn | lovehomeporn | no_key_specific_evidence |  | P1-03 |
| LRTRadio | lrtradio | no_key_specific_evidence |  | P1-03 |
| LRTStream | lrtstream | no_key_specific_evidence |  | P0-03 |
| LRTVOD | lrtvod | no_key_specific_evidence |  | P1-03 |
| LSMLREmbed | lsmlrembed | no_key_specific_evidence |  | P1-03 |
| LSMLTVEmbed | lsmltvembed | no_key_specific_evidence |  | P1-03 |
| LSMReplay | lsmreplay | no_key_specific_evidence |  | P1-03 |
| Lumni | lumni | no_key_specific_evidence |  | P1-03 |
| maariv.co.il | maariv.co.il | no_key_specific_evidence |  | P1-03 |
| MagellanTV | magellantv | no_key_specific_evidence |  | P1-03 |
| MagentaMusik | magentamusik | no_key_specific_evidence |  | P1-03 |
| mailru | mailru | no_key_specific_evidence |  | P1-03 |
| mailru:music | mailru | no_key_specific_evidence |  | P1-03 |
| mailru:​music:search | mailru | no_key_specific_evidence |  | P1-03 |
| MainStreaming | mainstreaming | no_key_specific_evidence |  | P0-03 |
| mangomolo:live | mangomolo | no_key_specific_evidence |  | P0-03 |
| mangomolo:video | mangomolo | no_key_specific_evidence |  | P1-03 |
| MangoTV | mangotv | no_key_specific_evidence |  | P1-03 |
| ManyVids | manyvids | no_key_specific_evidence |  | P1-03 |
| MaoriTV | maoritv | no_key_specific_evidence |  | P1-03 |
| Markiza | markiza | broken_decision_pending |  | P1-04 |
| MarkizaPage | markizapage | broken_decision_pending |  | P1-04 |
| massengeschmack.tv | massengeschmack.tv | no_key_specific_evidence |  | P1-03 |
| Masters | masters | no_key_specific_evidence |  | P1-03 |
| MatchiTV | matchitv | no_key_specific_evidence |  | P1-03 |
| MatchTV | matchtv | no_key_specific_evidence |  | P1-03 |
| mave | mave | no_key_specific_evidence |  | P1-03 |
| mave:channel | mave | no_key_specific_evidence |  | P1-03 |
| MBN | mbn | no_key_specific_evidence |  | P1-03 |
| MDR | mdr | no_key_specific_evidence |  | P1-03 |
| MedalTV | medaltv | implemented_subset | MEDAL.md | P1-03 |
| media.ccc.de | media.ccc.de | no_key_specific_evidence |  | P1-03 |
| media.ccc.de:lists | media.ccc.de | no_key_specific_evidence |  | P1-03 |
| Mediaite | mediaite | no_key_specific_evidence |  | P1-03 |
| MediaKlikk | mediaklikk | no_key_specific_evidence |  | P1-03 |
| Medialaan | medialaan | no_key_specific_evidence |  | P1-03 |
| Mediaset | mediaset | no_key_specific_evidence |  | P1-03 |
| MediasetShow | mediasetshow | no_key_specific_evidence |  | P1-03 |
| Mediasite | mediasite | no_key_specific_evidence |  | P1-03 |
| MediasiteCatalog | mediasitecatalog | no_key_specific_evidence |  | P1-03 |
| MediasiteNamedCatalog | mediasitenamedcatalog | no_key_specific_evidence |  | P1-03 |
| MediaStream | mediastream | no_key_specific_evidence |  | P0-03 |
| MediaWorksNZVOD | mediaworksnzvod | no_key_specific_evidence |  | P1-03 |
| Medici | medici | no_key_specific_evidence |  | P1-03 |
| megaphone.fm | megaphone.fm | implemented_subset | MEGAPHONE.md | P1-03 |
| megatvcom | megatvcom | no_key_specific_evidence |  | P1-03 |
| megatvcom:embed | megatvcom | no_key_specific_evidence |  | P1-03 |
| Meipai | meipai | no_key_specific_evidence |  | P1-03 |
| mellowfan | mellowfan | no_key_specific_evidence |  | P1-03 |
| mellowfan:capture | mellowfan | no_key_specific_evidence |  | P1-03 |
| mellowfan:channel | mellowfan | no_key_specific_evidence |  | P1-03 |
| mellowfan:​channel:search | mellowfan | no_key_specific_evidence |  | P1-03 |
| mellowfan:movie | mellowfan | no_key_specific_evidence |  | P1-03 |
| mellowfan:playlist | mellowfan | no_key_specific_evidence |  | P1-03 |
| MelonVOD | melonvod | no_key_specific_evidence |  | P1-03 |
| Metacritic | metacritic | no_key_specific_evidence |  | P1-03 |
| mewatch | mewatch | no_key_specific_evidence |  | P1-03 |
| MicrosoftBuild | microsoftbuild | no_key_specific_evidence |  | P1-03 |
| MicrosoftEmbed | microsoftembed | implemented_subset | MICROSOFT-EMBED.md | P1-03 |
| MicrosoftLearnEpisode | microsoftlearnepisode | implemented_subset | MICROSOFT-LEARN.md | P1-03 |
| MicrosoftLearnPlaylist | microsoftlearnplaylist | implemented_subset | MICROSOFT-LEARN.md | P1-03 |
| MicrosoftLearnSession | microsoftlearnsession | implemented_subset | MICROSOFT-LEARN.md | P1-03 |
| MicrosoftMedius | microsoftmedius | no_key_specific_evidence |  | P1-03 |
| minds | minds | no_key_specific_evidence |  | P1-03 |
| minds:channel | minds | no_key_specific_evidence |  | P1-03 |
| minds:group | minds | no_key_specific_evidence |  | P1-03 |
| mir24.tv | mir24.tv | no_key_specific_evidence |  | P1-03 |
| mirrativ | mirrativ | no_key_specific_evidence |  | P1-03 |
| mirrativ:user | mirrativ | no_key_specific_evidence |  | P1-03 |
| MirrorCoUK | mirrorcouk | no_key_specific_evidence |  | P1-03 |
| mixch | mixch | no_key_specific_evidence |  | P1-03 |
| mixch:archive | mixch | no_key_specific_evidence |  | P1-03 |
| mixch:movie | mixch | no_key_specific_evidence |  | P1-03 |
| mixcloud | mixcloud | implemented_subset | MIXCLOUD.md | P1-03 |
| mixcloud:playlist | mixcloud | implemented_subset | MIXCLOUD.md | P1-03 |
| mixcloud:user | mixcloud | implemented_subset | MIXCLOUD.md | P1-03 |
| Mixlr | mixlr | no_key_specific_evidence |  | P1-03 |
| MixlrRecoring | mixlrrecoring | no_key_specific_evidence |  | P1-03 |
| MLB | mlb | no_key_specific_evidence |  | P1-03 |
| MLBArticle | mlbarticle | no_key_specific_evidence |  | P1-03 |
| MLBTV | mlbtv | no_key_specific_evidence |  | P1-03 |
| MLBVideo | mlbvideo | no_key_specific_evidence |  | P1-03 |
| MLSSoccer | mlssoccer | no_key_specific_evidence |  | P1-03 |
| MNetTV | mnettv | no_key_specific_evidence |  | P1-03 |
| MNetTVLive | mnettvlive | no_key_specific_evidence |  | P0-03 |
| MNetTVRecordings | mnettvrecordings | no_key_specific_evidence |  | P1-03 |
| MochaVideo | mochavideo | no_key_specific_evidence |  | P1-03 |
| Mojevideo | mojevideo | no_key_specific_evidence |  | P1-03 |
| Monstercat | monstercat | no_key_specific_evidence |  | P1-03 |
| monstersiren | monstersiren | no_key_specific_evidence |  | P1-03 |
| Motorsport | motorsport | broken_decision_pending |  | P1-04 |
| MovieFap | moviefap | no_key_specific_evidence |  | P1-03 |
| moviepilot | moviepilot | no_key_specific_evidence |  | P1-03 |
| MovingImage | movingimage | no_key_specific_evidence |  | P1-03 |
| MSN | msn | no_key_specific_evidence |  | P1-03 |
| mtg | mtg | no_key_specific_evidence |  | P1-03 |
| mtv | mtv | no_key_specific_evidence |  | P1-03 |
| MTVUutisetArticle | mtvuutisetarticle | broken_decision_pending |  | P1-04 |
| MuenchenTV | muenchentv | broken_decision_pending |  | P1-04 |
| MujRozhlas | mujrozhlas | no_key_specific_evidence |  | P1-03 |
| Murrtube | murrtube | no_key_specific_evidence |  | P1-03 |
| MurrtubeUser | murrtubeuser | broken_decision_pending |  | P1-04 |
| MuseAI | museai | no_key_specific_evidence |  | P1-03 |
| MuseScore | musescore | no_key_specific_evidence |  | P1-03 |
| Mux | mux | no_key_specific_evidence |  | P1-03 |
| Mx3 | mx3 | no_key_specific_evidence |  | P1-03 |
| Mx3Neo | mx3neo | no_key_specific_evidence |  | P1-03 |
| Mx3Volksmusik | mx3volksmusik | no_key_specific_evidence |  | P1-03 |
| mxplayer | mxplayer | no_key_specific_evidence |  | P1-03 |
| mxplayer:season | mxplayer | no_key_specific_evidence |  | P1-03 |
| mxplayer:show | mxplayer | no_key_specific_evidence |  | P1-03 |
| MySpace | myspace | no_key_specific_evidence |  | P1-03 |
| MySpace:album | myspace | no_key_specific_evidence |  | P1-03 |
| MySpass | myspass | no_key_specific_evidence |  | P1-03 |
| MyVideoGe | myvideoge | no_key_specific_evidence |  | P1-03 |
| MyVidster | myvidster | no_key_specific_evidence |  | P1-03 |
| Mzaalo | mzaalo | no_key_specific_evidence |  | P1-03 |
| n-tv.de | n-tv.de | no_key_specific_evidence |  | P1-03 |
| N1Info:article | n1info | no_key_specific_evidence |  | P1-03 |
| N1InfoAsset | n1infoasset | no_key_specific_evidence |  | P1-03 |
| NascarClassics | nascarclassics | no_key_specific_evidence |  | P1-03 |
| Nate | nate | no_key_specific_evidence |  | P1-03 |
| NateProgram | nateprogram | no_key_specific_evidence |  | P1-03 |
| NationalGeographicTV | nationalgeographictv | no_key_specific_evidence |  | P1-03 |
| Naver | naver | no_key_specific_evidence |  | P1-03 |
| Naver:live | naver | no_key_specific_evidence |  | P0-03 |
| nba | nba | broken_decision_pending |  | P1-04 |
| nba:channel | nba | broken_decision_pending |  | P1-04 |
| nba:embed | nba | broken_decision_pending |  | P1-04 |
| nba:watch | nba | broken_decision_pending |  | P1-04 |
| nba:​watch:collection | nba | broken_decision_pending |  | P1-04 |
| nba:​watch:embed | nba | broken_decision_pending |  | P1-04 |
| NBC | nbc | no_key_specific_evidence |  | P1-03 |
| NBCNews | nbcnews | no_key_specific_evidence |  | P1-03 |
| nbcolympics | nbcolympics | no_key_specific_evidence |  | P1-03 |
| nbcolympics:stream | nbcolympics | broken_decision_pending |  | P1-04 |
| NBCSports | nbcsports | broken_decision_pending |  | P1-04 |
| NBCSportsStream | nbcsportsstream | broken_decision_pending |  | P1-04 |
| NBCSportsVPlayer | nbcsportsvplayer | broken_decision_pending |  | P1-04 |
| NBCStations | nbcstations | no_key_specific_evidence |  | P1-03 |
| ndr | ndr | no_key_specific_evidence |  | P1-03 |
| ndr:embed | ndr | no_key_specific_evidence |  | P1-03 |
| ndr:​embed:base | ndr | no_key_specific_evidence |  | P1-03 |
| NDTV | ndtv | broken_decision_pending |  | P1-04 |
| nebula:channel | nebula | no_key_specific_evidence |  | P1-03 |
| nebula:media | nebula | no_key_specific_evidence |  | P1-03 |
| nebula:season | nebula | no_key_specific_evidence |  | P1-03 |
| nebula:subscriptions | nebula | no_key_specific_evidence |  | P1-03 |
| nebula:video | nebula | no_key_specific_evidence |  | P1-03 |
| NekoHacker | nekohacker | no_key_specific_evidence |  | P1-03 |
| Nest | nest | no_key_specific_evidence |  | P1-03 |
| NestClip | nestclip | no_key_specific_evidence |  | P1-03 |
| NetAppCollection | netappcollection | no_key_specific_evidence |  | P1-03 |
| NetAppVideo | netappvideo | no_key_specific_evidence |  | P1-03 |
| netease:album | netease | implemented_subset | NETEASE.md | P1-03 |
| netease:djradio | netease | implemented_subset | NETEASE.md | P1-03 |
| netease:mv | netease | implemented_subset | NETEASE.md | P1-03 |
| netease:playlist | netease | implemented_subset | NETEASE.md | P1-03 |
| netease:program | netease | implemented_subset | NETEASE.md | P1-03 |
| netease:singer | netease | implemented_subset | NETEASE.md | P1-03 |
| netease:song | netease | implemented_subset | NETEASE.md | P1-03 |
| NetPlusTV | netplustv | no_key_specific_evidence |  | P1-03 |
| NetPlusTVLive | netplustvlive | no_key_specific_evidence |  | P0-03 |
| NetPlusTVRecordings | netplustvrecordings | no_key_specific_evidence |  | P1-03 |
| Netzkino | netzkino | no_key_specific_evidence |  | P1-03 |
| Newgrounds | newgrounds | implemented_subset | NEWGROUNDS.md | P1-03 |
| Newgrounds:playlist | newgrounds | implemented_subset | NEWGROUNDS.md | P1-03 |
| Newgrounds:user | newgrounds | implemented_subset | NEWGROUNDS.md | P1-03 |
| NewsPicks | newspicks | no_key_specific_evidence |  | P1-03 |
| Newsy | newsy | no_key_specific_evidence |  | P1-03 |
| Nexx | nexx | no_key_specific_evidence |  | P1-03 |
| NexxEmbed | nexxembed | no_key_specific_evidence |  | P1-03 |
| nfb | nfb | implemented_subset | NFB.md | P1-03 |
| nfb:series | nfb | implemented_subset | NFB.md | P1-03 |
| NFHSNetwork | nfhsnetwork | no_key_specific_evidence |  | P1-03 |
| nfl.com | nfl.com | no_key_specific_evidence |  | P1-03 |
| nfl.com:article | nfl.com | no_key_specific_evidence |  | P1-03 |
| nfl.com:​plus:episode | nfl.com | no_key_specific_evidence |  | P1-03 |
| nfl.com:​plus:replay | nfl.com | no_key_specific_evidence |  | P1-03 |
| NhkForSchoolBangumi | nhkforschoolbangumi | no_key_specific_evidence |  | P1-03 |
| NhkForSchoolProgramList | nhkforschoolprogramlist | no_key_specific_evidence |  | P1-03 |
| NhkForSchoolSubject | nhkforschoolsubject | no_key_specific_evidence |  | P1-03 |
| NhkRadioNewsPage | nhkradionewspage | no_key_specific_evidence |  | P1-03 |
| NhkRadiru | nhkradiru | no_key_specific_evidence |  | P1-03 |
| NhkRadiruLive | nhkradirulive | no_key_specific_evidence |  | P0-03 |
| NhkVod | nhkvod | no_key_specific_evidence |  | P1-03 |
| NhkVodProgram | nhkvodprogram | no_key_specific_evidence |  | P1-03 |
| nhl.com | nhl.com | no_key_specific_evidence |  | P1-03 |
| nick.com | nick.com | no_key_specific_evidence |  | P1-03 |
| niconico | niconico | implemented_subset | CATALOG-SESSIONS.md | P1-03 |
| niconico:history | niconico | no_key_specific_evidence |  | P1-03 |
| niconico:live | niconico | no_key_specific_evidence |  | P0-03 |
| niconico:playlist | niconico | no_key_specific_evidence |  | P1-03 |
| niconico:series | niconico | no_key_specific_evidence |  | P1-03 |
| niconico:tag | niconico | no_key_specific_evidence |  | P1-03 |
| NiconicoChannelPlus | niconicochannelplus | no_key_specific_evidence |  | P1-03 |
| NiconicoChannelPlus:​channel:lives | niconicochannelplus | no_key_specific_evidence |  | P0-03 |
| NiconicoChannelPlus:​channel:videos | niconicochannelplus | no_key_specific_evidence |  | P1-03 |
| NiconicoUser | niconicouser | no_key_specific_evidence |  | P1-03 |
| nicovideo:search | nicovideo | no_key_specific_evidence |  | P1-03 |
| nicovideo:​search:date | nicovideo | no_key_specific_evidence |  | P1-03 |
| nicovideo:search_url | nicovideo | no_key_specific_evidence |  | P1-03 |
| NinaProtocol | ninaprotocol | no_key_specific_evidence |  | P1-03 |
| Nintendo | nintendo | no_key_specific_evidence |  | P1-03 |
| Nitter | nitter | no_key_specific_evidence |  | P1-03 |
| njoy | njoy | no_key_specific_evidence |  | P1-03 |
| njoy:embed | njoy | no_key_specific_evidence |  | P1-03 |
| NobelPrize | nobelprize | no_key_specific_evidence |  | P1-03 |
| NoicePodcast | noicepodcast | no_key_specific_evidence |  | P1-03 |
| NonkTube | nonktube | no_key_specific_evidence |  | P1-03 |
| NoodleMagazine | noodlemagazine | no_key_specific_evidence |  | P1-03 |
| NOSNLArticle | nosnlarticle | no_key_specific_evidence |  | P1-03 |
| Nova | nova | no_key_specific_evidence |  | P1-03 |
| NovaEmbed | novaembed | no_key_specific_evidence |  | P1-03 |
| NovaPlay | novaplay | no_key_specific_evidence |  | P1-03 |
| NowCanal | nowcanal | no_key_specific_evidence |  | P1-03 |
| nowness | nowness | implemented_subset | HOSTING-COLLECTIONS.mdMIXED-COLLECTIONS.md | P1-01 |
| nowness:playlist | nowness | implemented_subset | HOSTING-COLLECTIONS.mdMIXED-COLLECTIONS.mdADAPTIVE-RECOVERY.mdLIVE-SABR-AUTHORIZATION.mdWINDOW-PREFIX-RECOVERY.md | P1-01 |
| nowness:series | nowness | implemented_subset | HOSTING-COLLECTIONS.mdMIXED-COLLECTIONS.md | P1-01 |
| Noz | noz | broken_decision_pending |  | P1-04 |
| npo | npo | no_key_specific_evidence |  | P1-03 |
| npo.nl:live | npo.nl | no_key_specific_evidence |  | P0-03 |
| npo.nl:radio | npo.nl | no_key_specific_evidence |  | P1-03 |
| npo.nl:​radio:fragment | npo.nl | no_key_specific_evidence |  | P1-03 |
| Npr | npr | no_key_specific_evidence |  | P1-03 |
| NRK | nrk | implemented_subset | PUBLIC-BROADCAST.md | P1-03 |
| NRKPlaylist | nrkplaylist | no_key_specific_evidence |  | P1-03 |
| NRKRadioPodkast | nrkradiopodkast | no_key_specific_evidence |  | P1-03 |
| NRKSkole | nrkskole | no_key_specific_evidence |  | P1-03 |
| NRKTV | nrktv | implemented_subset | PUBLIC-BROADCAST.md | P1-03 |
| NRKTVDirekte | nrktvdirekte | no_key_specific_evidence |  | P1-03 |
| NRKTVEpisode | nrktvepisode | no_key_specific_evidence |  | P1-03 |
| NRKTVEpisodes | nrktvepisodes | no_key_specific_evidence |  | P1-03 |
| NRKTVSeason | nrktvseason | no_key_specific_evidence |  | P1-03 |
| NRKTVSeries | nrktvseries | no_key_specific_evidence |  | P1-03 |
| NRLTV | nrltv | broken_decision_pending |  | P1-04 |
| nts.live | nts.live | no_key_specific_evidence |  | P0-03 |
| ntv.ru | ntv.ru | no_key_specific_evidence |  | P1-03 |
| NubilesPorn | nubilesporn | no_key_specific_evidence |  | P1-03 |
| Nuvid | nuvid | no_key_specific_evidence |  | P1-03 |
| NYTimes | nytimes | no_key_specific_evidence |  | P1-03 |
| NYTimesArticle | nytimesarticle | no_key_specific_evidence |  | P1-03 |
| NYTimesCookingGuide | nytimescookingguide | no_key_specific_evidence |  | P1-03 |
| NYTimesCookingRecipe | nytimescookingrecipe | no_key_specific_evidence |  | P1-03 |
| nzherald | nzherald | no_key_specific_evidence |  | P1-03 |
| NZOnScreen | nzonscreen | no_key_specific_evidence |  | P1-03 |
| NZZ | nzz | no_key_specific_evidence |  | P1-03 |
| ocw.mit.edu | ocw.mit.edu | no_key_specific_evidence |  | P1-03 |
| Odnoklassniki | odnoklassniki | no_key_specific_evidence |  | P1-03 |
| OfTV | oftv | no_key_specific_evidence |  | P1-03 |
| OfTVPlaylist | oftvplaylist | no_key_specific_evidence |  | P1-03 |
| OktoberfestTV | oktoberfesttv | broken_decision_pending |  | P1-04 |
| OlympicsReplay | olympicsreplay | no_key_specific_evidence |  | P1-03 |
| Omnyfm | omnyfm | implemented_subset | OMNY.md | P1-03 |
| OmnyfmPlaylist | omnyfmplaylist | implemented_subset | OMNY.md | P1-03 |
| OmnyfmShow | omnyfmshow | implemented_subset | OMNY.md | P1-03 |
| on24 | on24 | no_key_specific_evidence |  | P1-03 |
| OnDemandChinaEpisode | ondemandchinaepisode | no_key_specific_evidence |  | P1-03 |
| OnDemandKorea | ondemandkorea | no_key_specific_evidence |  | P1-03 |
| OnDemandKoreaProgram | ondemandkoreaprogram | no_key_specific_evidence |  | P1-03 |
| OneFootball | onefootball | no_key_specific_evidence |  | P1-03 |
| OnePlacePodcast | oneplacepodcast | no_key_specific_evidence |  | P1-03 |
| onet.pl | onet.pl | no_key_specific_evidence |  | P1-03 |
| onet.tv | onet.tv | no_key_specific_evidence |  | P1-03 |
| onet.tv:channel | onet.tv | no_key_specific_evidence |  | P1-03 |
| OnetMVP | onetmvp | no_key_specific_evidence |  | P1-03 |
| onsen | onsen | no_key_specific_evidence |  | P1-03 |
| Opencast | opencast | no_key_specific_evidence |  | P1-03 |
| OpencastPlaylist | opencastplaylist | no_key_specific_evidence |  | P1-03 |
| orf:​fm4:story | orf | no_key_specific_evidence |  | P1-03 |
| orf:iptv | orf | no_key_specific_evidence |  | P1-03 |
| orf:on | orf | implemented_subset | ORF-BROADCAST.md | P1-03 |
| orf:podcast | orf | implemented_subset | PROVIDER-FAMILIES.md | P1-03 |
| orf:radio | orf | implemented_subset | ORF-BROADCAST.md | P1-03 |
| OsnatelTV | osnateltv | no_key_specific_evidence |  | P1-03 |
| OsnatelTVLive | osnateltvlive | no_key_specific_evidence |  | P0-03 |
| OsnatelTVRecordings | osnateltvrecordings | no_key_specific_evidence |  | P1-03 |
| OutsideTV | outsidetv | no_key_specific_evidence |  | P1-03 |
| OwnCloud | owncloud | no_key_specific_evidence |  | P1-03 |
| PacktPub | packtpub | no_key_specific_evidence |  | P1-03 |
| PacktPubCourse | packtpubcourse | no_key_specific_evidence |  | P1-03 |
| PalcoMP3:artist | palcomp3 | no_key_specific_evidence |  | P1-03 |
| PalcoMP3:song | palcomp3 | no_key_specific_evidence |  | P1-03 |
| PalcoMP3:video | palcomp3 | no_key_specific_evidence |  | P1-03 |
| PandaTv | pandatv | no_key_specific_evidence |  | P1-03 |
| Panopto | panopto | implemented_subset | PANOPTO.md | P1-03 |
| PanoptoList | panoptolist | implemented_subset | PANOPTO.md | P1-03 |
| PanoptoPlaylist | panoptoplaylist | implemented_subset | PANOPTO.md | P1-03 |
| ParamountPressExpress | paramountpressexpress | no_key_specific_evidence |  | P1-03 |
| Parler | parler | no_key_specific_evidence |  | P1-03 |
| parliamentlive.tv | parliamentlive.tv | no_key_specific_evidence |  | P0-03 |
| Parlview | parlview | no_key_specific_evidence |  | P1-03 |
| parti:livestream | parti | no_key_specific_evidence |  | P0-03 |
| parti:video | parti | no_key_specific_evidence |  | P1-03 |
| patreon | patreon | no_key_specific_evidence |  | P1-03 |
| patreon:campaign | patreon | no_key_specific_evidence |  | P1-03 |
| pbs | pbs | implemented_subset | PBS.md | P1-03 |
| PBSKids | pbskids | implemented_subset | PBS-KIDS.md | P1-03 |
| PearVideo | pearvideo | no_key_specific_evidence |  | P1-03 |
| PeekVids | peekvids | no_key_specific_evidence |  | P1-03 |
| peer.tv | peer.tv | no_key_specific_evidence |  | P1-03 |
| PeerTube | peertube | implemented_subset | PEERTUBE.mdWRITE-CLEANUP-RECOVERY.md | P1-03 |
| PeerTube:Playlist | peertube | implemented_subset | PEERTUBE.mdWRITE-CLEANUP-RECOVERY.md | P1-03 |
| peloton | peloton | no_key_specific_evidence |  | P1-03 |
| peloton:live | peloton | no_key_specific_evidence |  | P0-03 |
| PerformGroup | performgroup | no_key_specific_evidence |  | P1-03 |
| periscope | periscope | no_key_specific_evidence |  | P1-03 |
| periscope:user | periscope | no_key_specific_evidence |  | P1-03 |
| PGATour | pgatour | no_key_specific_evidence |  | P1-03 |
| PhilharmonieDeParis | philharmoniedeparis | no_key_specific_evidence |  | P1-03 |
| phoenix.de | phoenix.de | no_key_specific_evidence |  | P1-03 |
| Photobucket | photobucket | no_key_specific_evidence |  | P1-03 |
| PiaLive | pialive | no_key_specific_evidence |  | P0-03 |
| Piapro | piapro | no_key_specific_evidence |  | P1-03 |
| picarto | picarto | no_key_specific_evidence |  | P1-03 |
| picarto:vod | picarto | no_key_specific_evidence |  | P1-03 |
| Piksel | piksel | no_key_specific_evidence |  | P1-03 |
| Pinkbike | pinkbike | no_key_specific_evidence |  | P1-03 |
| Pinterest | pinterest | implemented_subset | PINTEREST.md | P1-03 |
| PinterestCollection | pinterestcollection | implemented_subset | PINTEREST.md | P1-03 |
| Platzi | platzi | no_key_specific_evidence |  | P1-03 |
| PlatziCourse | platzicourse | no_key_specific_evidence |  | P1-03 |
| play.tv | play.tv | no_key_specific_evidence |  | P1-03 |
| player.sky.it | player.sky.it | no_key_specific_evidence |  | P1-03 |
| PlayerFm | playerfm | no_key_specific_evidence |  | P1-03 |
| PlaySuisse | playsuisse | no_key_specific_evidence |  | P1-03 |
| Playtvak | playtvak | broken_decision_pending |  | P1-04 |
| PlayVids | playvids | no_key_specific_evidence |  | P1-03 |
| pluralsight | pluralsight | no_key_specific_evidence |  | P1-03 |
| pluralsight:course | pluralsight | no_key_specific_evidence |  | P1-03 |
| PlutoTV | plutotv | broken_decision_pending |  | P1-04 |
| PlyrEmbed | plyrembed | no_key_specific_evidence |  | P1-03 |
| PodbayFM | podbayfm | no_key_specific_evidence |  | P1-03 |
| PodbayFMChannel | podbayfmchannel | no_key_specific_evidence |  | P1-03 |
| Podchaser | podchaser | no_key_specific_evidence |  | P1-03 |
| podomatic | podomatic | implemented_subset | PODOMATIC.md | P1-03 |
| PokerGo | pokergo | no_key_specific_evidence |  | P1-03 |
| PokerGoCollection | pokergocollection | no_key_specific_evidence |  | P1-03 |
| PolsatGo | polsatgo | no_key_specific_evidence |  | P1-03 |
| PolskieRadio | polskieradio | no_key_specific_evidence |  | P1-03 |
| polskieradio:audition | polskieradio | no_key_specific_evidence |  | P1-03 |
| polskieradio:category | polskieradio | no_key_specific_evidence |  | P1-03 |
| polskieradio:legacy | polskieradio | no_key_specific_evidence |  | P1-03 |
| polskieradio:player | polskieradio | no_key_specific_evidence |  | P1-03 |
| polskieradio:podcast | polskieradio | no_key_specific_evidence |  | P1-03 |
| polskieradio:​podcast:list | polskieradio | no_key_specific_evidence |  | P1-03 |
| Popcorntimes | popcorntimes | no_key_specific_evidence |  | P1-03 |
| PopcornTV | popcorntv | no_key_specific_evidence |  | P1-03 |
| Pornbox | pornbox | no_key_specific_evidence |  | P1-03 |
| PornerBros | pornerbros | no_key_specific_evidence |  | P1-03 |
| PornFlip | pornflip | no_key_specific_evidence |  | P1-03 |
| PornHub | pornhub | no_key_specific_evidence |  | P1-03 |
| PornHubPagedVideoList | pornhubpagedvideolist | no_key_specific_evidence |  | P1-03 |
| PornHubPlaylist | pornhubplaylist | no_key_specific_evidence |  | P1-03 |
| PornHubUser | pornhubuser | no_key_specific_evidence |  | P1-03 |
| PornHubUserVideosUpload | pornhubuservideosupload | no_key_specific_evidence |  | P1-03 |
| Pornotube | pornotube | no_key_specific_evidence |  | P1-03 |
| PornoVoisines | pornovoisines | broken_decision_pending |  | P1-04 |
| PornoXO | pornoxo | broken_decision_pending |  | P1-04 |
| PornTop | porntop | no_key_specific_evidence |  | P1-03 |
| PornTube | porntube | no_key_specific_evidence |  | P1-03 |
| Pr0gramm | pr0gramm | no_key_specific_evidence |  | P1-03 |
| PrankCast | prankcast | no_key_specific_evidence |  | P1-03 |
| PrankCastPost | prankcastpost | no_key_specific_evidence |  | P1-03 |
| PremiershipRugby | premiershiprugby | no_key_specific_evidence |  | P1-03 |
| PressTV | presstv | no_key_specific_evidence |  | P1-03 |
| ProjectVeritas | projectveritas | broken_decision_pending |  | P1-04 |
| PRXAccount | prxaccount | no_key_specific_evidence |  | P1-03 |
| PRXSeries | prxseries | no_key_specific_evidence |  | P1-03 |
| prxseries:search | prxseries | no_key_specific_evidence |  | P1-03 |
| prxstories:search | prxstories | no_key_specific_evidence |  | P1-03 |
| PRXStory | prxstory | no_key_specific_evidence |  | P1-03 |
| puhutv | puhutv | no_key_specific_evidence |  | P1-03 |
| puhutv:serie | puhutv | no_key_specific_evidence |  | P1-03 |
| Pyvideo | pyvideo | no_key_specific_evidence |  | P1-03 |
| QDance | qdance | no_key_specific_evidence |  | P1-03 |
| QingTing | qingting | no_key_specific_evidence |  | P1-03 |
| qqmusic | qqmusic | implemented_subset | QQ-MUSIC.md | P1-03 |
| qqmusic:album | qqmusic | implemented_subset | QQ-MUSIC.md | P1-03 |
| qqmusic:mv | qqmusic | implemented_subset | QQ-MUSIC.md | P1-03 |
| qqmusic:playlist | qqmusic | implemented_subset | QQ-MUSIC.md | P1-03 |
| qqmusic:singer | qqmusic | implemented_subset | QQ-MUSIC.md | P1-03 |
| qqmusic:toplist | qqmusic | implemented_subset | QQ-MUSIC.md | P1-03 |
| QuantumTV | quantumtv | no_key_specific_evidence |  | P1-03 |
| QuantumTVLive | quantumtvlive | no_key_specific_evidence |  | P0-03 |
| QuantumTVRecordings | quantumtvrecordings | no_key_specific_evidence |  | P1-03 |
| R7 | r7 | broken_decision_pending |  | P1-04 |
| R7Article | r7article | broken_decision_pending |  | P1-04 |
| Radiko | radiko | no_key_specific_evidence |  | P1-03 |
| RadikoRadio | radikoradio | no_key_specific_evidence |  | P1-03 |
| radio.de | radio.de | broken_decision_pending |  | P1-04 |
| Radio1Be | radio1be | no_key_specific_evidence |  | P1-03 |
| radiocanada | radiocanada | no_key_specific_evidence |  | P1-03 |
| radiocanada:audiovideo | radiocanada | no_key_specific_evidence |  | P1-03 |
| radiofrance | radiofrance | no_key_specific_evidence |  | P1-03 |
| RadioFranceLive | radiofrancelive | no_key_specific_evidence |  | P0-03 |
| RadioFrancePodcast | radiofrancepodcast | implemented_subset | RADIO-COMPLETION.md | P1-03 |
| RadioFranceProfile | radiofranceprofile | implemented_subset | CATALOG-SESSIONS.md | P1-03 |
| RadioFranceProgramSchedule | radiofranceprogramschedule | implemented_subset | CATALOG-SESSIONS.md | P1-03 |
| RadioJavan | radiojavan | broken_decision_pending |  | P1-04 |
| radiokapital | radiokapital | no_key_specific_evidence |  | P1-03 |
| radiokapital:show | radiokapital | no_key_specific_evidence |  | P1-03 |
| RadioRadicale | radioradicale | no_key_specific_evidence |  | P1-03 |
| RadioZetPodcast | radiozetpodcast | no_key_specific_evidence |  | P1-03 |
| radlive | radlive | no_key_specific_evidence |  | P0-03 |
| radlive:channel | radlive | no_key_specific_evidence |  | P0-03 |
| radlive:season | radlive | no_key_specific_evidence |  | P0-03 |
| Rai | rai | no_key_specific_evidence |  | P1-03 |
| RaiCultura | raicultura | no_key_specific_evidence |  | P1-03 |
| RaiNews | rainews | no_key_specific_evidence |  | P1-03 |
| RaiPlay | raiplay | no_key_specific_evidence |  | P1-03 |
| RaiPlayLive | raiplaylive | no_key_specific_evidence |  | P0-03 |
| RaiPlayPlaylist | raiplayplaylist | no_key_specific_evidence |  | P1-03 |
| RaiPlaySound | raiplaysound | no_key_specific_evidence |  | P1-03 |
| RaiPlaySoundLive | raiplaysoundlive | no_key_specific_evidence |  | P0-03 |
| RaiPlaySoundPlaylist | raiplaysoundplaylist | no_key_specific_evidence |  | P1-03 |
| RaiSudtirol | raisudtirol | no_key_specific_evidence |  | P1-03 |
| RayWenderlich | raywenderlich | no_key_specific_evidence |  | P1-03 |
| RayWenderlichCourse | raywenderlichcourse | no_key_specific_evidence |  | P1-03 |
| RbgTum | rbgtum | no_key_specific_evidence |  | P1-03 |
| RbgTumCourse | rbgtumcourse | no_key_specific_evidence |  | P1-03 |
| RbgTumNewCourse | rbgtumnewcourse | no_key_specific_evidence |  | P1-03 |
| RCS | rcs | no_key_specific_evidence |  | P1-03 |
| RCSEmbeds | rcsembeds | no_key_specific_evidence |  | P1-03 |
| RCSVarious | rcsvarious | no_key_specific_evidence |  | P1-03 |
| RCTIPlus | rctiplus | no_key_specific_evidence |  | P1-03 |
| RCTIPlusSeries | rctiplusseries | no_key_specific_evidence |  | P1-03 |
| RCTIPlusTV | rctiplustv | no_key_specific_evidence |  | P1-03 |
| RDS | rds | broken_decision_pending |  | P1-04 |
| RedBull | redbull | no_key_specific_evidence |  | P1-03 |
| RedBullEmbed | redbullembed | no_key_specific_evidence |  | P1-03 |
| RedBullTV | redbulltv | no_key_specific_evidence |  | P1-03 |
| RedBullTVRrnContent | redbulltvrrncontent | no_key_specific_evidence |  | P1-03 |
| Reddit | reddit | implemented_subset | REDDIT.md | P1-03 |
| RedGifs | redgifs | no_key_specific_evidence |  | P1-03 |
| RedGifsSearch | redgifssearch | no_key_specific_evidence |  | P1-03 |
| RedGifsUser | redgifsuser | no_key_specific_evidence |  | P1-03 |
| RedTube | redtube | no_key_specific_evidence |  | P1-03 |
| RENTV | rentv | broken_decision_pending |  | P1-04 |
| RENTVArticle | rentvarticle | broken_decision_pending |  | P1-04 |
| Restudy | restudy | broken_decision_pending |  | P1-04 |
| Reuters | reuters | broken_decision_pending |  | P1-04 |
| ReverbNation | reverbnation | implemented_subset | REVERBNATION.md | P1-03 |
| RideHome | ridehome | no_key_specific_evidence |  | P1-03 |
| RinseFM | rinsefm | no_key_specific_evidence |  | P1-03 |
| RinseFMArtistPlaylist | rinsefmartistplaylist | no_key_specific_evidence |  | P1-03 |
| RockstarGames | rockstargames | broken_decision_pending |  | P1-04 |
| Rokfin | rokfin | no_key_specific_evidence |  | P1-03 |
| rokfin:channel | rokfin | no_key_specific_evidence |  | P1-03 |
| rokfin:search | rokfin | no_key_specific_evidence |  | P1-03 |
| rokfin:stack | rokfin | no_key_specific_evidence |  | P1-03 |
| RoosterTeeth | roosterteeth | no_key_specific_evidence |  | P1-03 |
| RoosterTeethSeries | roosterteethseries | no_key_specific_evidence |  | P1-03 |
| RottenTomatoes | rottentomatoes | no_key_specific_evidence |  | P1-03 |
| RoyaLive | royalive | no_key_specific_evidence |  | P0-03 |
| Rozhlas | rozhlas | no_key_specific_evidence |  | P1-03 |
| RozhlasVltava | rozhlasvltava | no_key_specific_evidence |  | P1-03 |
| RTBF | rtbf | broken_decision_pending |  | P1-04 |
| RTDocumentry | rtdocumentry | no_key_specific_evidence |  | P1-03 |
| RTDocumentryPlaylist | rtdocumentryplaylist | no_key_specific_evidence |  | P1-03 |
| rte | rte | no_key_specific_evidence |  | P1-03 |
| rte:radio | rte | no_key_specific_evidence |  | P1-03 |
| rtl.lu:article | rtl.lu | no_key_specific_evidence |  | P1-03 |
| rtl.lu:tele-vod | rtl.lu | no_key_specific_evidence |  | P1-03 |
| rtl.nl | rtl.nl | no_key_specific_evidence |  | P1-03 |
| rtl2 | rtl2 | broken_decision_pending |  | P1-04 |
| RTLLuLive | rtllulive | no_key_specific_evidence |  | P0-03 |
| RTLLuRadio | rtlluradio | no_key_specific_evidence |  | P1-03 |
| RTNews | rtnews | no_key_specific_evidence |  | P1-03 |
| RTP | rtp | no_key_specific_evidence |  | P1-03 |
| RTRFM | rtrfm | no_key_specific_evidence |  | P1-03 |
| RTS | rts | broken_decision_pending |  | P1-04 |
| RTVCKaltura | rtvckaltura | no_key_specific_evidence |  | P1-01 |
| RTVCPlay | rtvcplay | no_key_specific_evidence |  | P1-03 |
| RTVCPlayEmbed | rtvcplayembed | no_key_specific_evidence |  | P1-03 |
| rtve.es:alacarta | rtve.es | no_key_specific_evidence |  | P1-03 |
| rtve.es:audio | rtve.es | implemented_subset | RTVE-AUDIO.md | P1-03 |
| rtve.es:live | rtve.es | no_key_specific_evidence |  | P0-03 |
| rtve.es:program | rtve.es | implemented_subset | RTVE-AUDIO.md | P1-03 |
| rtve.es:television | rtve.es | no_key_specific_evidence |  | P1-03 |
| rtvslo.si | rtvslo.si | no_key_specific_evidence |  | P1-03 |
| rtvslo.si:show | rtvslo.si | no_key_specific_evidence |  | P1-03 |
| RudoVideo | rudovideo | no_key_specific_evidence |  | P1-03 |
| Rule34Video | rule34video | no_key_specific_evidence |  | P1-03 |
| Rumble | rumble | implemented_subset | RUMBLE.md | P1-03 |
| RumbleChannel | rumblechannel | implemented_subset | RUMBLE.md | P1-03 |
| RumbleEmbed | rumbleembed | implemented_subset | RUMBLE.md | P1-03 |
| Ruptly | ruptly | no_key_specific_evidence |  | P1-03 |
| rutube | rutube | implemented_subset | RUTUBE.md | P1-03 |
| rutube:channel | rutube | implemented_subset | RUTUBE.md | P1-03 |
| rutube:embed | rutube | implemented_subset | RUTUBE.md | P1-03 |
| rutube:movie | rutube | implemented_subset | RUTUBE.md | P1-03 |
| rutube:person | rutube | implemented_subset | RUTUBE.md | P1-03 |
| rutube:playlist | rutube | implemented_subset | RUTUBE.md | P1-03 |
| rutube:tags | rutube | implemented_subset | RUTUBE.md | P1-03 |
| Ruutu | ruutu | broken_decision_pending |  | P1-04 |
| Ruv | ruv | no_key_specific_evidence |  | P1-03 |
| ruv.is:spila | ruv.is | no_key_specific_evidence |  | P1-03 |
| S4C | s4c | no_key_specific_evidence |  | P1-03 |
| S4CSeries | s4cseries | no_key_specific_evidence |  | P1-03 |
| safari | safari | no_key_specific_evidence |  | P1-03 |
| safari:api | safari | no_key_specific_evidence |  | P1-03 |
| safari:course | safari | no_key_specific_evidence |  | P1-03 |
| Saitosan | saitosan | broken_decision_pending |  | P1-04 |
| SAKTV | saktv | no_key_specific_evidence |  | P1-03 |
| SAKTVLive | saktvlive | no_key_specific_evidence |  | P0-03 |
| SAKTVRecordings | saktvrecordings | no_key_specific_evidence |  | P1-03 |
| SaltTV | salttv | no_key_specific_evidence |  | P1-03 |
| SaltTVLive | salttvlive | no_key_specific_evidence |  | P0-03 |
| SaltTVRecordings | salttvrecordings | no_key_specific_evidence |  | P1-03 |
| SampleFocus | samplefocus | no_key_specific_evidence |  | P1-03 |
| Sangiin | sangiin | no_key_specific_evidence |  | P1-03 |
| Sapo | sapo | no_key_specific_evidence |  | P1-03 |
| SaucePlus | sauceplus | no_key_specific_evidence |  | P1-03 |
| SaucePlusChannel | saucepluschannel | no_key_specific_evidence |  | P1-03 |
| SBS | sbs | no_key_specific_evidence |  | P1-03 |
| sbs.co.kr | sbs.co.kr | no_key_specific_evidence |  | P1-03 |
| sbs.co.kr:allvod_program | sbs.co.kr | no_key_specific_evidence |  | P1-03 |
| sbs.co.kr:programs_vod | sbs.co.kr | no_key_specific_evidence |  | P1-03 |
| schooltv | schooltv | no_key_specific_evidence |  | P1-03 |
| ScienceChannel | sciencechannel | no_key_specific_evidence |  | P1-03 |
| Screen9 | screen9 | no_key_specific_evidence |  | P1-03 |
| Screencast | screencast | no_key_specific_evidence |  | P1-03 |
| Screencastify | screencastify | no_key_specific_evidence |  | P1-03 |
| ScreencastOMatic | screencastomatic | no_key_specific_evidence |  | P1-03 |
| ScreenRec | screenrec | no_key_specific_evidence |  | P1-03 |
| ScrippsNetworks | scrippsnetworks | no_key_specific_evidence |  | P1-03 |
| scrippsnetworks:watch | scrippsnetworks | no_key_specific_evidence |  | P1-03 |
| Scrolller | scrolller | no_key_specific_evidence |  | P1-03 |
| sejm | sejm | broken_decision_pending |  | P1-04 |
| Sen | sen | no_key_specific_evidence |  | P1-03 |
| SenalColombiaLive | senalcolombialive | broken_decision_pending |  | P1-04 |
| senate.gov | senate.gov | no_key_specific_evidence |  | P1-03 |
| senate.gov:isvp | senate.gov | no_key_specific_evidence |  | P1-03 |
| Servus | servus | no_key_specific_evidence |  | P1-03 |
| Sexu | sexu | broken_decision_pending |  | P1-04 |
| SeznamZpravy | seznamzpravy | no_key_specific_evidence |  | P1-03 |
| SeznamZpravyArticle | seznamzpravyarticle | no_key_specific_evidence |  | P1-03 |
| Shahid | shahid | no_key_specific_evidence |  | P1-03 |
| ShahidShow | shahidshow | no_key_specific_evidence |  | P1-03 |
| SharePoint | sharepoint | no_key_specific_evidence |  | P1-03 |
| ShemarooMe | shemaroome | no_key_specific_evidence |  | P1-03 |
| Shiey | shiey | no_key_specific_evidence |  | P1-03 |
| showroom:live | showroom | no_key_specific_evidence |  | P0-03 |
| showroom:vod | showroom | no_key_specific_evidence |  | P1-03 |
| ShugiinItvLive | shugiinitvlive | no_key_specific_evidence |  | P0-03 |
| ShugiinItvLiveRoom | shugiinitvliveroom | no_key_specific_evidence |  | P0-03 |
| ShugiinItvVod | shugiinitvvod | no_key_specific_evidence |  | P1-03 |
| SibnetEmbed | sibnetembed | no_key_specific_evidence |  | P1-03 |
| simplecast | simplecast | implemented_subset | SIMPLECAST.md | P1-03 |
| simplecast:episode | simplecast | implemented_subset | SIMPLECAST.md | P1-03 |
| simplecast:podcast | simplecast | implemented_subset | SIMPLECAST.md | P1-03 |
| Sina | sina | no_key_specific_evidence |  | P1-03 |
| Skeb | skeb | no_key_specific_evidence |  | P1-03 |
| sky.it | sky.it | no_key_specific_evidence |  | P1-03 |
| sky:news | sky | no_key_specific_evidence |  | P1-03 |
| sky:​news:story | sky | no_key_specific_evidence |  | P1-03 |
| sky:sports | sky | no_key_specific_evidence |  | P1-03 |
| sky:​sports:news | sky | no_key_specific_evidence |  | P1-03 |
| SkylineWebcams | skylinewebcams | broken_decision_pending |  | P1-04 |
| skynewsarabia:article | skynewsarabia | broken_decision_pending |  | P1-04 |
| skynewsarabia:video | skynewsarabia | broken_decision_pending |  | P1-04 |
| SkyNewsAU | skynewsau | no_key_specific_evidence |  | P1-03 |
| Slideshare | slideshare | no_key_specific_evidence |  | P1-03 |
| SlidesLive | slideslive | no_key_specific_evidence |  | P0-03 |
| Slutload | slutload | no_key_specific_evidence |  | P1-03 |
| smotrim | smotrim | no_key_specific_evidence |  | P1-03 |
| smotrim:audio | smotrim | no_key_specific_evidence |  | P1-03 |
| smotrim:live | smotrim | no_key_specific_evidence |  | P0-03 |
| smotrim:playlist | smotrim | no_key_specific_evidence |  | P1-03 |
| SnapchatSpotlight | snapchatspotlight | no_key_specific_evidence |  | P1-03 |
| SoftWhiteUnderbelly | softwhiteunderbelly | no_key_specific_evidence |  | P1-03 |
| Sohu | sohu | no_key_specific_evidence |  | P1-03 |
| SohuV | sohuv | no_key_specific_evidence |  | P1-03 |
| SonyLIV | sonyliv | no_key_specific_evidence |  | P1-03 |
| SonyLIVSeries | sonylivseries | no_key_specific_evidence |  | P1-03 |
| soop | soop | no_key_specific_evidence |  | P1-03 |
| soop:catchstory | soop | no_key_specific_evidence |  | P1-03 |
| soop:live | soop | no_key_specific_evidence |  | P0-03 |
| soop:user | soop | no_key_specific_evidence |  | P1-03 |
| soundcloud | soundcloud | implemented_subset | SOUNDCLOUD.md | P1-03 |
| soundcloud:playlist | soundcloud | implemented_subset | SOUNDCLOUD.md | P1-03 |
| soundcloud:related | soundcloud | implemented_subset | AUDIO-INVENTORIES.md | P1-03 |
| soundcloud:search | soundcloud | implemented_subset | AUDIO-INVENTORIES.md | P1-03 |
| soundcloud:set | soundcloud | implemented_subset | SOUNDCLOUD.md | P1-03 |
| soundcloud:trackstation | soundcloud | no_key_specific_evidence |  | P1-03 |
| soundcloud:user | soundcloud | implemented_subset | AUDIO-INVENTORIES.md | P1-03 |
| soundcloud:​user:permalink | soundcloud | implemented_subset | AUDIO-INVENTORIES.md | P1-03 |
| SoundcloudEmbed | soundcloudembed | implemented_subset | SOUNDCLOUD.md | P1-03 |
| soundgasm | soundgasm | no_key_specific_evidence |  | P1-03 |
| soundgasm:profile | soundgasm | no_key_specific_evidence |  | P1-03 |
| southpark.cc.com | southpark.cc.com | no_key_specific_evidence |  | P1-03 |
| southpark.cc.com:español | southpark.cc.com | no_key_specific_evidence |  | P1-03 |
| southpark.de | southpark.de | no_key_specific_evidence |  | P1-03 |
| southpark.lat | southpark.lat | no_key_specific_evidence |  | P1-03 |
| southparkstudios.co.uk | southparkstudios.co.uk | no_key_specific_evidence |  | P1-03 |
| southparkstudios.com.br | southparkstudios.com.br | no_key_specific_evidence |  | P1-03 |
| southparkstudios.nu | southparkstudios.nu | no_key_specific_evidence |  | P1-03 |
| SovietsCloset | sovietscloset | no_key_specific_evidence |  | P1-03 |
| SovietsClosetPlaylist | sovietsclosetplaylist | no_key_specific_evidence |  | P1-03 |
| SpankBang | spankbang | no_key_specific_evidence |  | P1-03 |
| SpankBangPlaylist | spankbangplaylist | no_key_specific_evidence |  | P1-03 |
| Spiegel | spiegel | no_key_specific_evidence |  | P1-03 |
| Sport5 | sport5 | no_key_specific_evidence |  | P1-03 |
| SportBox | sportbox | broken_decision_pending |  | P1-04 |
| sporteurope | sporteurope | no_key_specific_evidence |  | P1-03 |
| Spreaker | spreaker | implemented_subset | SPREAKER.md | P1-03 |
| SpreakerShow | spreakershow | implemented_subset | SPREAKER.md | P1-03 |
| SproutVideo | sproutvideo | no_key_specific_evidence |  | P1-03 |
| sr:mediathek | sr | no_key_specific_evidence |  | P1-03 |
| SRGSSR | srgssr | no_key_specific_evidence |  | P1-03 |
| SRGSSRPlay | srgssrplay | no_key_specific_evidence |  | P1-03 |
| StacommuLive | stacommulive | no_key_specific_evidence |  | P0-03 |
| StacommuVOD | stacommuvod | no_key_specific_evidence |  | P1-03 |
| StagePlusVODConcert | stageplusvodconcert | no_key_specific_evidence |  | P1-03 |
| startrek | startrek | no_key_specific_evidence |  | P1-03 |
| startv | startv | no_key_specific_evidence |  | P1-03 |
| Steam | steam | no_key_specific_evidence |  | P1-03 |
| SteamCommunity | steamcommunity | no_key_specific_evidence |  | P1-03 |
| SteamCommunityBroadcast | steamcommunitybroadcast | no_key_specific_evidence |  | P0-03 |
| StoryFire | storyfire | no_key_specific_evidence |  | P1-03 |
| StoryFireSeries | storyfireseries | no_key_specific_evidence |  | P1-03 |
| StoryFireUser | storyfireuser | no_key_specific_evidence |  | P1-03 |
| Streaks | streaks | no_key_specific_evidence |  | P1-03 |
| Streamable | streamable | implemented_subset | STREAMABLE.md | P0-03 |
| StreamCZ | streamcz | no_key_specific_evidence |  | P0-03 |
| StreetVoice | streetvoice | no_key_specific_evidence |  | P1-03 |
| Stripchat | stripchat | no_key_specific_evidence |  | P1-03 |
| stv:player | stv | no_key_specific_evidence |  | P1-03 |
| stvr | stvr | no_key_specific_evidence |  | P1-03 |
| Subsplash | subsplash | no_key_specific_evidence |  | P1-03 |
| subsplash:playlist | subsplash | no_key_specific_evidence |  | P1-03 |
| Substack | substack | no_key_specific_evidence |  | P1-03 |
| SunPorno | sunporno | no_key_specific_evidence |  | P1-03 |
| sverigesradio:episode | sverigesradio | no_key_specific_evidence |  | P1-03 |
| sverigesradio:publication | sverigesradio | no_key_specific_evidence |  | P1-03 |
| svt:page | svt | no_key_specific_evidence |  | P1-03 |
| svt:play | svt | implemented_subset | GLOBAL-BROADCAST.md | P1-03 |
| svt:​play:series | svt | no_key_specific_evidence |  | P1-03 |
| Syfy | syfy | no_key_specific_evidence |  | P1-03 |
| SztvHu | sztvhu | no_key_specific_evidence |  | P1-03 |
| t-online.de | t-online.de | broken_decision_pending |  | P1-04 |
| Tagesschau | tagesschau | broken_decision_pending |  | P1-04 |
| TapTapApp | taptapapp | no_key_specific_evidence |  | P1-03 |
| TapTapAppIntl | taptapappintl | no_key_specific_evidence |  | P1-03 |
| TapTapMoment | taptapmoment | no_key_specific_evidence |  | P1-03 |
| TapTapPostIntl | taptappostintl | no_key_specific_evidence |  | P1-03 |
| tarangplus:episodes | tarangplus | no_key_specific_evidence |  | P1-03 |
| tarangplus:playlist | tarangplus | no_key_specific_evidence |  | P1-03 |
| tarangplus:video | tarangplus | no_key_specific_evidence |  | P1-03 |
| Tass | tass | broken_decision_pending |  | P1-04 |
| TBS | tbs | no_key_specific_evidence |  | P1-03 |
| TBSJPEpisode | tbsjpepisode | no_key_specific_evidence |  | P1-03 |
| TBSJPPlaylist | tbsjpplaylist | no_key_specific_evidence |  | P1-03 |
| TBSJPProgram | tbsjpprogram | no_key_specific_evidence |  | P1-03 |
| Teachable | teachable | broken_decision_pending |  | P1-04 |
| TeachableCourse | teachablecourse | no_key_specific_evidence |  | P1-03 |
| teachertube | teachertube | broken_decision_pending |  | P1-04 |
| teachertube:​user:collection | teachertube | broken_decision_pending |  | P1-04 |
| TeachingChannel | teachingchannel | broken_decision_pending |  | P1-04 |
| Teamcoco | teamcoco | no_key_specific_evidence |  | P1-03 |
| TeamTreeHouse | teamtreehouse | no_key_specific_evidence |  | P1-03 |
| techtv.mit.edu | techtv.mit.edu | no_key_specific_evidence |  | P1-03 |
| TedEmbed | tedembed | implemented_subset | TED.md | P1-03 |
| TedPlaylist | tedplaylist | implemented_subset | TED.md | P1-03 |
| TedSeries | tedseries | implemented_subset | TED.md | P1-03 |
| TedTalk | tedtalk | implemented_subset | TED.md | P1-03 |
| Tele13 | tele13 | no_key_specific_evidence |  | P1-03 |
| Tele5 | tele5 | no_key_specific_evidence |  | P1-03 |
| TeleBruxelles | telebruxelles | no_key_specific_evidence |  | P1-03 |
| TelecaribePlay | telecaribeplay | no_key_specific_evidence |  | P1-03 |
| Telecinco | telecinco | no_key_specific_evidence |  | P1-03 |
| Telegraaf | telegraaf | no_key_specific_evidence |  | P1-03 |
| telegram:embed | telegram | no_key_specific_evidence |  | P1-03 |
| TeleMB | telemb | broken_decision_pending |  | P1-04 |
| Telemundo | telemundo | broken_decision_pending |  | P1-04 |
| TeleQuebec | telequebec | no_key_specific_evidence |  | P1-03 |
| TeleQuebecEmission | telequebecemission | no_key_specific_evidence |  | P1-03 |
| TeleQuebecLive | telequebeclive | no_key_specific_evidence |  | P0-03 |
| TeleQuebecSquat | telequebecsquat | no_key_specific_evidence |  | P1-03 |
| TeleQuebecVideo | telequebecvideo | no_key_specific_evidence |  | P1-03 |
| TeleTask | teletask | broken_decision_pending |  | P1-04 |
| Telewebion | telewebion | no_key_specific_evidence |  | P1-03 |
| TennisTV | tennistv | no_key_specific_evidence |  | P1-03 |
| TF1 | tf1 | no_key_specific_evidence |  | P1-03 |
| TFO | tfo | broken_decision_pending |  | P1-04 |
| theatercomplextown:ppv | theatercomplextown | no_key_specific_evidence |  | P1-03 |
| theatercomplextown:vod | theatercomplextown | no_key_specific_evidence |  | P1-03 |
| TheChosen | thechosen | no_key_specific_evidence |  | P1-03 |
| TheChosenGroup | thechosengroup | broken_decision_pending |  | P1-04 |
| TheGuardianPodcast | theguardianpodcast | no_key_specific_evidence |  | P1-03 |
| TheGuardianPodcastPlaylist | theguardianpodcastplaylist | no_key_specific_evidence |  | P1-03 |
| TheHighWire | thehighwire | no_key_specific_evidence |  | P1-03 |
| TheIntercept | theintercept | no_key_specific_evidence |  | P1-03 |
| ThePlatform | theplatform | no_key_specific_evidence |  | P1-03 |
| ThePlatformFeed | theplatformfeed | no_key_specific_evidence |  | P1-03 |
| TheStar | thestar | no_key_specific_evidence |  | P1-03 |
| TheSun | thesun | no_key_specific_evidence |  | P1-03 |
| TheWeatherChannel | theweatherchannel | no_key_specific_evidence |  | P1-03 |
| ThisAmericanLife | thisamericanlife | no_key_specific_evidence |  | P1-03 |
| ThisOldHouse | thisoldhouse | no_key_specific_evidence |  | P1-03 |
| ThisVid | thisvid | no_key_specific_evidence |  | P1-03 |
| ThisVidMember | thisvidmember | no_key_specific_evidence |  | P1-03 |
| ThisVidPlaylist | thisvidplaylist | no_key_specific_evidence |  | P1-03 |
| ThreeSpeak | threespeak | no_key_specific_evidence |  | P1-03 |
| ThreeSpeakUser | threespeakuser | no_key_specific_evidence |  | P1-03 |
| TikTok | tiktok | implemented_subset | TIKTOK.md | P1-03 |
| tiktok:collection | tiktok | no_key_specific_evidence |  | P1-03 |
| tiktok:effect | tiktok | broken_decision_pending |  | P1-04 |
| tiktok:live | tiktok | no_key_specific_evidence |  | P0-03 |
| tiktok:sound | tiktok | broken_decision_pending |  | P1-04 |
| tiktok:tag | tiktok | broken_decision_pending |  | P1-04 |
| tiktok:user | tiktok | no_key_specific_evidence |  | P1-03 |
| TLC | tlc | no_key_specific_evidence |  | P1-03 |
| TMZ | tmz | no_key_specific_evidence |  | P1-03 |
| TNAFlix | tnaflix | no_key_specific_evidence |  | P1-03 |
| TNAFlixNetworkEmbed | tnaflixnetworkembed | no_key_specific_evidence |  | P1-03 |
| toggle | toggle | no_key_specific_evidence |  | P1-03 |
| toggo | toggo | no_key_specific_evidence |  | P1-03 |
| tokfm:audition | tokfm | no_key_specific_evidence |  | P1-03 |
| tokfm:podcast | tokfm | no_key_specific_evidence |  | P1-03 |
| ToonGoggles | toongoggles | no_key_specific_evidence |  | P1-03 |
| tou.tv | tou.tv | no_key_specific_evidence |  | P1-03 |
| toutiao | toutiao | no_key_specific_evidence |  | P1-03 |
| Toypics | toypics | broken_decision_pending |  | P1-04 |
| ToypicsUser | toypicsuser | broken_decision_pending |  | P1-04 |
| TravelChannel | travelchannel | no_key_specific_evidence |  | P1-03 |
| TrtCocukVideo | trtcocukvideo | no_key_specific_evidence |  | P1-03 |
| TrtWorld | trtworld | no_key_specific_evidence |  | P1-03 |
| TrueID | trueid | no_key_specific_evidence |  | P1-03 |
| TruNews | trunews | no_key_specific_evidence |  | P1-03 |
| Truth | truth | no_key_specific_evidence |  | P1-03 |
| ttinglive | ttinglive | no_key_specific_evidence |  | P0-03 |
| Tube8 | tube8 | broken_decision_pending |  | P1-04 |
| TubeTuGraz | tubetugraz | no_key_specific_evidence |  | P1-03 |
| TubeTuGrazSeries | tubetugrazseries | no_key_specific_evidence |  | P1-03 |
| tubitv | tubitv | no_key_specific_evidence |  | P1-03 |
| tubitv:series | tubitv | no_key_specific_evidence |  | P1-03 |
| Tumblr | tumblr | implemented_subset | TUMBLR.md | P1-03 |
| tunein:embed | tunein | no_key_specific_evidence |  | P1-03 |
| tunein:podcast | tunein | no_key_specific_evidence |  | P1-03 |
| tunein:​podcast:program | tunein | no_key_specific_evidence |  | P1-03 |
| tunein:station | tunein | no_key_specific_evidence |  | P1-03 |
| tv.dfb.de | tv.dfb.de | no_key_specific_evidence |  | P1-03 |
| TV2 | tv2 | no_key_specific_evidence |  | P1-03 |
| TV2Article | tv2article | no_key_specific_evidence |  | P1-03 |
| TV2DK | tv2dk | no_key_specific_evidence |  | P1-03 |
| TV2DKBornholmPlay | tv2dkbornholmplay | no_key_specific_evidence |  | P1-03 |
| tv2play.hu | tv2play.hu | no_key_specific_evidence |  | P1-03 |
| tv2playseries.hu | tv2playseries.hu | no_key_specific_evidence |  | P1-03 |
| TV4 | tv4 | no_key_specific_evidence |  | P1-03 |
| TV5MONDE | tv5monde | no_key_specific_evidence |  | P1-03 |
| tv5unis | tv5unis | no_key_specific_evidence |  | P1-03 |
| tv5unis:video | tv5unis | no_key_specific_evidence |  | P1-03 |
| tv8.it | tv8.it | no_key_specific_evidence |  | P1-03 |
| tv8.it:live | tv8.it | no_key_specific_evidence |  | P0-03 |
| tv8.it:playlist | tv8.it | no_key_specific_evidence |  | P1-03 |
| TVANouvelles | tvanouvelles | no_key_specific_evidence |  | P1-03 |
| TVANouvellesArticle | tvanouvellesarticle | no_key_specific_evidence |  | P1-03 |
| tvaplus | tvaplus | no_key_specific_evidence |  | P1-03 |
| TVC | tvc | no_key_specific_evidence |  | P1-03 |
| TVCArticle | tvcarticle | no_key_specific_evidence |  | P1-03 |
| TVer | tver | no_key_specific_evidence |  | P1-03 |
| tver:olympic | tver | no_key_specific_evidence |  | P1-03 |
| tvigle | tvigle | no_key_specific_evidence |  | P1-03 |
| TVIPlayer | tviplayer | no_key_specific_evidence |  | P1-03 |
| TVN24 | tvn24 | broken_decision_pending |  | P1-04 |
| tvnoe | tvnoe | no_key_specific_evidence |  | P1-03 |
| TVO | tvo | no_key_specific_evidence |  | P1-03 |
| tvopengr:embed | tvopengr | no_key_specific_evidence |  | P1-03 |
| tvopengr:watch | tvopengr | no_key_specific_evidence |  | P1-03 |
| tvp | tvp | no_key_specific_evidence |  | P1-03 |
| tvp:embed | tvp | no_key_specific_evidence |  | P1-03 |
| tvp:stream | tvp | no_key_specific_evidence |  | P0-03 |
| tvp:vod | tvp | implemented_subset | PROVIDER-FAMILIES.md | P1-03 |
| tvp:​vod:series | tvp | implemented_subset | PROVIDER-FAMILIES.md | P1-03 |
| TVPlayHome | tvplayhome | no_key_specific_evidence |  | P1-03 |
| tvw | tvw | no_key_specific_evidence |  | P1-03 |
| tvw:news | tvw | no_key_specific_evidence |  | P1-03 |
| tvw:tvchannels | tvw | no_key_specific_evidence |  | P1-03 |
| Tweakers | tweakers | no_key_specific_evidence |  | P1-03 |
| TwitCasting | twitcasting | no_key_specific_evidence |  | P1-03 |
| TwitCastingLive | twitcastinglive | no_key_specific_evidence |  | P0-03 |
| TwitCastingUser | twitcastinguser | no_key_specific_evidence |  | P1-03 |
| twitch:clips | twitch | implemented_subset | TWITCH.md | P1-03 |
| twitch:collection | twitch | no_key_specific_evidence |  | P1-03 |
| twitch:stream | twitch | no_key_specific_evidence |  | P0-03 |
| twitch:videos | twitch | no_key_specific_evidence |  | P1-03 |
| twitch:​videos:clips | twitch | no_key_specific_evidence |  | P1-03 |
| twitch:​videos:collections | twitch | no_key_specific_evidence |  | P1-03 |
| twitch:vod | twitch | no_key_specific_evidence |  | P1-03 |
| twitter | twitter | implemented_subset | TWITTER.md | P1-03 |
| twitter:amplify | twitter | no_key_specific_evidence |  | P1-03 |
| twitter:broadcast | twitter | no_key_specific_evidence |  | P0-03 |
| twitter:card | twitter | no_key_specific_evidence |  | P1-03 |
| twitter:shortener | twitter | no_key_specific_evidence |  | P1-03 |
| twitter:spaces | twitter | no_key_specific_evidence |  | P1-03 |
| Txxx | txxx | no_key_specific_evidence |  | P1-03 |
| udemy | udemy | no_key_specific_evidence |  | P1-03 |
| udemy:course | udemy | no_key_specific_evidence |  | P1-03 |
| UDNEmbed | udnembed | no_key_specific_evidence |  | P1-03 |
| UFCTV | ufctv | no_key_specific_evidence |  | P1-03 |
| ukcolumn | ukcolumn | broken_decision_pending |  | P1-04 |
| UlizaPlayer | ulizaplayer | no_key_specific_evidence |  | P1-03 |
| UlizaPortal | ulizaportal | no_key_specific_evidence |  | P1-03 |
| umg:de | umg | no_key_specific_evidence |  | P1-03 |
| Unistra | unistra | no_key_specific_evidence |  | P1-03 |
| UnitedNationsWebTv | unitednationswebtv | no_key_specific_evidence |  | P1-03 |
| Unity | unity | broken_decision_pending |  | P1-04 |
| uol.com.br | uol.com.br | no_key_specific_evidence |  | P1-03 |
| uplynk | uplynk | no_key_specific_evidence |  | P1-03 |
| uplynk:preplay | uplynk | no_key_specific_evidence |  | P1-03 |
| Urort | urort | broken_decision_pending |  | P1-04 |
| URPlay | urplay | no_key_specific_evidence |  | P1-03 |
| USANetwork | usanetwork | no_key_specific_evidence |  | P1-03 |
| USAToday | usatoday | no_key_specific_evidence |  | P1-03 |
| ustream | ustream | no_key_specific_evidence |  | P0-03 |
| ustream:channel | ustream | no_key_specific_evidence |  | P0-03 |
| ustudio | ustudio | no_key_specific_evidence |  | P1-03 |
| ustudio:embed | ustudio | no_key_specific_evidence |  | P1-03 |
| Varzesh3 | varzesh3 | broken_decision_pending |  | P1-04 |
| Vbox7 | vbox7 | no_key_specific_evidence |  | P1-03 |
| Veo | veo | no_key_specific_evidence |  | P1-03 |
| Vevo | vevo | no_key_specific_evidence |  | P1-03 |
| VevoPlaylist | vevoplaylist | no_key_specific_evidence |  | P1-03 |
| VGTV | vgtv | broken_decision_pending |  | P1-04 |
| vh1.com | vh1.com | no_key_specific_evidence |  | P1-03 |
| vhx:embed | vhx | no_key_specific_evidence |  | P1-03 |
| vice | vice | broken_decision_pending |  | P1-04 |
| vice:article | vice | broken_decision_pending |  | P1-04 |
| vice:show | vice | broken_decision_pending |  | P1-04 |
| Viddler | viddler | broken_decision_pending |  | P1-04 |
| Videa | videa | no_key_specific_evidence |  | P1-03 |
| video.arnes.si | video.arnes.si | no_key_specific_evidence |  | P1-03 |
| video.google:search | video.google | no_key_specific_evidence |  | P1-03 |
| video.sky.it | video.sky.it | no_key_specific_evidence |  | P1-03 |
| video.sky.it:live | video.sky.it | no_key_specific_evidence |  | P0-03 |
| VideoKenPlayer | videokenplayer | no_key_specific_evidence |  | P1-03 |
| VideoPress | videopress | no_key_specific_evidence |  | P1-03 |
| Vidflex | vidflex | no_key_specific_evidence |  | P1-03 |
| Vidio | vidio | no_key_specific_evidence |  | P1-03 |
| VidioLive | vidiolive | no_key_specific_evidence |  | P0-03 |
| VidioPremier | vidiopremier | no_key_specific_evidence |  | P1-03 |
| VidLii | vidlii | no_key_specific_evidence |  | P1-03 |
| Vidly | vidly | no_key_specific_evidence |  | P1-03 |
| vids.io | vids.io | no_key_specific_evidence |  | P1-03 |
| Vidyard | vidyard | implemented_subset | VIDYARD.md | P1-03 |
| viewlift | viewlift | no_key_specific_evidence |  | P1-03 |
| viewlift:embed | viewlift | no_key_specific_evidence |  | P1-03 |
| Viidea | viidea | no_key_specific_evidence |  | P1-03 |
| vimeo | vimeo | implemented_subset | VIMEO.mdMIXED-COLLECTIONS.mdADAPTIVE-RECOVERY.mdWRITE-CLEANUP-RECOVERY.mdCHANGED-JOB-SESSIONS.md | P0-04 |
| vimeo:album | vimeo | implemented_subset | CORE-PLATFORM-INVENTORIES.md | P0-04 |
| vimeo:channel | vimeo | implemented_subset | CORE-PLATFORM-INVENTORIES.md | P0-04 |
| vimeo:event | vimeo | no_key_specific_evidence |  | P0-04 |
| vimeo:group | vimeo | implemented_subset | CORE-PLATFORM-INVENTORIES.md | P0-04 |
| vimeo:likes | vimeo | implemented_subset | CORE-PLATFORM-INVENTORIES.md | P0-04 |
| vimeo:ondemand | vimeo | no_key_specific_evidence |  | P0-04 |
| vimeo:pro | vimeo | no_key_specific_evidence |  | P0-04 |
| vimeo:review | vimeo | no_key_specific_evidence |  | P0-04 |
| vimeo:user | vimeo | implemented_subset | CORE-PLATFORM-INVENTORIES.md | P0-04 |
| vimeo:watchlater | vimeo | implemented_subset | CORE-PLATFORM-INVENTORIES.md | P0-04 |
| ViMP | vimp | no_key_specific_evidence |  | P1-03 |
| ViMP:Playlist | vimp | no_key_specific_evidence |  | P1-03 |
| Viously | viously | no_key_specific_evidence |  | P1-03 |
| Viqeo | viqeo | broken_decision_pending |  | P1-04 |
| Visir | visir | no_key_specific_evidence |  | P1-03 |
| Viu | viu | no_key_specific_evidence |  | P1-03 |
| viu:ott | viu | no_key_specific_evidence |  | P1-03 |
| viu:playlist | viu | no_key_specific_evidence |  | P1-03 |
| ViuOTTIndonesia | viuottindonesia | no_key_specific_evidence |  | P1-03 |
| vk | vk | no_key_specific_evidence |  | P1-03 |
| vk:uservideos | vk | no_key_specific_evidence |  | P1-03 |
| vk:wallpost | vk | no_key_specific_evidence |  | P1-03 |
| VKPlay | vkplay | no_key_specific_evidence |  | P1-03 |
| VKPlayLive | vkplaylive | no_key_specific_evidence |  | P0-03 |
| vm.tiktok | vm.tiktok | no_key_specific_evidence |  | P1-03 |
| Vocaroo | vocaroo | implemented_subset | VOCAROO.md | P1-03 |
| VODPlatform | vodplatform | no_key_specific_evidence |  | P1-03 |
| voicy | voicy | broken_decision_pending |  | P1-04 |
| voicy:channel | voicy | broken_decision_pending |  | P1-04 |
| volejtv:category | volejtv | no_key_specific_evidence |  | P1-03 |
| volejtv:club | volejtv | no_key_specific_evidence |  | P1-03 |
| volejtv:match | volejtv | no_key_specific_evidence |  | P1-03 |
| VoxMedia | voxmedia | no_key_specific_evidence |  | P1-03 |
| VoxMediaVolume | voxmediavolume | no_key_specific_evidence |  | P1-03 |
| vpro | vpro | no_key_specific_evidence |  | P1-03 |
| vqq:series | vqq | no_key_specific_evidence |  | P1-03 |
| vqq:video | vqq | no_key_specific_evidence |  | P1-03 |
| vrsquare | vrsquare | no_key_specific_evidence |  | P1-03 |
| vrsquare:channel | vrsquare | no_key_specific_evidence |  | P1-03 |
| vrsquare:search | vrsquare | no_key_specific_evidence |  | P1-03 |
| vrsquare:section | vrsquare | no_key_specific_evidence |  | P1-03 |
| VRT | vrt | no_key_specific_evidence |  | P1-03 |
| vrtmax | vrtmax | no_key_specific_evidence |  | P1-03 |
| VTM | vtm | no_key_specific_evidence |  | P1-03 |
| VTV | vtv | no_key_specific_evidence |  | P1-03 |
| VTVGo | vtvgo | no_key_specific_evidence |  | P1-03 |
| VTXTV | vtxtv | no_key_specific_evidence |  | P1-03 |
| VTXTVLive | vtxtvlive | no_key_specific_evidence |  | P0-03 |
| VTXTVRecordings | vtxtvrecordings | no_key_specific_evidence |  | P1-03 |
| Walla | walla | broken_decision_pending |  | P1-04 |
| WalyTV | walytv | no_key_specific_evidence |  | P1-03 |
| WalyTVLive | walytvlive | no_key_specific_evidence |  | P0-03 |
| WalyTVRecordings | walytvrecordings | no_key_specific_evidence |  | P1-03 |
| washingtonpost | washingtonpost | no_key_specific_evidence |  | P1-03 |
| washingtonpost:article | washingtonpost | no_key_specific_evidence |  | P1-03 |
| wat.tv | wat.tv | no_key_specific_evidence |  | P1-03 |
| WatchESPN | watchespn | no_key_specific_evidence |  | P1-03 |
| WDR | wdr | no_key_specific_evidence |  | P1-03 |
| WDRElefant | wdrelefant | no_key_specific_evidence |  | P1-03 |
| WDRPage | wdrpage | no_key_specific_evidence |  | P1-03 |
| web.archive:youtube | web.archive | no_key_specific_evidence |  | P1-03 |
| Webcamerapl | webcamerapl | no_key_specific_evidence |  | P1-03 |
| Webcaster | webcaster | no_key_specific_evidence |  | P1-03 |
| WebcasterFeed | webcasterfeed | no_key_specific_evidence |  | P1-03 |
| WebOfStories | webofstories | no_key_specific_evidence |  | P1-03 |
| WebOfStoriesPlaylist | webofstoriesplaylist | no_key_specific_evidence |  | P1-03 |
| Weibo | weibo | no_key_specific_evidence |  | P1-03 |
| WeiboUser | weibouser | no_key_specific_evidence |  | P1-03 |
| WeiboVideo | weibovideo | no_key_specific_evidence |  | P1-03 |
| WeiqiTV | weiqitv | broken_decision_pending |  | P1-04 |
| wetv:episode | wetv | no_key_specific_evidence |  | P1-03 |
| WeTvSeries | wetvseries | no_key_specific_evidence |  | P1-03 |
| Weverse | weverse | no_key_specific_evidence |  | P1-03 |
| WeverseLive | weverselive | no_key_specific_evidence |  | P0-03 |
| WeverseLiveTab | weverselivetab | no_key_specific_evidence |  | P0-03 |
| WeverseMedia | weversemedia | no_key_specific_evidence |  | P1-03 |
| WeverseMediaTab | weversemediatab | no_key_specific_evidence |  | P1-03 |
| WeverseMoment | weversemoment | no_key_specific_evidence |  | P1-03 |
| WeVidi | wevidi | no_key_specific_evidence |  | P1-03 |
| whowatch | whowatch | broken_decision_pending |  | P1-04 |
| Whyp | whyp | implemented_subset | WHYP.md | P1-03 |
| wikimedia.org | wikimedia.org | implemented_subset | WIKIMEDIA.md | P1-03 |
| Wimbledon | wimbledon | no_key_specific_evidence |  | P1-03 |
| WimTV | wimtv | no_key_specific_evidence |  | P1-03 |
| WinSportsVideo | winsportsvideo | no_key_specific_evidence |  | P1-03 |
| Wistia | wistia | implemented_subset | HOSTING.mdMIXED-COLLECTIONS.mdPUBLICATION-SESSION-RECOVERY.mdWRITE-CLEANUP-RECOVERY.mdCHANGED-JOB-SESSIONS.mdNO-OVERWRITE-TRANSFERS.mdADAPTIVE-NO-OVERWRITE.mdLIVE-OUTPUT-INVENTORIES.md | P1-01 |
| WistiaChannel | wistiachannel | implemented_subset | HOSTING.mdMIXED-COLLECTIONS.md | P1-01 |
| WistiaPlaylist | wistiaplaylist | implemented_subset | HOSTING.mdMIXED-COLLECTIONS.md | P1-01 |
| wnl | wnl | no_key_specific_evidence |  | P1-03 |
| wordpress:mb.miniAudioPlayer | wordpress | no_key_specific_evidence |  | P1-03 |
| wordpress:playlist | wordpress | no_key_specific_evidence |  | P1-03 |
| WorldStarHipHop | worldstarhiphop | no_key_specific_evidence |  | P1-03 |
| wppilot | wppilot | no_key_specific_evidence |  | P1-03 |
| wppilot:channels | wppilot | no_key_specific_evidence |  | P1-03 |
| wrestleuniverse:ppv | wrestleuniverse | no_key_specific_evidence |  | P1-03 |
| wrestleuniverse:vod | wrestleuniverse | no_key_specific_evidence |  | P1-03 |
| WSJ | wsj | no_key_specific_evidence |  | P1-03 |
| WSJArticle | wsjarticle | no_key_specific_evidence |  | P1-03 |
| WWE | wwe | no_key_specific_evidence |  | P1-03 |
| wyborcza:video | wyborcza | no_key_specific_evidence |  | P1-03 |
| WyborczaPodcast | wyborczapodcast | no_key_specific_evidence |  | P1-03 |
| wykop:dig | wykop | no_key_specific_evidence |  | P1-03 |
| wykop:​dig:comment | wykop | no_key_specific_evidence |  | P1-03 |
| wykop:post | wykop | no_key_specific_evidence |  | P1-03 |
| wykop:​post:comment | wykop | no_key_specific_evidence |  | P1-03 |
| XboxClips | xboxclips | no_key_specific_evidence |  | P1-03 |
| XHamster | xhamster | no_key_specific_evidence |  | P1-03 |
| XHamsterEmbed | xhamsterembed | no_key_specific_evidence |  | P1-03 |
| XHamsterUser | xhamsteruser | no_key_specific_evidence |  | P1-03 |
| XiaoHongShu | xiaohongshu | no_key_specific_evidence |  | P1-03 |
| ximalaya | ximalaya | no_key_specific_evidence |  | P1-03 |
| ximalaya:album | ximalaya | no_key_specific_evidence |  | P1-03 |
| Xinpianchang | xinpianchang | no_key_specific_evidence |  | P1-03 |
| XMinus | xminus | broken_decision_pending |  | P1-04 |
| XNXX | xnxx | no_key_specific_evidence |  | P1-03 |
| XVideos | xvideos | no_key_specific_evidence |  | P1-03 |
| xvideos:quickies | xvideos | no_key_specific_evidence |  | P1-03 |
| XXXYMovies | xxxymovies | no_key_specific_evidence |  | P1-03 |
| yahoo | yahoo | no_key_specific_evidence |  | P1-03 |
| yahoo:japannews | yahoo | no_key_specific_evidence |  | P1-03 |
| yahoo:search | yahoo | no_key_specific_evidence |  | P1-03 |
| YandexDisk | yandexdisk | no_key_specific_evidence |  | P1-03 |
| yandexmusic:album | yandexmusic | no_key_specific_evidence |  | P1-03 |
| yandexmusic:​artist:albums | yandexmusic | no_key_specific_evidence |  | P1-03 |
| yandexmusic:​artist:tracks | yandexmusic | no_key_specific_evidence |  | P1-03 |
| yandexmusic:playlist | yandexmusic | no_key_specific_evidence |  | P1-03 |
| yandexmusic:track | yandexmusic | no_key_specific_evidence |  | P1-03 |
| YandexVideo | yandexvideo | no_key_specific_evidence |  | P1-03 |
| YandexVideoPreview | yandexvideopreview | no_key_specific_evidence |  | P1-03 |
| YapFiles | yapfiles | broken_decision_pending |  | P1-04 |
| Yappy | yappy | broken_decision_pending |  | P1-04 |
| YappyProfile | yappyprofile | no_key_specific_evidence |  | P1-03 |
| yfanefa | yfanefa | no_key_specific_evidence |  | P1-03 |
| YleAreena | yleareena | no_key_specific_evidence |  | P1-03 |
| YouJizz | youjizz | no_key_specific_evidence |  | P1-03 |
| youku | youku | no_key_specific_evidence |  | P1-03 |
| youku:show | youku | no_key_specific_evidence |  | P1-03 |
| YouNowChannel | younowchannel | no_key_specific_evidence |  | P1-03 |
| YouNowLive | younowlive | no_key_specific_evidence |  | P0-03 |
| YouNowMoment | younowmoment | no_key_specific_evidence |  | P1-03 |
| YouPorn | youporn | no_key_specific_evidence |  | P1-03 |
| YouPornCategory | youporncategory | no_key_specific_evidence |  | P1-03 |
| YouPornChannel | youpornchannel | no_key_specific_evidence |  | P1-03 |
| YouPornCollection | youporncollection | no_key_specific_evidence |  | P1-03 |
| YouPornStar | youpornstar | no_key_specific_evidence |  | P1-03 |
| YouPornTag | youporntag | no_key_specific_evidence |  | P1-03 |
| YouPornVideos | youpornvideos | no_key_specific_evidence |  | P1-03 |
| youtube | youtube | implemented_subset | YOUTUBE.mdLIVE-SABR-AUTHORIZATION.mdWINDOW-PREFIX-RECOVERY.mdDURABLE-LIVE-CAPTIONS.mdPUBLICATION-SESSION-RECOVERY.mdWRITE-CLEANUP-RECOVERY.mdCHANGED-JOB-SESSIONS.mdADAPTIVE-NO-OVERWRITE.md | P0-04 |
| youtube:clip | youtube | no_key_specific_evidence |  | P0-04 |
| youtube:favorites | youtube | no_key_specific_evidence |  | P0-04 |
| youtube:history | youtube | no_key_specific_evidence |  | P0-04 |
| youtube:​music:search_url | youtube | no_key_specific_evidence |  | P0-04 |
| youtube:notif | youtube | no_key_specific_evidence |  | P0-04 |
| youtube:playlist | youtube | implemented_subset | YOUTUBE.mdCHANGED-JOB-SESSIONS.md | P0-04 |
| youtube:recommended | youtube | no_key_specific_evidence |  | P0-04 |
| youtube:search | youtube | no_key_specific_evidence |  | P0-04 |
| youtube:search_url | youtube | implemented_subset | CORE-PLATFORM-INVENTORIES.md | P0-04 |
| youtube:​shorts:pivot:audio | youtube | no_key_specific_evidence |  | P0-04 |
| youtube:subscriptions | youtube | no_key_specific_evidence |  | P0-04 |
| youtube:tab | youtube | implemented_subset | YOUTUBE.mdCHANGED-JOB-SESSIONS.md | P0-04 |
| youtube:user | youtube | implemented_subset | YOUTUBE.md | P0-04 |
| youtube:watchlater | youtube | no_key_specific_evidence |  | P0-04 |
| YoutubeLivestreamEmbed | youtubelivestreamembed | no_key_specific_evidence |  | P0-03 |
| YoutubeYtBe | youtubeytbe | implemented_subset | YOUTUBE.md | P1-03 |
| Zaiko | zaiko | no_key_specific_evidence |  | P1-03 |
| ZaikoETicket | zaikoeticket | no_key_specific_evidence |  | P1-03 |
| zan | zan | no_key_specific_evidence |  | P1-03 |
| Zapiks | zapiks | no_key_specific_evidence |  | P1-03 |
| Zattoo | zattoo | no_key_specific_evidence |  | P1-03 |
| ZattooLive | zattoolive | no_key_specific_evidence |  | P0-03 |
| ZattooMovies | zattoomovies | no_key_specific_evidence |  | P1-03 |
| ZattooRecordings | zattoorecordings | no_key_specific_evidence |  | P1-03 |
| zdf | zdf | implemented_subset | PUBLIC-BROADCAST.md | P1-03 |
| zdf:channel | zdf | no_key_specific_evidence |  | P1-03 |
| ZeeNews | zeenews | broken_decision_pending |  | P1-04 |
| ZenPorn | zenporn | no_key_specific_evidence |  | P1-03 |
| ZetlandDKArticle | zetlanddkarticle | no_key_specific_evidence |  | P1-03 |
| Zhihu | zhihu | no_key_specific_evidence |  | P1-03 |
| zingmp3 | zingmp3 | no_key_specific_evidence |  | P1-03 |
| zingmp3:album | zingmp3 | no_key_specific_evidence |  | P1-03 |
| zingmp3:chart-home | zingmp3 | no_key_specific_evidence |  | P1-03 |
| zingmp3:chart-music-video | zingmp3 | no_key_specific_evidence |  | P1-03 |
| zingmp3:hub | zingmp3 | no_key_specific_evidence |  | P1-03 |
| zingmp3:liveradio | zingmp3 | no_key_specific_evidence |  | P0-03 |
| zingmp3:podcast | zingmp3 | no_key_specific_evidence |  | P1-03 |
| zingmp3:podcast-episode | zingmp3 | no_key_specific_evidence |  | P1-03 |
| zingmp3:user | zingmp3 | no_key_specific_evidence |  | P1-03 |
| zingmp3:week-chart | zingmp3 | no_key_specific_evidence |  | P1-03 |
| zoom | zoom | no_key_specific_evidence |  | P1-03 |
| zoom:clips | zoom | no_key_specific_evidence |  | P1-03 |
| Zype | zype | no_key_specific_evidence |  | P1-03 |
| generic | generic | implemented_subset | STRUCTURED-MEDIA.md | P1-03 |

## Full app-option backlog

| Option | Audit status | Batch |
| --- | --- | --- |
| General.DownloadDirectory | binding_present_needs_audit | P0-01 |
| General.OutputTemplate | implemented_subset | P0-01 |
| General.MaxConcurrentDownloads | binding_present_needs_audit | P0-01 |
| General.IgnoreErrors | implemented_subset | P0-01 |
| General.AbortOnError | implemented_subset | P0-01 |
| General.DownloadArchive | implemented_subset | P0-01 |
| General.NoPlaylist | binding_present_needs_audit | P0-01 |
| General.RestrictFilenames | implemented_subset | P0-01 |
| General.NoOverwrites | implemented_subset | P0-01 |
| General.NoContinue | implemented_subset | P0-01 |
| General.NoPart | implemented_subset | P0-01 |
| General.NoMtime | implemented_subset | P0-01 |
| General.WriteDescription | implemented_subset | P0-01 |
| General.WriteInfoJson | implemented_subset | P0-01 |
| General.WriteThumbnail | implemented_subset | P0-01 |
| General.WriteAllThumbnails | implemented_subset | P0-01 |
| General.CacheDir | implemented_subset | P0-05 |
| General.NoCacheDir | implementation_or_app_policy_required | P0-05 |
| General.ExtraArgs | implemented_subset | P0-01 |
| General.CheckEngineUpdatesOnStart | implementation_or_app_policy_required | P0-05 |
| Format.DefaultQuality | binding_present_needs_audit | P0-01 |
| Format.MergeOutputFormat | implemented_subset | P0-03 |
| Format.PreferFreeFormats | implemented_subset | P0-01 |
| Format.AudioFormat | implemented_subset | P0-01 |
| Format.AudioQuality | implemented_subset | P0-01 |
| Format.RecodeVideo | implemented_subset | P0-01 |
| Format.KeepVideo | implemented_subset | P0-01 |
| Format.NoPostOverwrites | implemented_subset | P0-01 |
| Subtitle.WriteSubs | implemented_subset | P1-02 |
| Subtitle.WriteAutoSubs | implemented_subset | P1-02 |
| Subtitle.WriteAllSubs | implemented_subset | P1-02 |
| Subtitle.SubLangs | implemented_subset | P1-02 |
| Subtitle.SubFormat | implemented_subset | P1-02 |
| Subtitle.ConvertSubs | implemented_subset | P1-02 |
| Subtitle.EmbedSubs | implemented_subset | P1-02 |
| Network.Proxy | implemented_subset | P0-01 |
| Network.SocketTimeout | implemented_subset | P0-01 |
| Network.SourceAddress | implemented_subset | P0-01 |
| Network.IpVersion | implemented_subset | P0-01 |
| Network.GeoBypass | binding_present_needs_audit | P0-03 |
| Network.GeoVerificationProxy | implemented_subset | P0-03 |
| Network.GeoBypassCountry | implemented_subset | P0-03 |
| Network.GeoBypassIpBlock | implemented_subset | P0-03 |
| Network.RateLimit | implemented_subset | P0-01 |
| Network.Retries | implemented_subset | P0-01 |
| Network.FragmentRetries | implemented_subset | P0-01 |
| Network.SkipUnavailableFragments | implementation_or_app_policy_required | P0-03 |
| Network.HttpChunkSize | implemented_subset | P0-01 |
| Network.BufferSize | implemented_subset | P0-01 |
| Network.NoResizeBuffer | implemented_subset | P0-01 |
| Network.PlaylistReverse | implemented_subset | P0-01 |
| Network.PlaylistRandom | implemented_subset | P0-01 |
| Network.ExternalDownloader | implemented_subset | P0-03 |
| Network.ExternalDownloaderArgs | implemented_subset | P0-03 |
| Network.SleepInterval | implemented_subset | P0-01 |
| Network.MaxSleepInterval | implemented_subset | P0-01 |
| Network.NoCheckCertificate | implemented_subset | P0-01 |
| Network.UserAgent | implemented_subset | P0-01 |
| Network.Referer | implemented_subset | P0-01 |
| Network.CustomHeaders | implemented_subset | P0-01 |
| Network.ConcurrentFragments | implemented_subset | P0-01 |
| Selection.PlaylistStart | implemented_subset | P0-01 |
| Selection.PlaylistEnd | implemented_subset | P0-01 |
| Selection.PlaylistItems | implemented_subset | P0-01 |
| Selection.MatchTitle | implemented_subset | P0-01 |
| Selection.RejectTitle | implemented_subset | P0-01 |
| Selection.MaxDownloads | implemented_subset | P0-01 |
| Selection.MinFilesize | implemented_subset | P0-01 |
| Selection.MaxFilesize | implemented_subset | P0-01 |
| Selection.Date | implemented_subset | P0-01 |
| Selection.DateBefore | implemented_subset | P0-01 |
| Selection.DateAfter | implemented_subset | P0-01 |
| Selection.MinViews | implemented_subset | P0-01 |
| Selection.MaxViews | implemented_subset | P0-01 |
| Selection.MatchFilter | implemented_subset | P0-01 |
| Selection.AgeLimit | implemented_subset | P0-01 |
| Auth.Username | implemented_subset | P0-02 |
| Auth.Password | implemented_subset | P0-02 |
| Auth.TwoFactor | implemented_subset | P0-02 |
| Auth.UseNetrc | implemented_subset | P0-02 |
| Auth.VideoPassword | implemented_subset | P0-02 |
| Auth.ApMso | explicitly_refused | P0-02 |
| Auth.ApUsername | explicitly_refused | P0-02 |
| Auth.ApPassword | explicitly_refused | P0-02 |
| Auth.CookiesFile | implemented_subset | P0-02 |
| Auth.CookiesFromBrowser | implemented_subset | P0-02 |
| PostProcess.EmbedThumbnail | implemented_subset | P1-02 |
| PostProcess.EmbedChapters | binding_present_needs_audit | P1-02 |
| PostProcess.AddMetadata | implemented_subset | P1-02 |
| PostProcess.ParseMetadata | implemented_subset | P1-02 |
| PostProcess.Fixup | implemented_subset | P1-02 |
| PostProcess.FfmpegLocation | implementation_or_app_policy_required | P1-02 |
| PostProcess.PostprocessorArgs | implemented_subset | P1-02 |
| PostProcess.ExecCommand | implemented_subset | P1-02 |
| PostProcess.SponsorBlock | implemented_subset | P1-02 |
| DownloadOptions.DownloadMode | binding_present_needs_audit | P0-01 |
| DownloadOptions.QualityPreset | binding_present_needs_audit | P0-01 |
| DownloadOptions.CustomFormat | implemented_subset | P0-01 |
| DownloadOptions.VideoCodec | implemented_subset | P0-01 |
| DownloadOptions.MaxFps | implemented_subset | P0-01 |
| DownloadOptions.Container | binding_present_needs_audit | P0-01 |
| DownloadOptions.AudioFormat | implemented_subset | P0-01 |
| DownloadOptions.AudioQuality | implemented_subset | P0-01 |
| DownloadOptions.WriteSubs | implemented_subset | P0-01 |
| DownloadOptions.WriteAutoSubs | implemented_subset | P0-01 |
| DownloadOptions.EmbedSubs | implemented_subset | P0-01 |
| DownloadOptions.SubLangs | implemented_subset | P0-01 |
| DownloadOptions.WriteThumbnail | implemented_subset | P0-01 |
| DownloadOptions.EmbedThumbnail | implemented_subset | P0-01 |
| DownloadOptions.AddMetadata | implemented_subset | P0-01 |
| DownloadOptions.EmbedChapters | implemented_subset | P0-01 |
| DownloadOptions.OutputDirectory | binding_present_needs_audit | P0-01 |
| DownloadOptions.OutputTemplate | implemented_subset | P0-01 |
| DownloadOptions.DownloadPlaylist | implemented_subset | P0-01 |
| DownloadOptions.PlaylistItems | implemented_subset | P0-01 |
| DownloadOptions.PlaylistStart | implemented_subset | P0-01 |
| DownloadOptions.PlaylistEnd | implemented_subset | P0-01 |
| DownloadOptions.CookiesFile | implemented_subset | P0-01 |
| DownloadOptions.CookiesFromBrowser | implemented_subset | P0-01 |
| DownloadOptions.RateLimit | implemented_subset | P0-01 |
| DownloadOptions.Retries | implemented_subset | P0-01 |
| DownloadOptions.ConcurrentFragments | implemented_subset | P0-01 |
| DownloadOptions.ExtraArgs | implemented_subset | P0-01 |
| AnalysisOptions.Mode | implemented_subset | P0-01 |
| AnalysisOptions.CookiesFile | implemented_subset | P0-01 |
| AnalysisOptions.CookiesFromBrowser | implemented_subset | P0-01 |

## Full captured long-option backlog

| Captured name | Audit status | Batch |
| --- | --- | --- |
| --abort-on-error | typed_handler_present_needs_semantics_audit | P0-01 |
| --abort-on-unavailable-fragments | not_implemented_or_app_policy_required | P0-03 |
| --add-chapters | typed_handler_present_needs_semantics_audit | P1-02 |
| --add-headers | typed_handler_present_needs_semantics_audit | P0-01 |
| --add-metadata | typed_handler_present_needs_semantics_audit | P1-02 |
| --age-limit | typed_handler_present_needs_semantics_audit | P0-01 |
| --alias | not_implemented_or_app_policy_required | P0-01 |
| --allow-dynamic-mpd | typed_handler_present_needs_semantics_audit | P0-03 |
| --ap-list-mso | not_implemented_or_app_policy_required | P0-02 |
| --ap-mso | not_implemented_or_app_policy_required | P0-02 |
| --ap-password | not_implemented_or_app_policy_required | P0-02 |
| --ap-username | not_implemented_or_app_policy_required | P0-02 |
| --audio-format | typed_handler_present_needs_semantics_audit | P0-01 |
| --audio-multistreams | not_implemented_or_app_policy_required | P0-01 |
| --audio-quality | typed_handler_present_needs_semantics_audit | P0-01 |
| --batch-file | not_implemented_or_app_policy_required | P0-01 |
| --bidi-workaround | not_implemented_or_app_policy_required | P0-01 |
| --break-match-filters | not_implemented_or_app_policy_required | P0-01 |
| --break-on-existing | not_implemented_or_app_policy_required | P0-01 |
| --break-per-input | not_implemented_or_app_policy_required | P0-01 |
| --buffer-size | typed_handler_present_needs_semantics_audit | P0-01 |
| --cache-dir | typed_handler_present_needs_semantics_audit | P0-01 |
| --check-all-formats | not_implemented_or_app_policy_required | P0-01 |
| --check-formats | not_implemented_or_app_policy_required | P0-01 |
| --clean-info-json | not_implemented_or_app_policy_required | P0-01 |
| --client-certificate | not_implemented_or_app_policy_required | P0-01 |
| --client-certificate-key | not_implemented_or_app_policy_required | P0-01 |
| --client-certificate-password | not_implemented_or_app_policy_required | P0-02 |
| --color | not_implemented_or_app_policy_required | P0-01 |
| --compat-options | not_implemented_or_app_policy_required | P0-01 |
| --concat-playlist | not_implemented_or_app_policy_required | P0-01 |
| --concurrent-fragments | typed_handler_present_needs_semantics_audit | P0-03 |
| --config-locations | not_implemented_or_app_policy_required | P0-05 |
| --console-title | not_implemented_or_app_policy_required | P0-01 |
| --continue | typed_handler_present_needs_semantics_audit | P0-01 |
| --convert- | captured_name_requires_review | P0-01 |
| --convert-subs | typed_handler_present_needs_semantics_audit | P1-02 |
| --convert-thumbnails | not_implemented_or_app_policy_required | P1-02 |
| --cookies | typed_handler_present_needs_semantics_audit | P0-01 |
| --cookies-from-browser | typed_handler_present_needs_semantics_audit | P0-01 |
| --date | typed_handler_present_needs_semantics_audit | P0-01 |
| --dateafter | typed_handler_present_needs_semantics_audit | P0-01 |
| --datebefore | typed_handler_present_needs_semantics_audit | P0-01 |
| --default-search | not_implemented_or_app_policy_required | P0-01 |
| --download-archive | typed_handler_present_needs_semantics_audit | P0-01 |
| --download-sections | not_implemented_or_app_policy_required | P0-01 |
| --downloader | typed_handler_present_needs_semantics_audit | P0-01 |
| --downloader-args | typed_handler_present_needs_semantics_audit | P0-01 |
| --dump-json | not_implemented_or_app_policy_required | P0-01 |
| --dump-pages | not_implemented_or_app_policy_required | P0-01 |
| --dump-single-json | not_implemented_or_app_policy_required | P0-01 |
| --embed-chapters | typed_handler_present_needs_semantics_audit | P1-02 |
| --embed-info-json | not_implemented_or_app_policy_required | P0-01 |
| --embed-metadata | typed_handler_present_needs_semantics_audit | P1-02 |
| --embed-subs | typed_handler_present_needs_semantics_audit | P1-02 |
| --embed-thumbnail | typed_handler_present_needs_semantics_audit | P1-02 |
| --enable-file-urls | not_implemented_or_app_policy_required | P0-01 |
| --encoding | not_implemented_or_app_policy_required | P0-01 |
| --exec | typed_handler_present_needs_semantics_audit | P1-02 |
| --external-downloader | typed_handler_present_needs_semantics_audit | P0-01 |
| --external-downloader-args | typed_handler_present_needs_semantics_audit | P0-01 |
| --extract-audio | typed_handler_present_needs_semantics_audit | P0-01 |
| --extractor-args | not_implemented_or_app_policy_required | P0-01 |
| --extractor-descriptions | not_implemented_or_app_policy_required | P0-01 |
| --extractor-retries | not_implemented_or_app_policy_required | P0-01 |
| --ffmpeg-location | typed_handler_present_needs_semantics_audit | P0-01 |
| --file-access-retries | not_implemented_or_app_policy_required | P0-01 |
| --fixup | typed_handler_present_needs_semantics_audit | P1-02 |
| --flat-playlist | not_implemented_or_app_policy_required | P0-01 |
| --force-download-archive | typed_handler_present_needs_semantics_audit | P0-01 |
| --force-ipv4 | typed_handler_present_needs_semantics_audit | P0-01 |
| --force-ipv6 | typed_handler_present_needs_semantics_audit | P0-01 |
| --force-keyframes-at-cuts | not_implemented_or_app_policy_required | P0-01 |
| --force-overwrites | typed_handler_present_needs_semantics_audit | P0-01 |
| --force-write-archive | typed_handler_present_needs_semantics_audit | P0-01 |
| --format | typed_handler_present_needs_semantics_audit | P0-01 |
| --format-sort | not_implemented_or_app_policy_required | P0-01 |
| --format-sort-force | not_implemented_or_app_policy_required | P0-01 |
| --format-sort-reset | not_implemented_or_app_policy_required | P0-01 |
| --fragment-retries | typed_handler_present_needs_semantics_audit | P0-03 |
| --geo-verification-proxy | typed_handler_present_needs_semantics_audit | P0-01 |
| --get-audio | not_implemented_or_app_policy_required | P0-01 |
| --get-comments | not_implemented_or_app_policy_required | P0-01 |
| --help | not_implemented_or_app_policy_required | P0-05 |
| --hls-split-discontinuity | not_implemented_or_app_policy_required | P0-03 |
| --hls-use-mpegts | not_implemented_or_app_policy_required | P0-03 |
| --http-chunk-size | typed_handler_present_needs_semantics_audit | P0-01 |
| --ies | not_implemented_or_app_policy_required | P0-01 |
| --ignore-config | not_implemented_or_app_policy_required | P0-05 |
| --ignore-dynamic-mpd | typed_handler_present_needs_semantics_audit | P0-03 |
| --ignore-errors | typed_handler_present_needs_semantics_audit | P0-01 |
| --ignore-no-formats-error | typed_handler_present_needs_semantics_audit | P0-01 |
| --impersonate | not_implemented_or_app_policy_required | P0-01 |
| --js-runtimes | not_implemented_or_app_policy_required | P0-01 |
| --keep-fragments | not_implemented_or_app_policy_required | P0-03 |
| --keep-video | typed_handler_present_needs_semantics_audit | P0-01 |
| --lazy-playlist | not_implemented_or_app_policy_required | P0-01 |
| --legacy-server-connect | not_implemented_or_app_policy_required | P0-01 |
| --limit-rate | typed_handler_present_needs_semantics_audit | P0-01 |
| --list-extractors | not_implemented_or_app_policy_required | P0-01 |
| --list-formats | not_implemented_or_app_policy_required | P0-01 |
| --list-impersonate-targets | not_implemented_or_app_policy_required | P0-01 |
| --list-subs | not_implemented_or_app_policy_required | P1-02 |
| --list-thumbnails | not_implemented_or_app_policy_required | P1-02 |
| --live-from-start | typed_handler_present_needs_semantics_audit | P0-03 |
| --load-info-json | not_implemented_or_app_policy_required | P0-01 |
| --mark-watched | not_implemented_or_app_policy_required | P0-01 |
| --match-filters | typed_handler_present_needs_semantics_audit | P0-01 |
| --max-downloads | typed_handler_present_needs_semantics_audit | P0-01 |
| --max-filesize | typed_handler_present_needs_semantics_audit | P0-01 |
| --max-sleep-interval | typed_handler_present_needs_semantics_audit | P0-01 |
| --merge-output-format | typed_handler_present_needs_semantics_audit | P0-01 |
| --min-filesize | typed_handler_present_needs_semantics_audit | P0-01 |
| --min-sleep-interval | typed_handler_present_needs_semantics_audit | P0-01 |
| --mtime | typed_handler_present_needs_semantics_audit | P0-01 |
| --netrc | typed_handler_present_needs_semantics_audit | P0-02 |
| --netrc-cmd | not_implemented_or_app_policy_required | P0-02 |
| --netrc-location | typed_handler_present_needs_semantics_audit | P0-02 |
| --newline | not_implemented_or_app_policy_required | P0-01 |
| --no-abort-on-error | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-abort-on-unavailable-fragments | not_implemented_or_app_policy_required | P0-03 |
| --no-add-chapters | typed_handler_present_needs_semantics_audit | P1-02 |
| --no-add-metadata | typed_handler_present_needs_semantics_audit | P1-02 |
| --no-allow-dynamic-mpd | typed_handler_present_needs_semantics_audit | P0-03 |
| --no-audio-multistreams | not_implemented_or_app_policy_required | P0-01 |
| --no-batch-file | not_implemented_or_app_policy_required | P0-01 |
| --no-break-match-filters | not_implemented_or_app_policy_required | P0-01 |
| --no-break-on-existing | not_implemented_or_app_policy_required | P0-01 |
| --no-break-per-input | not_implemented_or_app_policy_required | P0-01 |
| --no-cache-dir | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-check-certificates | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-check-formats | not_implemented_or_app_policy_required | P0-01 |
| --no-clean-info-json | not_implemented_or_app_policy_required | P0-01 |
| --no-config | not_implemented_or_app_policy_required | P0-05 |
| --no-config-locations | not_implemented_or_app_policy_required | P0-05 |
| --no-continue | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-cookies | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-cookies-from-browser | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-download | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-download-archive | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-embed-chapters | typed_handler_present_needs_semantics_audit | P1-02 |
| --no-embed-info-json | not_implemented_or_app_policy_required | P0-01 |
| --no-embed-metadata | typed_handler_present_needs_semantics_audit | P1-02 |
| --no-embed-subs | typed_handler_present_needs_semantics_audit | P1-02 |
| --no-embed-thumbnail | typed_handler_present_needs_semantics_audit | P1-02 |
| --no-exec | typed_handler_present_needs_semantics_audit | P1-02 |
| --no-flat-playlist | not_implemented_or_app_policy_required | P0-01 |
| --no-force-keyframes-at-cuts | not_implemented_or_app_policy_required | P0-01 |
| --no-force-overwrites | not_implemented_or_app_policy_required | P0-01 |
| --no-format-sort-force | not_implemented_or_app_policy_required | P0-01 |
| --no-get-comments | not_implemented_or_app_policy_required | P0-01 |
| --no-hls-split-discontinuity | not_implemented_or_app_policy_required | P0-03 |
| --no-hls-use-mpegts | not_implemented_or_app_policy_required | P0-03 |
| --no-ignore-dynamic-mpd | typed_handler_present_needs_semantics_audit | P0-03 |
| --no-ignore-errors | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-ignore-no-formats-error | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-js- | captured_name_requires_review | P0-01 |
| --no-js-runtimes | not_implemented_or_app_policy_required | P0-01 |
| --no-keep-fragments | not_implemented_or_app_policy_required | P0-03 |
| --no-keep-video | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-lazy-playlist | not_implemented_or_app_policy_required | P0-01 |
| --no-live-from-start | typed_handler_present_needs_semantics_audit | P0-03 |
| --no-mark-watched | not_implemented_or_app_policy_required | P0-01 |
| --no-match-filters | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-mtime | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-overwrites | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-part | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-playlist | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-plugin-dirs | not_implemented_or_app_policy_required | P0-05 |
| --no-post-overwrites | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-prefer-free-formats | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-progress | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-quiet | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-remote-components | not_implemented_or_app_policy_required | P0-01 |
| --no-remove-chapters | not_implemented_or_app_policy_required | P1-02 |
| --no-resize-buffer | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-restrict-filenames | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-simulate | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-skip-unavailable-fragments | not_implemented_or_app_policy_required | P0-03 |
| --no-split-chapters | not_implemented_or_app_policy_required | P1-02 |
| --no-sponsorblock | typed_handler_present_needs_semantics_audit | P1-02 |
| --no-update | not_implemented_or_app_policy_required | P0-05 |
| --no-video-multistreams | not_implemented_or_app_policy_required | P0-01 |
| --no-wait-for-video | not_implemented_or_app_policy_required | P0-01 |
| --no-warnings | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-windows-filenames | not_implemented_or_app_policy_required | P0-01 |
| --no-write-auto-subs | typed_handler_present_needs_semantics_audit | P1-02 |
| --no-write-automatic-subs | typed_handler_present_needs_semantics_audit | P1-02 |
| --no-write-comments | not_implemented_or_app_policy_required | P0-01 |
| --no-write-description | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-write-info-json | typed_handler_present_needs_semantics_audit | P0-01 |
| --no-write-playlist-metafiles | not_implemented_or_app_policy_required | P0-01 |
| --no-write-subs | typed_handler_present_needs_semantics_audit | P1-02 |
| --no-write-thumbnail | typed_handler_present_needs_semantics_audit | P1-02 |
| --output | typed_handler_present_needs_semantics_audit | P0-01 |
| --output-na-placeholder | not_implemented_or_app_policy_required | P0-01 |
| --parse-metadata | typed_handler_present_needs_semantics_audit | P1-02 |
| --part | typed_handler_present_needs_semantics_audit | P0-01 |
| --password | typed_handler_present_needs_semantics_audit | P0-02 |
| --paths | typed_handler_present_needs_semantics_audit | P0-01 |
| --playlist-items | typed_handler_present_needs_semantics_audit | P0-01 |
| --playlist-random | typed_handler_present_needs_semantics_audit | P0-01 |
| --playlist-reverse | typed_handler_present_needs_semantics_audit | P0-01 |
| --plugin-dirs | not_implemented_or_app_policy_required | P0-05 |
| --post-overwrites | typed_handler_present_needs_semantics_audit | P0-01 |
| --postprocessor-args | typed_handler_present_needs_semantics_audit | P1-02 |
| --ppa | typed_handler_present_needs_semantics_audit | P0-01 |
| --prefer-free-formats | typed_handler_present_needs_semantics_audit | P0-01 |
| --prefer-insecure | not_implemented_or_app_policy_required | P0-01 |
| --preset-alias | not_implemented_or_app_policy_required | P0-01 |
| --print | not_implemented_or_app_policy_required | P0-01 |
| --print-to-file | not_implemented_or_app_policy_required | P0-01 |
| --print-traffic | not_implemented_or_app_policy_required | P0-01 |
| --progress | typed_handler_present_needs_semantics_audit | P0-01 |
| --progress-delta | not_implemented_or_app_policy_required | P0-01 |
| --progress-template | not_implemented_or_app_policy_required | P0-01 |
| --proxy | typed_handler_present_needs_semantics_audit | P0-01 |
| --quiet | typed_handler_present_needs_semantics_audit | P0-01 |
| --recode-video | typed_handler_present_needs_semantics_audit | P0-01 |
| --remote-components | not_implemented_or_app_policy_required | P0-01 |
| --remove-chapters | not_implemented_or_app_policy_required | P1-02 |
| --remux-video | typed_handler_present_needs_semantics_audit | P0-01 |
| --replace-in-metadata | not_implemented_or_app_policy_required | P1-02 |
| --resize-buffer | typed_handler_present_needs_semantics_audit | P0-01 |
| --restrict-filenames | typed_handler_present_needs_semantics_audit | P0-01 |
| --retries | typed_handler_present_needs_semantics_audit | P0-01 |
| --retry-sleep | not_implemented_or_app_policy_required | P0-01 |
| --rm-cache-dir | typed_handler_present_needs_semantics_audit | P0-01 |
| --simulate | typed_handler_present_needs_semantics_audit | P0-01 |
| --skip-download | typed_handler_present_needs_semantics_audit | P0-01 |
| --skip-playlist-after-errors | not_implemented_or_app_policy_required | P0-01 |
| --skip-unavailable-fragments | not_implemented_or_app_policy_required | P0-03 |
| --sleep-interval | typed_handler_present_needs_semantics_audit | P0-01 |
| --sleep-requests | typed_handler_present_needs_semantics_audit | P0-01 |
| --sleep-subtitles | typed_handler_present_needs_semantics_audit | P1-02 |
| --socket-timeout | typed_handler_present_needs_semantics_audit | P0-01 |
| --source-address | typed_handler_present_needs_semantics_audit | P0-01 |
| --split-chapters | not_implemented_or_app_policy_required | P1-02 |
| --sponsorblock-api | not_implemented_or_app_policy_required | P1-02 |
| --sponsorblock-chapter-title | not_implemented_or_app_policy_required | P1-02 |
| --sponsorblock-mark | typed_handler_present_needs_semantics_audit | P1-02 |
| --sponsorblock-remove | typed_handler_present_needs_semantics_audit | P1-02 |
| --sub- | captured_name_requires_review | P1-02 |
| --sub-format | typed_handler_present_needs_semantics_audit | P1-02 |
| --sub-langs | typed_handler_present_needs_semantics_audit | P1-02 |
| --throttled-rate | not_implemented_or_app_policy_required | P0-01 |
| --trim-filenames | not_implemented_or_app_policy_required | P0-01 |
| --twofactor | typed_handler_present_needs_semantics_audit | P0-02 |
| --update | not_implemented_or_app_policy_required | P0-05 |
| --update-to | not_implemented_or_app_policy_required | P0-05 |
| --use-extractors | not_implemented_or_app_policy_required | P0-01 |
| --use-postprocessor | not_implemented_or_app_policy_required | P1-02 |
| --username | typed_handler_present_needs_semantics_audit | P0-02 |
| --verbose | not_implemented_or_app_policy_required | P0-01 |
| --version | not_implemented_or_app_policy_required | P0-05 |
| --video-multistreams | not_implemented_or_app_policy_required | P0-01 |
| --video-password | typed_handler_present_needs_semantics_audit | P0-02 |
| --wait-for-video | not_implemented_or_app_policy_required | P0-01 |
| --windows-filenames | not_implemented_or_app_policy_required | P0-01 |
| --write-all-thumbnails | typed_handler_present_needs_semantics_audit | P1-02 |
| --write-auto-subs | typed_handler_present_needs_semantics_audit | P1-02 |
| --write-automatic-subs | typed_handler_present_needs_semantics_audit | P1-02 |
| --write-comments | not_implemented_or_app_policy_required | P0-01 |
| --write-description | typed_handler_present_needs_semantics_audit | P0-01 |
| --write-desktop-link | not_implemented_or_app_policy_required | P0-01 |
| --write-info-json | typed_handler_present_needs_semantics_audit | P0-01 |
| --write-link | not_implemented_or_app_policy_required | P0-01 |
| --write-pages | not_implemented_or_app_policy_required | P0-01 |
| --write-playlist-metafiles | not_implemented_or_app_policy_required | P0-01 |
| --write-subs | typed_handler_present_needs_semantics_audit | P1-02 |
| --write-thumbnail | typed_handler_present_needs_semantics_audit | P1-02 |
| --write-url-link | not_implemented_or_app_policy_required | P0-01 |
| --write-webloc-link | not_implemented_or_app_policy_required | P0-01 |
| --xattrs | not_implemented_or_app_policy_required | P0-01 |
| --xff | typed_handler_present_needs_semantics_audit | P0-01 |
| --yes-playlist | typed_handler_present_needs_semantics_audit | P0-01 |

The JSON companion contains exact sources, evidence, acceptance criteria, missing workflow specifications, grouping and dependencies. Missing/broken decisions remain open; default generic discovery does not prove site coverage. Native WPF cutover and packaging remain blocked by the original coverage requirement.
