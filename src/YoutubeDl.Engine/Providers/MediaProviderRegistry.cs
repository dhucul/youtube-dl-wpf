namespace YoutubeDl.Engine.Providers;

public sealed class MediaProviderRegistry
{
    private readonly IMediaProvider[] _providers;
    public IReadOnlyList<MediaProviderInfo> Providers { get; }

    public MediaProviderRegistry(IEnumerable<IMediaProvider> providers)
    {
        _providers = providers.OrderByDescending(provider => provider.Info.Priority).ToArray();
        if (_providers.GroupBy(provider => provider.Info.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new ArgumentException("Provider identifiers must be unique.", nameof(providers));
        Providers = Array.AsReadOnly(_providers.Select(provider => provider.Info).ToArray());
    }

    public bool CanHandle(Uri url) => IsHttp(url) ? _providers.Any(provider => provider.CanHandle(url)) :
        url.IsAbsoluteUri && url.Scheme is "lbry" or "audius" or "nrk" && _providers.Any(provider => provider.Info.Id == (url.Scheme == "lbry" ? "lbry" : url.Scheme == "nrk" ? "nrk" : "audius") && provider.CanHandle(url));

    public IMediaProvider Resolve(Uri url)
    {
        if (url.IsAbsoluteUri && url.Scheme is "lbry" or "audius" or "nrk")
            return _providers.FirstOrDefault(provider => provider.Info.Id == (url.Scheme == "lbry" ? "lbry" : url.Scheme == "nrk" ? "nrk" : "audius") && provider.CanHandle(url)) ?? throw new NotSupportedException("A native provider for this address scheme is not registered.");
        if (!IsHttp(url)) throw new ArgumentException("The media address must be an absolute HTTP or HTTPS URL.", nameof(url));
        return _providers.FirstOrDefault(provider => provider.CanHandle(url))
            ?? throw new NotSupportedException($"A native provider for {url.Host} has not been implemented yet.");
    }

    private static bool IsHttp(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https";

    public static MediaProviderRegistry CreateDefault(YouTube.IYouTubePlayerRuntime? playerRuntime = null, YouTube.IYouTubePlaybackSource? playbackSource = null, Newgrounds.INewgroundsPageSource? newgroundsPageSource = null) => new([new Facebook.FacebookProvider(), new ArdSounds.ArdSoundsProvider(), new Ard.ArdProvider(), new Zdf.ZdfProvider(), new Nrk.NrkProvider(), new Bbc.BbcProvider(), new Svt.SvtProvider(), new Niconico.NiconicoProvider(), new RadioFrance.RadioFranceProvider(), new Orf.OrfOnProvider(), new Orf.OrfRadioProvider(), new Tvp.TvpProvider(), new Arte.ArteProvider(), new Orf.OrfPodcastProvider(), new Rtve.RtveProvider(), new Whyp.WhypProvider(), new JioSaavn.JioSaavnProvider(), new QqMusic.QqMusicProvider(), new NetEase.NetEaseProvider(), new Rutube.RutubeProvider(), new Clyp.ClypProvider(), new Vocaroo.VocarooProvider(), new ReverbNation.ReverbNationProvider(), new HearThis.HearThisProvider(), new Bandlab.BandlabProvider(), new Audiomack.AudiomackProvider(), new Newgrounds.NewgroundsProvider(newgroundsPageSource), new Jamendo.JamendoProvider(), new Omny.OmnyProvider(), new IHeart.IHeartProvider(), new Mixcloud.MixcloudProvider(), new Audius.AudiusProvider(), new Podomatic.PodomaticProvider(), new Megaphone.MegaphoneProvider(), new Libsyn.LibsynProvider(), new Simplecast.SimplecastProvider(), new AudioBoom.AudioBoomProvider(), new Spreaker.SpreakerProvider(), new Acast.AcastProvider(), new Medal.MedalProvider(), new Coub.CoubProvider(), new PbsKids.PbsKidsProvider(), new Pbs.PbsProvider(), new MicrosoftEmbed.MicrosoftEmbedProvider(), new MicrosoftLearn.MicrosoftLearnProvider(), new Mastodon.MastodonProvider(), new Tumblr.TumblrProvider(), new Pinterest.PinterestProvider(), new Flickr.FlickrProvider(), new Twitter.TwitterProvider(), new Loom.LoomProvider(), new Panopto.PanoptoProvider(), new Wikimedia.WikimediaProvider(), new Bluesky.BlueskyProvider(), new Imgur.ImgurProvider(), new Nfb.NfbProvider(), new Rumble.RumbleProvider(), new Bilibili.BilibiliProvider(), new Odysee.OdyseeProvider(), new Reddit.RedditProvider(), new YouTube.YouTubeProvider(playerRuntime, playbackSource), new Vimeo.VimeoProvider(), new Twitch.TwitchProvider(), new TikTok.TikTokProvider(), new Ted.TedProvider(), new Streamable.StreamableProvider(), new Vidyard.VidyardProvider(), new CloudflareStream.CloudflareStreamProvider(), new Dailymotion.DailymotionProvider(), new Wistia.WistiaProvider(), new JwPlatform.JwPlatformProvider(), new Brightcove.BrightcoveProvider(), new Kaltura.KalturaProvider(), new Bandcamp.BandcampProvider(), new SoundCloud.SoundCloudProvider(), new ApplePodcasts.ApplePodcastsProvider(), new InternetArchive.InternetArchiveProvider(), new PeerTube.PeerTubeProvider(), new Nowness.NownessProvider(), new ConsumerHosting.ConsumerHostingProvider(), new DirectMediaProvider(), new GenericPageProvider()]);
}
