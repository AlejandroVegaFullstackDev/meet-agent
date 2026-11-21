namespace MeetAgent.Ai;

/// <summary>
/// Escribe archivos con secretos (<c>ia.json</c>, <c>chatgpt.json</c>): primero a un temporal y luego lo
/// reemplaza, para no dejar el archivo a medias si la app se cierra. Fuera de Windows el temporal nace ya
/// con permisos 600, así que nunca hay un momento en que otro usuario pueda leerlo.
/// </summary>
internal static class PrivateFile
{
    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.Delete(temp); // UnixCreateMode solo se aplica al crear el archivo
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using (var writer = new StreamWriter(temp, System.Text.Encoding.UTF8, options))
                writer.Write(text);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { }
            throw;
        }
    }
}
