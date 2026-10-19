using System.Buffers.Binary;
using System.Security.Cryptography;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Media;

internal sealed class LiveCaptionBudget(long maximum=32*1024*1024)
{
    private long _received;
    internal long Remaining=>Math.Max(0,maximum-Interlocked.Read(ref _received));
    internal void Record(int count){if(Interlocked.Add(ref _received,count)>maximum)throw new InvalidDataException("The selected live captions exceed their aggregate input limit.");}
}
internal sealed class LiveCaptionResources(MediaHttp http,NativeDownloadRequest request,ByteRateLimiter limiter,LiveCaptionBudget? budget=null):IDisposable
{
    private readonly Dictionary<Uri,IndexedDashDownloader.ResourceVersion> _versions=new();
    private readonly Dictionary<Uri,byte[]> _keys=new();private long _received;
    internal async Task<byte[]> LoadAsync(Uri url,HlsByteRange? range,HlsEncryption? encryption,ulong sequence,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();url=http.CanonicalizePlaybackUri(url);var remaining=Math.Min(8*1024*1024-_received,budget?.Remaining??long.MaxValue);if(remaining<=0)throw new InvalidDataException("Live captions exceed their input limit.");
        byte[] bytes;
        if(range is not null)
        {
            using var memory=new MemoryStream();_versions[url]=await IndexedDashDownloader.CopyRangeAsync(http,url,range,memory,_versions.GetValueOrDefault(url),limiter,request.FragmentRetries??request.Retries,remaining,ct,request.BufferSize,request.NoResizeBuffer,request.HttpChunkSize).ConfigureAwait(false);bytes=memory.ToArray();
        }
        else
        {
            using var message=new HttpRequestMessage(HttpMethod.Get,url);using var response=await http.SendAsync(message,ct).ConfigureAwait(false);response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength>remaining)throw new InvalidDataException("A live caption resource exceeds its input limit.");
            await using var input=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);using var memory=new MemoryStream();var buffer=new byte[8192];
            while(true){var count=await http.ReadAsync(input,buffer,ct).ConfigureAwait(false);if(count==0)break;if(memory.Length+count>remaining)throw new InvalidDataException("Live captions exceed their input limit.");await limiter.ThrottleAsync(count,ct).ConfigureAwait(false);memory.Write(buffer,0,count);}
            if(response.Content.Headers.ContentLength is { } expected&&memory.Length!=expected)throw new EndOfStreamException("The live caption resource was incomplete.");bytes=memory.ToArray();
        }
        _received=checked(_received+bytes.Length);
        budget?.Record(bytes.Length);
        if(encryption is null)return bytes;
        if(!_keys.TryGetValue(encryption.KeyUrl,out var key)){key=await http.GetBytesAsync(encryption.KeyUrl,16,ct).ConfigureAwait(false);if(key.Length!=16)throw new InvalidDataException("The live caption key is not128bits.");_keys.Add(encryption.KeyUrl,key);}
        var iv=encryption.IV??new byte[16];if(encryption.IV is null)BinaryPrimitives.WriteUInt64BigEndian(iv.AsSpan(8),sequence);
        using var aes=Aes.Create();aes.Key=key;return aes.DecryptCbc(bytes,iv,PaddingMode.PKCS7);
    }
    internal void RefreshKeys(){foreach(var key in _keys.Values)CryptographicOperations.ZeroMemory(key);_keys.Clear();}
    internal void RestoreBytes(int count)
    {
        if(count<0||count>8*1024*1024-_received)throw new InvalidDataException("Restored live captions exceed their input limit.");
        _received+=count;budget?.Record(count);
    }
    public void Dispose()=>RefreshKeys();
}
