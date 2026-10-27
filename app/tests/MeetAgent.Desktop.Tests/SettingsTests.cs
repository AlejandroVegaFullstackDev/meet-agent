using MeetAgent.Ai;
using MeetAgent.Desktop.ViewModels;

namespace MeetAgent.Desktop.Tests;

public class SettingsTests
{
    [Fact]
    public void Cambiar_ia_avisa_al_shell()
    {
        var vm = new SettingsViewModel(
            new AiSettings(new Connection(AiProvider.OpenAI, "gpt-5-mini", "sk-openai")), _ => { });
        var requested = false;
        vm.ChangeAiRequested += () => requested = true;

        Assert.Equal("OpenAI", vm.AiName);
        Assert.Equal("Conectada ✓", vm.AiStatus);

        vm.ChangeAiCommand.Execute(null);

        Assert.True(requested);
    }

    [Fact]
    public void Conectar_groq_guarda_el_transcriptor()
    {
        AiSettings? saved = null;
        var vm = new SettingsViewModel(new AiSettings(), s => saved = s);

        Assert.True(vm.NeedsTranscriber);
        Assert.True(vm.IsTranscriptionOpen);

        vm.SelectedTranscriptionOption = vm.TranscriptionOptions.Single(o => o.Kind == TranscriptionKind.Groq);
        vm.TranscriptionKey = " gsk-123 ";
        vm.SaveTranscriptionCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal(AiProvider.Groq, saved.Transcription!.Provider);
        Assert.Equal("whisper-large-v3-turbo", saved.Transcription.Model);
        Assert.Equal("gsk-123", saved.Transcription.ApiKey);
        Assert.True(vm.HasTranscriber);
        Assert.False(vm.IsTranscriptionOpen);
    }

    [Fact]
    public void Servidor_propio_pide_direccion_y_modelo()
    {
        AiSettings? saved = null;
        var vm = new SettingsViewModel(new AiSettings(), s => saved = s);
        vm.SelectedTranscriptionOption = vm.TranscriptionOptions.Single(o => o.Kind == TranscriptionKind.Server);
        vm.TranscriptionBaseUrl = "";
        vm.TranscriptionModel = "";

        vm.SaveTranscriptionCommand.Execute(null);

        Assert.Null(saved);
        Assert.Contains("dirección", vm.TranscriptionMessage);

        vm.TranscriptionBaseUrl = "http://localhost:8000/v1";
        vm.TranscriptionModel = "whisper-1";
        vm.SaveTranscriptionCommand.Execute(null);

        Assert.Equal("http://localhost:8000/v1", saved!.Transcription!.BaseUrl);
        Assert.Equal("whisper-1", saved.Transcription.Model);
    }

    [Fact]
    public void Analizar_al_terminar_y_privacidad()
    {
        AiSettings? saved = null;
        var opened = "";
        var vm = new SettingsViewModel(new AiSettings(), s => saved = s,
            dataDir: "/datos", openUrl: u => opened = u);

        vm.AnalyzeWhenMeetingEnds = false;

        Assert.False(saved!.AnalyzeWhenMeetingEnds);
        Assert.Equal("/datos", vm.DataPath);

        vm.OpenPrivacyCommand.Execute(null);

        Assert.Equal(SettingsViewModel.PrivacyUrl, opened);
    }
}
