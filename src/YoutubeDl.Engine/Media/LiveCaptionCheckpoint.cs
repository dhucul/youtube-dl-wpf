using System.Security.Cryptography;
using System.Text.Json;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Media;

// Snapshots contain caption bytes and timeline facts; resource addresses are represented only by hashes.
internal sealed class LiveCaptionCheckpoint:IDisposable
{
    internal sealed record Entry(string Identity,string Facts,string Description,byte[] Bytes,string Digest);
    private sealed record Document(int Schema,string Scope,List<Entry> Entries,int MinimumReaderSchema=0,string? WriterVersion=null);
    private readonly AdaptiveFragmentCheckpoint _storage;private readonly string _scope,_file;
    internal IReadOnlyList<Entry> Entries {get;private set;}
    private LiveCaptionCheckpoint(AdaptiveFragmentCheckpoint storage,string scope,List<Entry> entries){_storage=storage;_scope=scope;_file=Path.Combine(storage.DirectoryPath,"captions.json");Entries=entries;}
    internal static bool Exists(string destination,string scope)
    {
        var key=AdaptiveFragmentCheckpoint.Hash("native-adaptive-recovery-v1\n"+Path.GetFullPath(destination)+"\ncaption-v1:"+scope);
        return File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!,".native-fragments-"+key[..24],"captions.json"));
    }
    internal static async Task<LiveCaptionCheckpoint> OpenAsync(MediaHttp http,string destination,string scope,CancellationToken ct)
    {
        var storage=await AdaptiveFragmentCheckpoint.OpenAsync(http,destination,"caption-v1:"+scope,ct).ConfigureAwait(false);
        try
        {
            var file=Path.Combine(storage.DirectoryPath,"captions.json");Regular(file);var entries=new List<Entry>();var owner=AdaptiveFragmentCheckpoint.Hash(scope);
            if(File.Exists(file))
            {
                if(new FileInfo(file).Length>16*1024*1024)throw new InvalidDataException("The saved live captions exceed their snapshot limit.");
                using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
                var document=await JsonSerializer.DeserializeAsync<Document>(input,cancellationToken:ct).ConfigureAwait(false);
                if(document is null||!CheckpointSchema.Compatible(document.Schema,document.MinimumReaderSchema,document.WriterVersion)||document.Scope!=owner||document.Entries is null)throw new InvalidDataException("The saved live captions belong to another recording or require a newer reader.");
                Validate(document.Entries);entries=document.Entries;
            }
            return new(storage,owner,entries);
        }
        catch{storage.Dispose();throw;}
    }
    internal static Entry Row(string identity,string facts,string description,byte[] bytes)
    {
        var id=AdaptiveFragmentCheckpoint.Hash(identity);var fact=AdaptiveFragmentCheckpoint.Hash(facts);
        return new(id,fact,description,bytes,Digest(id,fact,description,bytes));
    }
    internal static Entry RowHashed(string identity,string facts,string description,byte[] bytes)=>new(identity,facts,description,bytes,Digest(identity,facts,description,bytes));
    internal async Task SaveAsync(IEnumerable<Entry> rows,CancellationToken ct)
    {
        var entries=rows.ToList();Validate(entries);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new Document(2,_scope,entries,2,NativeEngine.Version));if(bytes.Length>16*1024*1024)throw new InvalidDataException("The saved live captions exceed their snapshot limit.");
        var temporary=_file+"."+Guid.NewGuid().ToString("N")+".tmp";Regular(_file);
        try{await AdaptiveFragmentCheckpoint.DurableWriteAsync(temporary,bytes,ct).ConfigureAwait(false);File.Move(temporary,_file,true);Entries=entries;}
        finally{try{File.Delete(temporary);}catch(IOException){}}
    }
    private static void Validate(IReadOnlyList<Entry> entries)
    {
        if(entries.Count>21000||entries.Any(row=>row is null||!Hash(row.Identity)||!Hash(row.Facts)||row.Description is null||row.Description.Length>4096||row.Bytes is null||row.Bytes.Length>8*1024*1024||
            row.Digest!=Digest(row.Identity,row.Facts,row.Description,row.Bytes))||entries.Select(row=>row.Identity).Distinct().Count()!=entries.Count||entries.Sum(row=>(long)row.Bytes.Length)>8*1024*1024)
            throw new InvalidDataException("The saved live caption data or timeline changed or exceeds its limit.");
    }
    private static bool Hash(string? value)=>value is {Length:64}&&value.All(c=>c is >= 'A' and <= 'F' or >= '0' and <= '9');
    private static string Digest(string id,string fact,string description,byte[] bytes)=>AdaptiveFragmentCheckpoint.Hash(id+"\n"+fact+"\n"+description+"\n"+Convert.ToHexString(SHA256.HashData(bytes)));
    private static void Regular(string file){try{if((File.GetAttributes(file)&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0)throw new IOException("Live caption snapshots must be regular owned files.");}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}}
    public void Dispose()=>_storage.Dispose();
}
