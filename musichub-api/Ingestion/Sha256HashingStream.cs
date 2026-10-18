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
    private bool _hasherDisposed;

    public Sha256HashingStream(Stream inner) => _inner = inner;

    /// <summary>Number of bytes read through this stream so far.</summary>
    public long BytesRead { get; private set; }

    /// <summary>True once a read returned 0 bytes: the whole inner stream was consumed.</summary>
    public bool ReachedEnd { get; private set; }

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
        Record(buffer.AsSpan(offset, n), count);
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var n = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
        Record(buffer.AsSpan(offset, n), count);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await _inner.ReadAsync(buffer, cancellationToken);
        Record(buffer.Span[..n], buffer.Length);
        return n;
    }

    // Hash and byte count must advance together, so every read path goes through here.
    private void Record(ReadOnlySpan<byte> data, int requested)
    {
        if (data.IsEmpty)
        {
            if (requested > 0) ReachedEnd = true;
            return;
        }
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
        // The storage client may dispose the stream it was given before PutAsync returns, and the caller
        // still reads the hash afterwards. Capture it before releasing the hasher.
        if (disposing && !_hasherDisposed)
        {
            _finalHash ??= _hasher.GetHashAndReset();
            _hasher.Dispose();
            _hasherDisposed = true;
        }
        base.Dispose(disposing);
    }
}
