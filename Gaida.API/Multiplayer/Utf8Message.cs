using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;

namespace Gaida.API.Multiplayer;

/// <summary>
/// A protocol frame assembled straight into an <see cref="ArrayPool{T}" /> buffer.
/// Building one costs no string and no intermediate byte[]; the rental goes back on
/// <see cref="Dispose" />, which every send path runs in a finally.
/// </summary>
public sealed class Utf8Message : IBufferWriter<byte>, IDisposable
{
    private byte[] buffer;
    private int written;

    public Utf8Message(int capacity = 128)
    {
        buffer = ArrayPool<byte>.Shared.Rent(capacity);
    }

    /// <summary>The frame written so far. Only valid until <see cref="Dispose" />.</summary>
    public ReadOnlyMemory<byte> Memory => buffer.AsMemory(0, written);

    public void Advance(int count)
    {
        written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Reserve(sizeHint > 0 ? sizeHint : 1);
        return buffer.AsMemory(written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Reserve(sizeHint > 0 ? sizeHint : 1);
        return buffer.AsSpan(written);
    }

    public void Dispose()
    {
        var rented = buffer;
        buffer = [];
        written = 0;

        if (rented.Length > 0) ArrayPool<byte>.Shared.Return(rented);
    }

    public void Write(ReadOnlySpan<byte> value)
    {
        Reserve(value.Length);
        value.CopyTo(buffer.AsSpan(written));
        written += value.Length;
    }

    public void Write(ReadOnlySpan<char> value)
    {
        Reserve(Encoding.UTF8.GetMaxByteCount(value.Length));
        written += Encoding.UTF8.GetBytes(value, buffer.AsSpan(written));
    }

    /// <summary>
    /// Formats a value straight to UTF-8. Handing this to <see cref="Utf8.TryWrite(Span{byte},ref
    /// Utf8.TryWriteInterpolatedStringHandler,out int)" /> keeps the numeric paths on the
    /// framework's own UTF-8 formatters and keeps the culture identical to ToString().
    /// </summary>
    public void Write<T>(T value)
    {
        int count;
        Reserve(32);

        while (!Utf8.TryWrite(buffer.AsSpan(written), $"{value}", out count))
            Reserve((buffer.Length - written) * 2 + 32);

        written += count;
    }

    private void Reserve(int size)
    {
        if (written + size <= buffer.Length) return;

        var next = ArrayPool<byte>.Shared.Rent(Math.Max(written + size, buffer.Length * 2));
        buffer.AsSpan(0, written).CopyTo(next);

        if (buffer.Length > 0) ArrayPool<byte>.Shared.Return(buffer);
        buffer = next;
    }
}

/// <summary>
/// Lets a call site write <c>Broadcast($"current {index}")</c> and have the interpolation
/// land in a pooled buffer as UTF-8, instead of producing a string that then has to be
/// encoded into a second throwaway array.
/// </summary>
[InterpolatedStringHandler]
public readonly struct Utf8MessageHandler
{
    public Utf8MessageHandler(int literalLength, int formattedCount)
    {
        Message = new Utf8Message(literalLength + formattedCount * 24);
    }

    public Utf8Message Message { get; }

    public void AppendLiteral(string value)
    {
        Message.Write(value.AsSpan());
    }

    public void AppendFormatted(string? value)
    {
        Message.Write(value.AsSpan());
    }

    public void AppendFormatted(ReadOnlySpan<char> value)
    {
        Message.Write(value);
    }

    public void AppendFormatted(ReadOnlyMemory<char> value)
    {
        Message.Write(value.Span);
    }

    public void AppendFormatted<T>(T value)
    {
        Message.Write(value);
    }
}
