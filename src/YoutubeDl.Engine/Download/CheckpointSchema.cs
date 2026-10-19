namespace YoutubeDl.Engine.Download;

// Schema 1 is the compatible 0.168.0 timeline/caption envelope. Its source,
// metadata, payload and ownership validation remains mandatory before reuse.
internal static class CheckpointSchema
{
    internal static bool Compatible(int schema,int minimumReader,string? writer)=>schema switch
    {
        1=>minimumReader==0&&writer is null,
        2=>minimumReader==2&&Version.TryParse(writer,out var version)&&version>=new Version(0,170,0),
        _=>false
    };
}
