using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;
using DesktopOrganizer.Core.UseCases;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.DependencyInjection;
using WpfApplication = System.Windows.Application;

namespace DesktopOrganizer.App.TrayIcon;

/// <summary>
/// Controlador do tray icon (TASK-019).
///
/// Responsabilidades:
/// - Criar e exibir o <see cref="TaskbarIcon"/> (H.NotifyIcon.Wpf).
/// - Construir, sob demanda, o menu de contexto com snapshots e perfis do
///   estado corrente — recriado a cada abertura para sempre refletir o disco.
/// - Disparar use cases (capturar/restaurar snapshot, ativar perfil).
/// - Liberar o NotifyIcon no <see cref="Dispose"/>.
///
/// Concorrência: a montagem do menu acessa repositórios via async; como o
/// evento <see cref="ContextMenu.Opened"/> é síncrono, fazemos o load
/// bloqueante via <c>GetAwaiter().GetResult()</c>. Os repositórios atuais
/// fazem I/O leve (JSON local), então o impacto é imperceptível e evita
/// piscar de menu vazio. Se a UX começar a engasgar, a evolução natural é
/// pré-carregar em cache invalidado por eventos de domínio.
///
/// O ícone usa <see cref="SystemIcons.Application"/> como placeholder até a
/// arte oficial chegar — H.NotifyIcon aceita qualquer <see cref="Icon"/>
/// convertido para <see cref="ImageSource"/> via <c>Imaging.CreateBitmapSourceFromHIcon</c>.
/// </summary>
internal sealed class TrayIconController : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private TaskbarIcon? _taskbarIcon;
    private bool _disposed;

    public TrayIconController(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public void Initialize()
    {
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "DesktopOrganizer",
            IconSource = BuildPlaceholderIconSource(),
            ContextMenu = new ContextMenu(),
            // Garante que o menu abra ancorado no tray e não fique órfão.
            NoLeftClickDelay = true,
        };

        // Recria o menu a cada abertura: estado de snapshots/perfis muda
        // entre cliques e queremos sempre a foto fresca do disco.
        _taskbarIcon.ContextMenu!.Opened += OnContextMenuOpened;
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        try
        {
            menu.Items.Clear();
            BuildMenuItems(menu);
        }
        catch (Exception ex)
        {
            // Não deixa exceção derrubar o popup; mostra um item de diagnóstico.
            menu.Items.Clear();
            menu.Items.Add(new MenuItem
            {
                Header = $"Erro ao montar menu: {ex.Message}",
                IsEnabled = false,
            });
        }
    }

    private void BuildMenuItems(ContextMenu menu)
    {
        var snapshotRepo = _serviceProvider.GetRequiredService<ISnapshotRepository>();
        var profileRepo = _serviceProvider.GetRequiredService<IProfileRepository>();
        var configRepo = _serviceProvider.GetRequiredService<IConfigRepository>();

        // Carrega config + listas. I/O leve (JSON local), bloqueante OK.
        var config = configRepo.LoadAsync().GetAwaiter().GetResult();
        var profiles = profileRepo.GetAllAsync().GetAwaiter().GetResult();
        var snapshotsAtivos = snapshotRepo.GetByProfileIdAsync(config.ActiveProfileId).GetAwaiter().GetResult();

        // 1) Salvar snapshot atual
        var saveItem = new MenuItem { Header = "Salvar snapshot atual" };
        saveItem.Click += async (_, _) => await CaptureSnapshotAsync().ConfigureAwait(false);
        menu.Items.Add(saveItem);

        // 2) Submenu Restaurar snapshot
        var restoreSubmenu = new MenuItem { Header = "Restaurar snapshot" };
        if (snapshotsAtivos is null || snapshotsAtivos.Count == 0)
        {
            restoreSubmenu.Items.Add(new MenuItem
            {
                Header = "(nenhum snapshot)",
                IsEnabled = false,
            });
        }
        else
        {
            // Ordena por data desc para os mais recentes ficarem no topo.
            foreach (var snap in snapshotsAtivos.OrderByDescending(s => s.CapturedAt))
            {
                var snapItem = new MenuItem
                {
                    Header = FormatSnapshotLabel(snap),
                    Tag = snap,
                };
                snapItem.Click += async (_, _) => await RestoreSnapshotAsync(snap).ConfigureAwait(false);
                restoreSubmenu.Items.Add(snapItem);
            }
        }
        menu.Items.Add(restoreSubmenu);

        menu.Items.Add(new Separator());

        // 3) Submenu Perfis
        var profilesSubmenu = new MenuItem { Header = "Perfis" };
        if (profiles is null || profiles.Count == 0)
        {
            profilesSubmenu.Items.Add(new MenuItem
            {
                Header = "(nenhum perfil)",
                IsEnabled = false,
            });
        }
        else
        {
            foreach (var profile in profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var profileItem = new MenuItem
                {
                    Header = profile.Name,
                    IsCheckable = true,
                    IsChecked = profile.Id == config.ActiveProfileId,
                    Tag = profile,
                };
                profileItem.Click += async (_, _) => await ActivateProfileAsync(profile).ConfigureAwait(false);
                profilesSubmenu.Items.Add(profileItem);
            }
        }
        menu.Items.Add(profilesSubmenu);

        menu.Items.Add(new Separator());

        // 4) Sair
        var exitItem = new MenuItem { Header = "Sair" };
        exitItem.Click += (_, _) => WpfApplication.Current?.Shutdown();
        menu.Items.Add(exitItem);
    }

    private static string FormatSnapshotLabel(Snapshot snap)
    {
        // Local time para o usuário: o snapshot guarda UTC.
        var when = snap.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        return $"{snap.Name}  ({when})";
    }

    private async Task CaptureSnapshotAsync()
    {
        // Nome auto-gerado neste passo; TASK-020 substitui por diálogo.
        var name = "Snapshot " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm");
        try
        {
            var useCase = _serviceProvider.GetRequiredService<CaptureSnapshotUseCase>();
            await useCase.ExecuteAsync(name).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShowError("Falha ao salvar snapshot", ex.Message);
        }
    }

    private async Task RestoreSnapshotAsync(Snapshot snapshot)
    {
        try
        {
            var useCase = _serviceProvider.GetRequiredService<RestoreSnapshotUseCase>();
            await useCase.ExecuteAsync(snapshot).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShowError("Falha ao restaurar snapshot", ex.Message);
        }
    }

    private async Task ActivateProfileAsync(Profile profile)
    {
        try
        {
            var useCase = _serviceProvider.GetRequiredService<ManageProfilesUseCase>();
            await useCase.ActivateProfileAsync(profile.Id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShowError("Falha ao ativar perfil", ex.Message);
        }
    }

    private void ShowError(string title, string message)
    {
        // Best-effort: balloon do tray. Em algumas versões o método assina
        // `ShowNotification`; tratamos qualquer falha silenciosamente para
        // não criar loops de erro a partir de erros.
        try
        {
            _taskbarIcon?.ShowNotification(
                title: title,
                message: message,
                icon: NotificationIcon.Error);
        }
        catch
        {
            // Sem fallback ruidoso (ex.: MessageBox) — app é tray-only.
        }
    }

    /// <summary>
    /// Constrói uma <see cref="System.Windows.Media.ImageSource"/> a partir
    /// de <see cref="SystemIcons.Application"/> como placeholder, evitando
    /// dependência de um .ico embutido até a arte oficial existir.
    /// </summary>
    private static System.Windows.Media.ImageSource BuildPlaceholderIconSource()
    {
        using var icon = SystemIcons.Application;
        return Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            System.Windows.Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            if (_taskbarIcon is not null)
            {
                if (_taskbarIcon.ContextMenu is not null)
                {
                    _taskbarIcon.ContextMenu.Opened -= OnContextMenuOpened;
                }
                _taskbarIcon.Dispose();
                _taskbarIcon = null;
            }
        }
        catch
        {
            // Shutdown best-effort: nunca propaga.
        }
    }
}
