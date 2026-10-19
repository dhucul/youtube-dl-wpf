using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    private static async Task BatchedArgumentsMedia(string ffmpeg,string format,bool keep)
    {
        using var files=new Files();var losslessSource=format=="flac";var source=Path.Combine(files.Directory,losslessSource?"source.wav":"source.mp3");
        await Run(ffmpeg,losslessSource?["-v","error","-y","-f","lavfi","-i","sine=frequency=590:sample_rate=44100:duration=2","-c:a","pcm_s16le",source]:["-v","error","-y","-f","lavfi","-i","sine=frequency=590:sample_rate=44100:duration=2","-c:a","libmp3lame","-b:a","128k",source]);var bytes=await File.ReadAllBytesAsync(source);
        var request=NativeExtraArguments.Apply(MetadataParsingChecks.Request(files.Directory),["-x","--audio-format="+format,"--audio-quality=128K","--ffmpeg-location",Path.GetDirectoryName(ffmpeg)!,keep?"--keep-video":"--no-keep-video","--add-metadata","--no-add-chapters","--buffer-size=4K","--http-chunk-size=0","--write-info-json","--download-archive",Path.Combine(files.Directory,"archive.txt")]);
        if(format=="vorbis")
        {
            source=Path.Combine(files.Directory,"variable.ogg");
            await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","aevalsrc=0.8*sin(2*PI*590*t)*lt(mod(t\\,0.7)\\,0.3):s=44100:d=3","-c:a","libvorbis",source]);
            bytes=await File.ReadAllBytesAsync(source);
        }
        var page=MetadataParsingChecks.Page;
        if(losslessSource)page=page with{Media=[new(){Url=new("https://parse.example/audio.wav"),Extension="wav",HasAudio=true,HasVideo=false}]};
        if(format=="vorbis")page=page with{Media=[new(){Url=new("https://parse.example/audio.ogg"),Extension="ogg",HasAudio=true,HasVideo=false}]};
        var calls=0;var callbacks=0;var engine=MetadataParsingChecks.Engine(page,bytes,()=>calls++);var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);
        if(!result.Succeeded||result.OutputPaths.Count!=(keep?2:1)||callbacks!=result.OutputPaths.Count||calls!=1)throw new InvalidOperationException("Batched CLI extraction/retention publication changed.");
        foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);
        if(keep&&!result.OutputPaths.Any(path=>File.ReadAllBytes(path).SequenceEqual(bytes)))throw new InvalidOperationException("Batched CLI lost exact original bytes.");
        if(format is "flac" or "wav")
        {var processed=result.OutputPaths.Single(path=>Path.GetExtension(path)=="."+format&&!File.ReadAllBytes(path).SequenceEqual(bytes));if(await AudioHash(ffmpeg,processed)!=await AudioHash(ffmpeg,source))throw new InvalidOperationException("Lossless CLI extraction changed source audio.");}
        var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);if(!repeat.Skipped||calls!=1||callbacks!=result.OutputPaths.Count)throw new InvalidOperationException("Batched argument archive repeated delivery.");
    }
}
