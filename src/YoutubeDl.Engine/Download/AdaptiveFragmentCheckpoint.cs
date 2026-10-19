using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Http;
namespace YoutubeDl.Engine.Download;

// Receipts contain hashes, lengths and entity validators; never URLs or credentials.
internal sealed class AdaptiveFragmentCheckpoint:IDisposable
{
    private sealed record Receipt(int Schema,string Scope,long Bytes,string Digest,string? ETag,long? EntityBytes,bool Inline=false);
    private readonly MediaHttp _http;private readonly string _root,_scope;private readonly FileStream _lease;private bool _complete;
    internal string DirectoryPath=>_root;
    private AdaptiveFragmentCheckpoint(MediaHttp http,string root,string scope,FileStream lease){_http=http;_root=root;_scope=scope;_lease=lease;}
    internal static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static async Task<AdaptiveFragmentCheckpoint> OpenAsync(MediaHttp http,string destination,string facts,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(Encoding.UTF8.GetByteCount(facts)>32*1024*1024)throw new InvalidDataException("Adaptive recovery facts exceed their limit.");
        var scope=Hash("native-adaptive-recovery-v1\n"+Path.GetFullPath(destination)+"\n"+facts);var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!,".native-fragments-"+scope[..24]);
        Folder(root);var lockPath=Path.Combine(root,"lease.lock");Regular(lockPath);FileStream? lease=null;
        try
        {
            while(lease is null){ct.ThrowIfCancellationRequested();try{Folder(root);Regular(lockPath);lease=new(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){await Task.Delay(100,ct).ConfigureAwait(false);Folder(root);Regular(lockPath);}}
            var owner=Path.Combine(root,"scope.json");Regular(owner);
            if(File.Exists(owner)){if(new FileInfo(owner).Length>1024||await File.ReadAllTextAsync(owner,ct).ConfigureAwait(false)!=JsonSerializer.Serialize(new{Schema=1,Scope=scope}))throw new InvalidDataException("Adaptive recovery ownership does not match its selected source.");}
            else
            {
                var pending=owner+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{await DurableWriteAsync(pending,Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Schema=1,Scope=scope})),ct).ConfigureAwait(false);File.Move(pending,owner);}finally{Delete(pending);}
            }
            return new(http,root,scope,lease);
        }
        catch{lease?.Dispose();throw;}
    }
    internal async Task CopyAsync(int index,Uri url,HlsByteRange? range,byte[]? inline,string staging,Func<string,CancellationToken,Task<IndexedDashDownloader.ResourceVersion?>> fetch,CancellationToken ct)
    {
        if(index is <0 or >=200000)throw new InvalidDataException("The adaptive recovery resource count exceeds its limit.");
        var name=index.ToString("D6",System.Globalization.CultureInfo.InvariantCulture);var data=Path.Combine(_root,"fragment-"+name+".bin");var receiptPath=data+".json";var incoming=Path.Combine(_root,"incoming-"+name+".bin");
        foreach(var path in new[]{data,receiptPath,incoming,incoming+".part",incoming+".part.json",incoming+".native-resume.json"})Regular(path);
        var receipt=Read(receiptPath);
        if(receipt is {Schema:1,Bytes:>0,Digest:{Length:64}}&&receipt.Scope==_scope&&receipt.Bytes<=512L*1024*1024&&Regex.IsMatch(receipt.Digest,@"\A[A-F0-9]{64}\z")&&File.Exists(data))
        {
            var fresh=inline is not null?HashBytes(inline)==receipt.Digest:await ValidateAsync(url,range,receipt,ct).ConfigureAwait(false);
            if(fresh&&await CopyVerifiedAsync(data,staging,receipt.Bytes,receipt.Digest,ct).ConfigureAwait(false))return;
        }
        var version=await fetch(incoming,ct).ConfigureAwait(false);Regular(incoming);var size=new FileInfo(incoming).Length;
        if(size is <=0 or >512L*1024*1024)throw new InvalidDataException("The adaptive checkpoint resource is empty or excessive.");
        await using(var source=new FileStream(incoming,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous))
        {
            var digest=Convert.ToHexString(await SHA256.HashDataAsync(source,ct).ConfigureAwait(false));ct.ThrowIfCancellationRequested();source.Close();
            File.Move(incoming,data,overwrite:true);var tag=EntityTagHeaderValue.TryParse(version?.ETag,out var parsed)&&!parsed.IsWeak&&parsed.Tag!="*"&&parsed.Tag.Length<=1024?parsed.ToString():null;
            var next=new Receipt(1,_scope,size,digest,tag,version?.Length,inline is not null);var temporary=receiptPath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{await DurableWriteAsync(temporary,JsonSerializer.SerializeToUtf8Bytes(next),ct).ConfigureAwait(false);File.Move(temporary,receiptPath,overwrite:true);}finally{Delete(temporary);}
            if(!await CopyVerifiedAsync(data,staging,size,digest,ct).ConfigureAwait(false))throw new IOException("A completed adaptive fragment changed before assembly.");
        }
    }
    // Only a caller which has established a fresh matching source may import an unavailable prefix.
    internal async Task CopySavedAsync(int index,string digest,string staging,CancellationToken ct)
    {
        if(index is <0 or >=200000)throw new InvalidDataException("The saved resource index is invalid.");
        var data=Path.Combine(_root,"fragment-"+index.ToString("D6",System.Globalization.CultureInfo.InvariantCulture)+".bin");
        Regular(data);var receipt=Read(data+".json");
        if(receipt is not {Schema:1,Bytes:>0}||receipt.Bytes>512L*1024*1024||receipt.Scope!=_scope||receipt.Digest!=digest||
            !File.Exists(data)||!await CopyVerifiedAsync(data,staging,receipt.Bytes,digest,ct).ConfigureAwait(false))
            throw new InvalidDataException("The saved recording prefix is missing or has changed.");
    }
    private async Task<bool> ValidateAsync(Uri url,HlsByteRange? range,Receipt receipt,CancellationToken ct)
    {
        if(!EntityTagHeaderValue.TryParse(receipt.ETag,out var tag)||tag.IsWeak||tag.Tag=="*"||tag.Tag.Length>1024||receipt.EntityBytes is not >0)return false;
        var start=range?.Offset??0;if(start<0||start>=receipt.EntityBytes||range is not null&&range.Length!=receipt.Bytes)return false;
        using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.Range=new(start,start);request.Headers.IfMatch.Add(tag);request.Headers.AcceptEncoding.Add(new("identity"));
        using var response=await _http.SendAsync(request,ct).ConfigureAwait(false);if(response.StatusCode==HttpStatusCode.PreconditionFailed)return false;response.EnsureSuccessStatusCode();
        if(response.Headers.ETag is not {IsWeak:false} current||current.ToString()!=receipt.ETag)return false;
        if(response.StatusCode==HttpStatusCode.OK)return response.Content.Headers.ContentLength==receipt.EntityBytes;
        var actual=response.Content.Headers.ContentRange;
        return response.StatusCode==HttpStatusCode.PartialContent&&actual?.Unit=="bytes"&&actual.From==start&&actual.To==start&&actual.Length==receipt.EntityBytes&&response.Content.Headers.ContentLength==1;
    }
    private static async Task<bool> CopyVerifiedAsync(string source,string target,long length,string digest,CancellationToken ct)
    {
        Regular(source);await using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
        if(input.Length!=length||Convert.ToHexString(await SHA256.HashDataAsync(input,ct).ConfigureAwait(false))!=digest)return false;
        input.Position=0;await using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous);await input.CopyToAsync(output,ct).ConfigureAwait(false);await output.FlushAsync(ct).ConfigureAwait(false);return true;
    }
    private static Receipt? Read(string path)
    {
        Regular(path);
        if(!File.Exists(path)||new FileInfo(path).Length>8192)return null;
        try{using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);return JsonSerializer.Deserialize<Receipt>(input);}catch(Exception e)when(e is JsonException or IOException){return null;}
    }
    private static string HashBytes(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    internal static async Task DurableWriteAsync(string path,byte[] bytes,CancellationToken ct)
    {await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous);await stream.WriteAsync(bytes,ct).ConfigureAwait(false);await stream.FlushAsync(ct).ConfigureAwait(false);stream.Flush(true);}
    private static void Regular(string path)
    {
        try{if((File.GetAttributes(path)&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0)throw new IOException("Adaptive recovery files must be regular owned files.");}
        catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
    }
    private static void Folder(string path)
    {
        try{var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0||(attributes&FileAttributes.Directory)==0)throw new IOException("Adaptive recovery cannot use a linked or non-directory root.");}
        catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
        Directory.CreateDirectory(path);
    }
    internal void Complete()=>_complete=true;
    private static void Delete(string path){try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
    public void Dispose()
    {
        var clear=false;
        try
        {
            foreach(var path in Directory.EnumerateFileSystemEntries(_root)){Regular(path);if(!Regex.IsMatch(Path.GetFileName(path),@"\A(?:scope\.json(?:\.[a-f0-9]{32}\.tmp)?|lease\.lock|(?:fragment|incoming)-[0-9]{6}\.bin(?:\.(?:json|part|part\.json|native-resume\.json))?(?:\.[a-f0-9]{32}\.tmp)?)\z"))return;}
            var recoverable=Directory.EnumerateFiles(_root,"fragment-*.bin.json").Any(path=>Read(path) is { } receipt&&(receipt.Inline||EntityTagHeaderValue.TryParse(receipt.ETag,out var tag)&&!tag.IsWeak&&tag.Tag!="*"));
            recoverable|=Directory.EnumerateFiles(_root,"incoming-*").Any(path=>!Path.GetFileName(path).EndsWith(".json",StringComparison.Ordinal)&&new FileInfo(path).Length>0);
            if(!_complete&&recoverable)return;
            foreach(var path in Directory.EnumerateFiles(_root).Where(p=>Path.GetFileName(p)!="lease.lock"))File.Delete(path);
            clear=true;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
        finally{_lease.Dispose();}
        if(clear)try{File.Delete(Path.Combine(_root,"lease.lock"));Directory.Delete(_root);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
    }
}
