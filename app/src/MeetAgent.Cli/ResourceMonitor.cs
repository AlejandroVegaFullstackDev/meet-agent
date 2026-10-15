using System.Diagnostics;

namespace MeetAgent.Cli;

/// <summary>Muestrea el proceso cada 250 ms: pico de memoria residente y CPU promedio.</summary>
public sealed class ResourceMonitor : IDisposable
{
    private readonly Process _self = Process.GetCurrentProcess();
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private readonly TimeSpan _cpuStart;
    private readonly Timer _timer;
    private long _peakRss;

    public ResourceMonitor()
    {
        _cpuStart = _self.TotalProcessorTime;
        _timer = new Timer(_ => Sample(), null, 0, 250);
    }

    private void Sample()
    {
        _self.Refresh();
        _peakRss = Math.Max(_peakRss, _self.WorkingSet64);
    }

    public double PeakRssMb
    {
        get
        {
            Sample();
            // En Linux PeakWorkingSet64 lee VmHWM: cubre picos entre muestras.
            return Math.Max(_peakRss, _self.PeakWorkingSet64) / 1024.0 / 1024.0;
        }
    }

    /// <summary>CPU promedio como % de la máquina completa (100 % = todos los núcleos lógicos).</summary>
    public double CpuPercentOfMachine =>
        (_self.TotalProcessorTime - _cpuStart).TotalSeconds / _wall.Elapsed.TotalSeconds / Environment.ProcessorCount * 100;

    public TimeSpan Elapsed => _wall.Elapsed;

    public void Dispose() => _timer.Dispose();
}
