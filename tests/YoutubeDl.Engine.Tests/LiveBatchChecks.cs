using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static class LiveBatchChecks
{
    internal static Uri Url=new("https://live.example/live.m3u8");
    public static IEnumerable<(string Name,Func<Task> Test)> All =>
    [
        ("Live batch parses event refresh target and original sequence",()=>Sync(Parse)),
        ("Live batch malformed and repeated refresh controls fail explicitly",()=>Sync(Invalid)),
        ("Live batch delta playlists remain explicit unsupported boundaries",()=>Sync(Delta)),
        ("Live batch duration and from-start arguments preserve ordered overrides",()=>Sync(Arguments)),
        ("Live batch invalid recording limits fail before HTTP",Limits),
        ("Live batch recording controls reject progressive media before transfer",Progressive),
        ("Live batch from-start refuses a window missing the event beginning",FromStart),
        ("Live batch zero target cannot start an open recording or request resources",ZeroTarget),
        ("Live batch native analysis preserves recording discovery controls",()=>Sync(Analysis))
    ];
    private static Task Sync(Action action){action();return Task.CompletedTask;}
    private static void Assert(bool good,string message){if(!good)throw new InvalidOperationException(message);}
    private static void Parse(){var p=HlsPlaylistParser.Parse("#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-PLAYLIST-TYPE:EVENT\n#EXT-X-MEDIA-SEQUENCE:6\n#EXTINF:2,\none.ts\n",Url);Assert(p.SourceUrl==Url&&p.TargetDurationSeconds==2&&p.PlaylistType=="EVENT"&&p.MediaSequence==6&&p.MediaSegments.Single().Sequence==6&&!p.IsComplete,"Live refresh metadata changed.");}
    private static void Invalid(){foreach(var tags in new[]{"#EXT-X-TARGETDURATION:-1","#EXT-X-TARGETDURATION:NaN","#EXT-X-TARGETDURATION:2\n#EXT-X-TARGETDURATION:3","#EXT-X-PLAYLIST-TYPE:other","#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-MEDIA-SEQUENCE:1"}){try{HlsPlaylistParser.Parse("#EXTM3U\n"+tags+"\n#EXTINF:1,\none.ts",Url);throw new InvalidOperationException("Malformed live control accepted.");}catch(InvalidDataException){}}}
    private static void Delta(){var p=HlsPlaylistParser.Parse("#EXTM3U\n#EXT-X-SKIP:SKIPPED-SEGMENTS=2\n#EXTINF:1,\none.ts",Url);Assert(p.UnsupportedFeatures.Contains("delta live playlists"),"Delta descriptor was silently ignored.");}
    private static void Arguments(){var r=NativeExtraArguments.Apply(new(){Url=Url,OutputDirectory=Path.GetTempPath()},["--live-from-start","--no-live-from-start","--live-duration=12.5"]);Assert(!r.LiveFromStart&&r.LiveDurationSeconds==12.5,"Live option order changed.");}
    private static async Task Limits(){foreach(var duration in new[]{0d,-1,double.NaN,double.PositiveInfinity,86401}){var calls=0;var engine=new NativeEngine(httpFactory:_=>{calls++;throw new InvalidOperationException();});try{await engine.DownloadAsync(new(){Url=Url,OutputDirectory=Path.GetTempPath(),LiveDurationSeconds=duration},null,null,default);throw new InvalidOperationException("Invalid limit reached discovery.");}catch(ArgumentException){}Assert(calls==0,"Invalid duration opened HTTP.");}}
    private static async Task Progressive(){var calls=0;var engine=new NativeEngine(httpFactory:_=>AuthenticationBatchChecks.Http((_,_)=>{calls++;throw new InvalidOperationException("Unexpected media request.");}));try{await engine.DownloadAsync(new(){Url=new("https://live.example/file.mp4"),OutputDirectory=Path.GetTempPath(),LiveDurationSeconds=5},null,null,default);throw new InvalidOperationException("Progressive control ignored.");}catch(NotSupportedException){}Assert(calls==0,"Unsupported live control transferred media.");}
    private static async Task FromStart(){var p=HlsPlaylistParser.Parse("#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:9\n#EXTINF:2,\none.ts",Url);using var http=AuthenticationBatchChecks.Http((_,_)=>throw new InvalidOperationException("From-start requested media."));try{await new HlsLiveRecorder(http).DownloadAsync(p,Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".ts"),new(){Url=Url,OutputDirectory=Path.GetTempPath(),LiveFromStart=true},null,new(0),default);throw new InvalidOperationException("Missing beginning accepted.");}catch(NotSupportedException){}}
    private static async Task ZeroTarget()
    {
        using var http=AuthenticationBatchChecks.Http((_,_)=>throw new InvalidOperationException("Invalid timing requested media."));
        foreach(var text in new[]{"#EXTM3U\n#EXT-X-TARGETDURATION:0\n#EXTINF:1,\none.ts","#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:6,\none.ts","#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:1000000000000000000000000000000,\none.ts"})
        {var playlist=HlsPlaylistParser.Parse(text,Url);try{await new HlsLiveRecorder(http).DownloadAsync(playlist,Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".ts"),new(){Url=Url,OutputDirectory=Path.GetTempPath()},null,new(0),default);throw new InvalidOperationException("Invalid target/segment timing was accepted.");}catch(InvalidDataException){}}
    }
    private static void Analysis(){var r=NativeExtraArguments.Apply(new(){Url=Url,OutputDirectory=Path.GetTempPath()},NativeAnalysisArguments.Select(["--live-from-start","--live-duration=12","--force-ipv4"]));Assert(r.LiveFromStart&&r.LiveDurationSeconds is null&&r.IpVersion==4,"Live discovery controls or opaque duration changed.");}
}
