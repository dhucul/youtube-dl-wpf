using System.Security.Cryptography;
using System.Text.Json;
using YoutubeDl.Engine.Http;
namespace YoutubeDl.Engine.Download;

internal sealed class TimelineCheckpoint:IDisposable
{
    internal sealed record Record(string Identity,string Facts,string Digest,string? Metadata=null,string? MetadataDigest=null);
    private sealed record Document(int Schema,string Scope,List<Record> Records,int MinimumReaderSchema=0,string? WriterVersion=null);
    private readonly AdaptiveFragmentCheckpoint _storage;private readonly string _file,_scope;private readonly List<Record> _records;
    internal IReadOnlyList<Record> Records=>_records;
    private TimelineCheckpoint(AdaptiveFragmentCheckpoint storage,string scope,List<Record> records){_storage=storage;_scope=scope;_records=records;_file=Path.Combine(storage.DirectoryPath,"timeline.json");}
    internal static async Task<TimelineCheckpoint> OpenAsync(MediaHttp http,string destination,string scope,CancellationToken ct)
    {
        var storage=await AdaptiveFragmentCheckpoint.OpenAsync(http,destination,"timeline-v1:"+scope,ct).ConfigureAwait(false);var path=Path.Combine(storage.DirectoryPath,"timeline.json");
        try
        {
            Regular(path);var rows=new List<Record>();
            if(File.Exists(path))
            {
                if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("The saved recording timeline exceeds its limit.");
                using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);var document=await JsonSerializer.DeserializeAsync<Document>(input,cancellationToken:ct).ConfigureAwait(false);
                if(document is null||!CheckpointSchema.Compatible(document.Schema,document.MinimumReaderSchema,document.WriterVersion)||document.Scope!=AdaptiveFragmentCheckpoint.Hash(scope)||document.Records is null||document.Records.Count>100000||document.Records.Any(r=>r is null||!IsHash(r.Identity)||!IsHash(r.Facts)||!IsHash(r.Digest)||r.Metadata is { } metadata&&(metadata.Length>4096||AdaptiveFragmentCheckpoint.Hash(metadata)!=r.MetadataDigest))||document.Records.Select(r=>r.Identity).Distinct().Count()!=document.Records.Count)throw new InvalidDataException("The saved recording timeline is invalid or belongs to another source.");
                rows=document.Records;
            }
            return new(storage,AdaptiveFragmentCheckpoint.Hash(scope),rows);
        }
        catch{storage.Dispose();throw;}
    }
    internal async Task CaptureAsync(string identity,string facts,Uri url,HlsByteRange? range,byte[]? inline,string staging,Func<string,CancellationToken,Task<IndexedDashDownloader.ResourceVersion?>> fetch,CancellationToken ct,string? metadata=null)
    {
        var id=AdaptiveFragmentCheckpoint.Hash(identity);var fact=AdaptiveFragmentCheckpoint.Hash(facts);var existing=_records.FindIndex(r=>r.Identity==id);var index=existing<0?_records.Count:existing;
        if(existing>=0&&_records[existing].Facts!=fact)throw new InvalidDataException("The saved recording segment changed its declared identity or timing.");
        if(metadata is {Length:>4096}||existing>=0&&_records[existing].Metadata is { } prior&&prior!=metadata)throw new InvalidDataException("The saved recording descriptor changed or exceeds its limit.");
        await _storage.CopyAsync(index,url,range,inline,staging,fetch,ct).ConfigureAwait(false);
        await using var input=new FileStream(staging,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);var digest=Convert.ToHexString(await SHA256.HashDataAsync(input,ct).ConfigureAwait(false));
        if(existing>=0){if(_records[existing].Digest!=digest)throw new InvalidDataException("The saved recording segment changed its payload.");return;}
        if(_records.Count>=100000)throw new InvalidDataException("The saved recording exceeds its segment limit.");_records.Add(new(id,fact,digest,metadata,metadata is null?null:AdaptiveFragmentCheckpoint.Hash(metadata)));
        var pending=_file+"."+Guid.NewGuid().ToString("N")+".tmp";Regular(_file);
        try{await AdaptiveFragmentCheckpoint.DurableWriteAsync(pending,JsonSerializer.SerializeToUtf8Bytes(new Document(2,_scope,_records,2,NativeEngine.Version)),ct).ConfigureAwait(false);await RecordingReceipt.ReplaceAsync(pending,_file,ct).ConfigureAwait(false);}finally{try{File.Delete(pending);}catch(IOException){}}
    }
    internal void RequireVisible(IEnumerable<string> identities)
    {
        var available=identities.Select(AdaptiveFragmentCheckpoint.Hash).ToHashSet();if(_records.Any(record=>!available.Contains(record.Identity)))throw new InvalidDataException("The live window no longer exposes the saved beginning; recovery cannot complete the original recording.");
    }
    internal async Task RefineAsync(string identity,string previousFacts,string facts,string? metadata,CancellationToken ct)
    {
        var id=AdaptiveFragmentCheckpoint.Hash(identity);var index=_records.FindIndex(row=>row.Identity==id);
        if(index<0||_records[index].Facts!=AdaptiveFragmentCheckpoint.Hash(previousFacts)||metadata is {Length:>4096})throw new InvalidDataException("The refined live boundary does not match its saved segment.");
        var rows=_records.ToList();rows[index]=rows[index] with {Facts=AdaptiveFragmentCheckpoint.Hash(facts),Metadata=metadata,MetadataDigest=metadata is null?null:AdaptiveFragmentCheckpoint.Hash(metadata)};
        var initialization=_records.FindIndex(row=>row.Identity==AdaptiveFragmentCheckpoint.Hash("init:"+identity));
        if(initialization>=0&&_records[initialization].Facts==AdaptiveFragmentCheckpoint.Hash(previousFacts+":init"))rows[initialization]=rows[initialization] with {Facts=AdaptiveFragmentCheckpoint.Hash(facts+":init")};
        var pending=_file+"."+Guid.NewGuid().ToString("N")+".tmp";Regular(_file);
        try{await AdaptiveFragmentCheckpoint.DurableWriteAsync(pending,JsonSerializer.SerializeToUtf8Bytes(new Document(2,_scope,rows,2,NativeEngine.Version)),ct).ConfigureAwait(false);await RecordingReceipt.ReplaceAsync(pending,_file,ct).ConfigureAwait(false);_records.Clear();_records.AddRange(rows);}
        finally{try{File.Delete(pending);}catch(IOException){}}
    }
    internal Task CopySavedAsync(Record record,string target,CancellationToken ct)
    {
        var index=_records.IndexOf(record);if(index<0)throw new InvalidDataException("The saved resource is outside this recording.");
        return _storage.CopySavedAsync(index,record.Digest,target,ct);
    }
    private static bool IsHash(string? value)=>value is {Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static void Regular(string path){try{if((File.GetAttributes(path)&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0)throw new IOException("Recording receipts must be regular owned files.");}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}}
    public void Dispose()=>_storage.Dispose();
}
