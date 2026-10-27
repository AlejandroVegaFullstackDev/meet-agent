using Avalonia.Controls;
using Avalonia.Input;
using MeetAgent.Ai;

namespace MeetAgent.Desktop.Views;

/// <summary>
/// Permiso explícito antes de descargar el componente oficial de una cuenta. Es un diálogo de la app,
/// no una consola: el usuario solo ve qué se instala, cuánto tarda y las dos opciones.
/// </summary>
public sealed partial class AccountInstallDialog : Window
{
    public AccountInstallDialog()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => Close(false);
        InstallButton.Click += (_, _) => Close(true);
    }

    public static async Task<bool> AskAsync(Window owner, AiProvider provider)
    {
        var company = provider switch
        {
            AiProvider.ClaudeCuenta => "Anthropic",
            AiProvider.ChatGptCuenta => "OpenAI",
            _ => "Google",
        };
        var dialog = new AccountInstallDialog();
        dialog.MessageText.Text =
            $"Para usar tu cuenta debes instalar un componente oficial de {company} (unos 50 MB, 1 minuto). ¿Continuar?";
        return await dialog.ShowDialog<bool?>(owner) ?? false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
