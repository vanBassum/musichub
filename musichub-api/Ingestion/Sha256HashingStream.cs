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
        var n = _inner.Read(buffer, offset, count);
        if (n > 0) _hasher.AppendData(buffer, offset, n);
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var n = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
        if (n > 0) { _hasher.AppendData(buffer, offset, n); BytesRead += n; }
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await _inner.ReadAsync(buffer, cancellationToken);
        if (n > 0) { _hasher.AppendData(buffer.Span[..n]); BytesRead += n; }
        return n;
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
