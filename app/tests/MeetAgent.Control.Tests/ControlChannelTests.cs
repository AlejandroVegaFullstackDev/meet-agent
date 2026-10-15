using MeetAgent.Control;

namespace MeetAgent.Control.Tests;

public class ControlChannelTests
{
    // Nombre único por prueba para que no choquen entre sí ni con la app real.
    private static string Pipe() => $"meetagent-test-{Guid.NewGuid():N}";

    [Fact]
    public async Task Ida_y_vuelta_de_un_comando()
    {
        var name = Pipe();
        await using var server = ControlServer.Start(name, req => Task.FromResult(
            new ControlResponse(Ok: true, Running: req.Command == ControlCommand.Start, MeetingId: 42, Consent: "transcribir", Message: $"recibí {req.Command} {req.Title}")));
        var client = new ControlClient(name);

        var r = await client.SendAsync(new ControlRequest(ControlCommand.Start, "Daily"), TimeSpan.FromSeconds(5));
        Assert.NotNull(r);
        Assert.True(r!.Ok);
        Assert.True(r.Running);
        Assert.Equal(42, r.MeetingId);
        Assert.Equal("recibí Start Daily", r.Message);
    }

    [Fact]
    public async Task Atiende_varias_conexiones_seguidas()
    {
        var name = Pipe();
        int calls = 0;
        await using var server = ControlServer.Start(name, _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new ControlResponse(true, false, null, null, "ok"));
        });
        var client = new ControlClient(name);
        for (int i = 0; i < 3; i++)
            Assert.NotNull(await client.SendAsync(new ControlRequest(ControlCommand.Status), TimeSpan.FromSeconds(5)));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Sin_app_abierta_devuelve_null_rapido()
    {
        var client = new ControlClient(Pipe());
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await client.SendAsync(new ControlRequest(ControlCommand.Status), TimeSpan.FromSeconds(5)));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"tardó {sw.Elapsed}");
    }

    [Fact]
    public async Task Si_el_manejador_falla_responde_error_sin_tumbar_el_servidor()
    {
        var name = Pipe();
        bool fail = true;
        await using var server = ControlServer.Start(name, _ =>
            fail ? throw new InvalidOperationException("se rompió") : Task.FromResult(new ControlResponse(true, false, null, null, "bien")));
        var client = new ControlClient(name);
        var bad = await client.SendAsync(new ControlRequest(ControlCommand.Stop), TimeSpan.FromSeconds(5));
        Assert.False(bad!.Ok);
        Assert.Contains("se rompió", bad.Message);
        fail = false;
        Assert.True((await client.SendAsync(new ControlRequest(ControlCommand.Stop), TimeSpan.FromSeconds(5)))!.Ok);
    }

    [Fact]
    public void El_protocolo_no_tiene_forma_de_pedir_grabar()
    {
        // La decisión de grabar es solo del usuario en el diálogo de la app.
        Assert.DoesNotContain(typeof(ControlRequest).GetProperties(), p => p.Name.Contains("Consent") || p.Name.Contains("Record"));
        Assert.Equal(["Status", "Start", "Stop"], Enum.GetNames<ControlCommand>());
    }
}
