using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DesktopOrganizer.App.Views;
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
            // Icon (System.Drawing.Icon) — passa direto sem conversão ImageSource,
            // evitando bug de InteropBitmap/UriSource em H.NotifyIcon 2.4.1.
            Icon = (System.Drawing.Icon)SystemIcons.Application.Clone(),
            ContextMenu = new ContextMenu(),
            NoLeftClickDelay = true,
        };

        // ForceCreate garante registro do NotifyIcon no Win32 imediatamente
        // (sem isso, ícone pode não aparecer dependendo de quando o dispatcher idle).
        _taskbarIcon.ForceCreate(enablesEfficiencyMode: false);

        _taskbarIcon.ContextMenu!.Opened += OnContextMenuOpened;

        var notifications = _serviceProvider.GetRequiredService<TrayNotificationService>();
        notifications.Bind(_taskbarIcon);
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

        // 4) Restaurar ao iniciar Windows (TASK-022)
        // Estado lido em tempo real do Registry via IStartupService — não
        // confiamos apenas em AppConfig.StartupEnabled porque o usuário pode
        // editar a chave do Registry por fora; a config é só espelho persistido.
        var startupService = _serviceProvider.GetRequiredService<IStartupService>();
        var startupItem = new MenuItem
        {
            Header = "Restaurar ao iniciar Windows",
            IsCheckable = true,
            IsChecked = startupService.IsEnabled(),
        };
        startupItem.Click += async (_, _) => await ToggleStartupAsync().ConfigureAwait(false);
        menu.Items.Add(startupItem);

        menu.Items.Add(new Separator());

        // 5) Sair
        var exitItem = new MenuItem { Header = "Sair" };
        exitItem.Click += (_, _) => WpfApplication.Current?.Shutdown();
        menu.Items.Add(exitItem);
    }

    private async Task ToggleStartupAsync()
    {
        try
        {
            var startupService = _serviceProvider.GetRequiredService<IStartupService>();
            var configRepo = _serviceProvider.GetRequiredService<IConfigRepository>();

            // Estado ANTES do toggle: WPF já alterou o IsChecked do MenuItem,
            // mas a fonte da verdade aqui é o Registry — se IsEnabled() == true
            // significa que a chave existe e queremos removê-la (toggle off).
            var currentlyEnabled = startupService.IsEnabled();

            if (currentlyEnabled)
            {
                startupService.Disable();
            }
            else
            {
                var executablePath = ResolveExecutablePath();
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    ShowError(
                        "Falha ao habilitar inicialização",
                        "Não foi possível resolver o caminho do executável.");
                    return;
                }
                startupService.Enable(executablePath);
            }

            // Persiste o espelho na AppConfig para refletir o estado autoritativo
            // do Registry após a operação.
            var config = await configRepo.LoadAsync().ConfigureAwait(false);
            config.StartupEnabled = startupService.IsEnabled();
            await configRepo.SaveAsync(config).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShowError("Falha ao alterar inicialização com Windows", ex.Message);
        }
    }

    /// <summary>
    /// Resolve o caminho do executável atual. Em apps publicados como
    /// single-file ou self-contained, <c>Process.MainModule.FileName</c>
    /// aponta para o .exe correto (não para o dotnet host), que é o que
    /// queremos registrar no autostart.
    /// </summary>
    private static string ResolveExecutablePath()
    {
        try
        {
            var mainModule = Process.GetCurrentProcess().MainModule;
            var path = mainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path!;
            }
        }
        catch
        {
            // Fallback abaixo cobre cenários onde MainModule é inacessível
            // (raro, mas pode ocorrer em sandboxes).
        }
        return Environment.ProcessPath ?? string.Empty;
    }

    private static string FormatSnapshotLabel(Snapshot snap)
    {
        // Local time para o usuário: o snapshot guarda UTC.
        var when = snap.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        return $"{snap.Name}  ({when})";
    }

    private async Task CaptureSnapshotAsync()
    {
        // TASK-020: pede o nome via diálogo modal. ShowDialog precisa rodar
        // no UI thread; este handler já é invocado pela UI (Click no menu),
        // então estamos no thread certo.
        string name;
        try
        {
            var dlg = new SnapshotNameDialog();
            // App é tray-only, sem MainWindow visível. Tentar setar Owner numa
            // janela não-shown lança "Cannot set Owner Property to a Window that
            // has not been shown previously". Só atribui se houver janela exibida.
            var ownerCandidate = WpfApplication.Current?.Windows
                .OfType<System.Windows.Window>()
                .FirstOrDefault(w => w.IsLoaded && w.IsVisible);
            if (ownerCandidate is not null)
            {
                dlg.Owner = ownerCandidate;
            }
            else
            {
                dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
                dlg.Topmost = true;
            }
            if (dlg.ShowDialog() != true)
            {
                // Usuário cancelou — aborta captura silenciosamente.
                return;
            }
            name = dlg.SnapshotName;
        }
        catch (Exception ex)
        {
            ShowError("Falha ao abrir diálogo de snapshot", ex.Message);
            return;
        }

        try
        {
            var useCase = _serviceProvider.GetRequiredService<CaptureSnapshotUseCase>();
            await useCase.ExecuteAsync(name).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShowError("Falha ao salvar snapshot", ex.Message);
        }

        // TODO: deleção de perfil — reutilizar MessageBox.Show nativo
        // (MessageBoxButton.YesNo + MessageBoxImage.Warning). Capacidade
        // ainda não exposta no menu; tratar em iteração futura.
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
        // H.NotifyIcon exige BitmapImage com UriSource (não StreamSource).
        // Escrevemos um .ico do SystemIcons.Application em arquivo temp e
        // retornamos BitmapImage apontando pra ele.
        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "DesktopOrganizer-tray-icon.ico");

        if (!System.IO.File.Exists(tempPath))
        {
            using var fs = new System.IO.FileStream(tempPath, System.IO.FileMode.Create);
            SystemIcons.Application.Save(fs);
        }

        var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bitmapImage.UriSource = new Uri(tempPath, UriKind.Absolute);
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
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
            // Solta a referência ao ícone no notification service ANTES de
            // dispor o TaskbarIcon, evitando que uma notificação tardia
            // tente usar handle inválido.
            try
            {
                var notifications = _serviceProvider.GetService<TrayNotificationService>();
                notifications?.Unbind();
            }
            catch
            {
                // Service provider pode já ter sido disposto; ignorar.
            }

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
