using System.Security.Cryptography;

namespace MusicHub.Api.Ingestion;

/// <summary>
/// Wraps a readable stream and accumulates a SHA-256 hash of every byte read through it.
/// Call <see cref="GetHashHex"/> after the stream is fully consumed.
/// </summary>
internal sealed class Sha256HashingStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private byte[]? _finalHash;

    public Sha256HashingStream(Stream inner) => _inner = inner;

    /// <summary>Number of bytes read through this stream so far.</summary>
    public long BytesRead { get; private set; }

    /// <summary>
    /// The exception the wrapped source threw while being read, if any. It tells a failing source
    /// (the client's request body) from a failure in whatever consumes this stream (storage).
    /// </summary>
    public Exception? SourceError { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n;
        try { n = _inner.Read(buffer, offset, count); }
        catch (Exception ex) { SourceError = ex; throw; }
        Record(buffer.AsSpan(offset, n));
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int n;
        try { n = await _inner.ReadAsync(buffer, offset, count, cancellationToken); }
        catch (Exception ex) { SourceError = ex; throw; }
        Record(buffer.AsSpan(offset, n));
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int n;
        try { n = await _inner.ReadAsync(buffer, cancellationToken); }
        catch (Exception ex) { SourceError = ex; throw; }
        Record(buffer.Span[..n]);
        return n;
    }

    // Hash and byte count must advance together, so every read path goes through here.
    private void Record(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return;
        _hasher.AppendData(data);
        BytesRead += data.Length;
    }

    /// <summary>
    /// Returns the lowercase hex SHA-256 of all bytes read so far. Safe to call multiple times.
    /// </summary>
    public string GetHashHex()
    {
        _finalHash ??= _hasher.GetHashAndReset();
        return Convert.ToHexString(_finalHash).ToLowerInvariant();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hasher.Dispose();
        base.Dispose(disposing);
    }
}
