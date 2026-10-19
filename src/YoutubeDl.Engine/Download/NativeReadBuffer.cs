using System.Diagnostics;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Download;

/// <summary>A response owns its read buffer; adaptation never replaces bytes still being consumed.</summary>
internal sealed class NativeReadBuffer
{
    private readonly bool _fixed;
    private readonly int _minimum;
    private readonly int _maximum;
    private byte[]? _bytes;
    private int _next;
    internal NativeReadBuffer(string? size, bool noResize)
    {
        _next = ParseSize(size); _fixed = noResize;
        _minimum = Math.Min(1024, _next); _maximum = Math.Max(4 * 1024 * 1024, _next);
    }
    internal static int ParseSize(string? value)
    {
        var size = NativeFileSizeLimits.Parse(value) ?? 1024;
        if (size < 1 || size > Array.MaxLength) throw new NativeSelectionException("The download buffer must be a positive byte size within the runtime's array capacity.");
        return (int)size;
    }
    internal async ValueTask<ReadOnlyMemory<byte>> ReadAsync(MediaHttp http, Stream source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_bytes is null || _bytes.Length != _next) _bytes = GC.AllocateUninitializedArray<byte>(_next);
        var bytes = _bytes; var started = Stopwatch.GetTimestamp();
        var count = await http.ReadAsync(source, bytes, ct).ConfigureAwait(false);
        Adapt(count, Stopwatch.GetElapsedTime(started));
        return bytes.AsMemory(0, count);
    }
    internal void Adapt(int count, TimeSpan elapsed)
    {
        if (_fixed || count <= 0) return;
        if (count == _next && elapsed <= TimeSpan.FromMilliseconds(50)) _next = (int)Math.Min(_maximum, (long)_next * 2);
        else if (elapsed >= TimeSpan.FromMilliseconds(250)) _next = Math.Max(_minimum, Math.Min(count, _next / 2));
    }
}

/// <summary>Provides the same response buffering to protocol readers that request small fields.</summary>
internal sealed class NativeBufferedReadStream(Stream source, MediaHttp http, string? size, bool noResize) : Stream
{
    private readonly NativeReadBuffer _buffer = new(size, noResize);
    private ReadOnlyMemory<byte> _pending;
    private bool _disposed;
    public override bool CanRead => !_disposed && source.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ct.ThrowIfCancellationRequested();
        if (destination.Length == 0) return 0;
        if (_pending.Length == 0) _pending = await _buffer.ReadAsync(http, source, ct).ConfigureAwait(false);
        var count = Math.Min(destination.Length, _pending.Length);
        _pending.Span[..count].CopyTo(destination.Span); _pending = _pending[count..]; return count;
    }
    protected override void Dispose(bool disposing) { _disposed = true; _pending = default; base.Dispose(disposing); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
