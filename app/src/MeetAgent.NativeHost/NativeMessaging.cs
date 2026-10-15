using System.Buffers.Binary;
using System.Text;

namespace MeetAgent.NativeHost;

/// <summary>
/// Protocolo de native messaging de Chrome/Firefox: cada mensaje es un entero de 32 bits
/// (orden nativo; little-endian en x86/ARM) con el largo en bytes, seguido del JSON en UTF-8.
/// </summary>
public static class NativeMessaging
{
    /// <summary>El navegador limita los mensajes al host a 4 GB y los del host a 1 MB; aquí, 1 MB en ambos sentidos.</summary>
    public const int MaxMessageBytes = 1024 * 1024;

    /// <returns>El JSON, o null si el navegador cerró el puerto (EOF).</returns>
    public static async Task<string?> ReadAsync(Stream input, CancellationToken ct = default)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEofAsync(input, header, ct)) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxMessageBytes) throw new InvalidDataException($"Mensaje de {length} bytes fuera de rango");
        var body = new byte[length];
        if (!await ReadExactlyOrEofAsync(input, body, ct)) throw new EndOfStreamException("Mensaje cortado");
        return Encoding.UTF8.GetString(body);
    }

    public static async Task WriteAsync(Stream output, string json, CancellationToken ct = default)
    {
        var body = Encoding.UTF8.GetBytes(json);
        if (body.Length > MaxMessageBytes) throw new InvalidDataException("Respuesta demasiado grande");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await output.WriteAsync(header, ct);
        await output.WriteAsync(body, ct);
        await output.FlushAsync(ct);
    }

    private static async Task<bool> ReadExactlyOrEofAsync(Stream s, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            var n = await s.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) return read == 0 ? false : throw new EndOfStreamException("Mensaje cortado");
            read += n;
        }
        return true;
    }
}
