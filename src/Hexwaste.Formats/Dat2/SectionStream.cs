namespace Hexwaste.Formats.Dat2;

/// <summary>
/// Read-only view over a fixed [offset, offset+length) section of a parent stream.
/// Used to expose one DAT2 entry's raw bytes without loading them into memory.
/// </summary>
internal sealed class SectionStream : Stream
{
    private readonly Stream _parent;
    private readonly long _offset;
    private readonly long _length;
    private readonly bool _ownsParent;
    private long _position;

    public SectionStream(Stream parent, long offset, long length, bool ownsParent)
    {
        _parent = parent;
        _offset = offset;
        _length = length;
        _ownsParent = ownsParent;
        _parent.Seek(offset, SeekOrigin.Begin);
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        long remaining = _length - _position;
        if (remaining <= 0)
            return 0;
        if (buffer.Length > remaining)
            buffer = buffer[..(int)remaining];

        _parent.Seek(_offset + _position, SeekOrigin.Begin);
        int read = _parent.Read(buffer);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        ArgumentOutOfRangeException.ThrowIfNegative(target);
        _position = target;
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsParent)
            _parent.Dispose();
        base.Dispose(disposing);
    }
}
