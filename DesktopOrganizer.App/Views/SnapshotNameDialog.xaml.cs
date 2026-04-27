using System;
using System.Windows;
using System.Windows.Input;

namespace DesktopOrganizer.App.Views;

/// <summary>
/// Diálogo modal para captura do nome do snapshot (TASK-020).
///
/// Sugere por padrão "Snapshot YYYY-MM-DD HH:mm" (hora local) e permite
/// edição livre. Enter confirma, Esc cancela. Tamanho fixo, centralizado
/// no owner — quando houver. ShowDialog() retorna true se OK, false se
/// Cancelar/fechar.
/// </summary>
public partial class SnapshotNameDialog : Window
{
    public SnapshotNameDialog()
    {
        InitializeComponent();
        NameTextBox.Text = "Snapshot " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm");
        // Seleciona tudo para o usuário sobrescrever digitando direto.
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    /// <summary>
    /// Nome digitado pelo usuário (já trim). Só faz sentido após
    /// ShowDialog() retornar true.
    /// </summary>
    public string SnapshotName => NameTextBox.Text?.Trim() ?? string.Empty;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            // UX: bloqueia OK com nome vazio sem fechar o diálogo.
            MessageBox.Show(
                this,
                "Informe um nome para o snapshot.",
                "Nome obrigatório",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void NameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        // IsDefault no botão OK já cobre Enter, mas garantimos comportamento
        // explícito caso a TextBox em algum tema "engula" o evento.
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, e);
            e.Handled = true;
        }
    }
}
