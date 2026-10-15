using MeetAgent.Audio.Linux;
using MeetAgent.Audio.Windows;
using MeetAgent.Core.Ports;

namespace MeetAgent.Cli;

public static class Platform
{
    public static IAudioSourceFactory AudioSources() =>
        OperatingSystem.IsWindows() ? new WindowsAudioSourceFactory()
        : OperatingSystem.IsLinux() ? new LinuxAudioSourceFactory()
        : throw new PlatformNotSupportedException("Solo Windows y Linux.");
}
