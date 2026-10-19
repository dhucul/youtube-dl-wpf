using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    private static async Task PracticalFacebookSilentMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var mixed=Path.Combine(files.Directory,"mixed.mp4");var video=Path.Combine(files.Directory,"silent.mp4");
        await MixedSource(ffmpeg,mixed);await Run(ffmpeg,["-v","error","-y","-i",mixed,"-map","0:v:0","-c","copy",video]);
        var bytes=await File.ReadAllBytesAsync(video);var renewal=mode.EndsWith("-renewal",StringComparison.Ordinal);var audio=mode.EndsWith("-audio",StringComparison.Ordinal);
        var fixture=new PracticalFacebookChecks.Fixture{Mode="silent-"+(mode.StartsWith("dash",StringComparison.Ordinal)?"dash":"progressive")+(renewal?"-renewal":""),Bytes=bytes,VideoBytes=bytes};
        var request=fixture.Request(Path.Combine(files.Directory,"output")) with{FfmpegPath=ffmpeg,AudioOnly=audio,KeepOriginal=true,DownloadArchiveFile=Path.Combine(files.Directory,"archive.txt")};
        if(audio)
        {
            await ProviderFamiliesChecks.Expect<Exception>(()=>fixture.Engine.DownloadAsync(request,null,null,default));
            PracticalFacebookChecks.Assert(fixture.Transfers==0&&!File.Exists(request.DownloadArchiveFile),"Silent Facebook media fabricated audio or recorded a refused audio request.");return;
        }
        var result=await fixture.Engine.DownloadAsync(request,null,null,default);
        PracticalFacebookChecks.Assert(result.Succeeded&&result.OutputPath is not null&&File.ReadAllBytes(result.OutputPath).SequenceEqual(bytes),"Silent Facebook download changed or rejected complete video: "+result.Error);
        await Run(ffmpeg,["-v","error","-xerror","-i",result.OutputPath!,"-map","0:v:0","-f","null","-"]);
        PracticalFacebookChecks.Assert(fixture.Transfers==1&&fixture.Generation==(renewal?2:1),"Silent Facebook download fetched unexpected tracks or failed renewal.");
        var archived=await fixture.Engine.DownloadAsync(request,null,null,default);
        PracticalFacebookChecks.Assert(archived.Succeeded&&archived.Skipped&&fixture.Transfers==1,"Archived silent Facebook video repeated transfer.");
    }
}
