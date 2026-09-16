using System.Runtime.InteropServices;
using LiveClaude.Platform.Posix.Native;

namespace LiveClaude.Platform.Posix.Pty;

/// <summary>
/// The pty master, as a stream.
///
/// Written by hand rather than wrapping the descriptor in a FileStream because two details matter and
/// FileStream gets both wrong for a terminal:
///
/// * When the last slave closes, Linux fails the next read with EIO instead of returning end of file.
///   That is the normal way a session ends, and surfacing it as an IOException turns every clean
///   shutdown into a logged error.
/// * A read interrupted by a signal returns EINTR and has to be retried; FileStream would throw.
/// </summary>
internal sealed class PtyMasterStream : Stream
{
    private readonly int _fd;
    private int _closed;

    public PtyMasterStream(int fd)
    {
        _fd = fd;
    }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override unsafe int Read(byte[] buffer, int offset, int count)
    {
        if (_closed != 0)
            return 0;

        fixed (byte* p = &buffer[offset])
        {
            while (true)
            {
                var read = Libc.read(_fd, (IntPtr)p, (nuint)count);
                if (read >= 0)
                    return (int)read;

                var error = Marshal.GetLastPInvokeError();

                if (error == Libc.EINTR)
                    continue;

                // The slave side is gone: the child exited. That is end of file here.
                if (error == Libc.EIO)
                    return 0;

                throw new IOException($"Reading the pseudo terminal failed (errno {error}).");
            }
        }
    }

    public override unsafe void Write(byte[] buffer, int offset, int count)
    {
        if (_closed != 0)
            throw new ObjectDisposedException(nameof(PtyMasterStream));

        var remaining = count;
        var position = offset;

        fixed (byte* start = buffer)
        {
            while (remaining > 0)
            {
                var written = Libc.write(_fd, (IntPtr)(start + position), (nuint)remaining);

                if (written > 0)
                {
                    position += (int)written;
                    remaining -= (int)written;
                    continue;
                }

                var error = Marshal.GetLastPInvokeError();

                if (error is Libc.EINTR or Libc.EAGAIN)
                    continue;

                throw new IOException($"Writing to the pseudo terminal failed (errno {error}).");
            }
        }
    }

    public override void Flush()
    {
        // Unbuffered: every write went straight to the descriptor.
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
            Libc.close(_fd);

        base.Dispose(disposing);
    }
}
