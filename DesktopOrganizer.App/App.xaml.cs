using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopOrganizer.App.TrayIcon;
using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;
using DesktopOrganizer.Core.UseCases;
using DesktopOrganizer.Infrastructure.Persistence;
using DesktopOrganizer.Infrastructure.Startup;
using DesktopOrganizer.Infrastructure.VirtualDesktop;
using DesktopOrganizer.Infrastructure.Win32;
using Microsoft.Extensions.DependencyInjection;
using WpfApplication = System.Windows.Application;
using WpfStartupEventArgs = System.Windows.StartupEventArgs;
using WpfExitEventArgs = System.Windows.ExitEventArgs;

namespace DesktopOrganizer.App;

/// <summary>
/// Bootstrap da aplicação WPF DesktopOrganizer.
///
/// Responsabilidades (TASK-018):
/// 1. Garantir instância única via Mutex global nomeado.
/// 2. Construir o <see cref="IServiceProvider"/> (Microsoft.Extensions.DependencyInjection)
///    com todos os serviços do Core/Infrastructure.
/// 3. Bootstrap inicial: criar perfil "default" e <c>config.json</c> na primeira execução.
/// 4. Detectar argumento <c>--boot</c> e disparar <see cref="BootRestoreUseCase"/>
///    em background (fire-and-forget) após o desktop estar pronto.
/// 5. Inicializar o <see cref="TrayIconController"/> (stub — TASK-019 implementa o real).
/// 6. Liberar Mutex e tray no <see cref="OnExit"/>.
///
/// NOTA sobre o nome do tipo: <see cref="System.Windows.WindowState"/> colide
/// com o enum <see cref="DesktopOrganizer.Core.Domain.WindowState"/>. Por isso
/// este arquivo usa aliases para <see cref="System.Windows.Application"/>
/// (<c>WpfApplication</c>) e elimina o <c>using System.Windows;</c> direto.
/// </summary>
public partial class App : WpfApplication
{
    /// <summary>
    /// Nome do mutex global que garante instância única em todo o sistema.
    /// O prefixo <c>Global\</c> assegura que o mutex valha entre sessões de
    /// usuário; trocar para <c>Local\</c> se for desejado escopo por sessão.
    /// </summary>
    private const string SingleInstanceMutexName = "DesktopOrganizer-SingleInstance-Mutex";

    /// <summary>
    /// Argumento de linha de comando que sinaliza execução automática no boot
    /// (registrada em <c>HKCU\...\Run</c>). Quando presente, dispara
    /// <see cref="BootRestoreUseCase"/> após o startup.
    /// </summary>
    private const string BootArgument = "--boot";

    private Mutex? _singleInstanceMutex;
    private ServiceProvider? _serviceProvider;
    private TrayIconController? _trayController;

    protected override async void OnStartup(WpfStartupEventArgs e)
    {
        base.OnStartup(e);

        // 0) Verificação de versão mínima do Windows (L1: Windows 11 = build 22000+).
        if (Environment.OSVersion.Version.Build < 22000)
        {
            System.Windows.MessageBox.Show(
                "DesktopOrganizer requer Windows 11 ou superior.",
                "Sistema incompatível",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Current.Shutdown();
            return;
        }

        // 1) Single-instance guard. Se outra instância já segura o mutex,
        // encerramos sem inicializar nada — sem mensagem ruidosa.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            // Não somos donos do mutex; liberamos a referência e saímos.
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Current.Shutdown();
            return;
        }

        // 2) Container de DI.
        _serviceProvider = BuildServiceProvider();

        // 3) Bootstrap de primeira execução (config + perfil default).
        await EnsureDefaultProfileAndConfigAsync(_serviceProvider).ConfigureAwait(true);

        // 4) Tray icon (stub na TASK-018; real na TASK-019).
        _trayController = new TrayIconController(_serviceProvider);
        _trayController.Initialize();

        // 5) Modo --boot: dispara restauração em background, sem bloquear o startup.
        if (e.Args is not null && e.Args.Any(a => string.Equals(a, BootArgument, StringComparison.OrdinalIgnoreCase)))
        {
            var sp = _serviceProvider;
            _ = Task.Run(async () =>
            {
                try
                {
                    var bootUseCase = sp.GetRequiredService<BootRestoreUseCase>();
                    await bootUseCase.ExecuteAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Fire-and-forget: falhas no boot não devem derrubar a aplicação.
                    // Logging real fica para iteração futura.
                }
            });
        }
    }

    protected override void OnExit(WpfExitEventArgs e)
    {
        // Ordem de teardown: tray primeiro (depende do SP), depois SP, depois mutex.
        try
        {
            _trayController?.Dispose();
        }
        catch
        {
            // Não propagar exceção no shutdown.
        }

        try
        {
            _serviceProvider?.Dispose();
        }
        catch
        {
            // idem.
        }

        try
        {
            if (_singleInstanceMutex is not null)
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
        }
        catch
        {
            // ReleaseMutex pode lançar se já foi solto; ignorar no shutdown.
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Monta o container de DI registrando todas as implementações da
    /// camada de Infraestrutura para as interfaces do Core, mais os use
    /// cases.
    ///
    /// Estratégia de tempo de vida:
    /// - Repositórios e serviços com estado interno mínimo / I/O leve são
    ///   <b>singletons</b> — uma instância por aplicação.
    /// - Use cases são <b>transient</b> — leves e baratos de instanciar,
    ///   evita compartilhamento acidental de estado entre execuções.
    /// </summary>
    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // --- Persistência ---------------------------------------------------
        services.AddSingleton<AppDataPaths>();
        services.AddSingleton<IProfileRepository, JsonProfileRepository>();
        services.AddSingleton<ISnapshotRepository, JsonSnapshotRepository>();
        services.AddSingleton<IConfigRepository, JsonConfigRepository>();

        // --- Startup (Registry) ---------------------------------------------
        services.AddSingleton<IStartupService, RegistryStartupService>();

        // --- Virtual Desktop -------------------------------------------------
        // Tenta a implementação COM real; se IsAvailable == false, registra o stub.
        // A inicialização do COM acontece no construtor do Impl — qualquer falha
        // já estará refletida em IsAvailable e não vaza exceção.
        services.AddSingleton<IVirtualDesktopManager>(_ =>
        {
            try
            {
                var comImpl = new VirtualDesktopManagerImpl();
                if (comImpl.IsAvailable)
                {
                    return comImpl;
                }
            }
            catch
            {
                // Se a construção lançou (por algum motivo extremo), cai no stub.
            }
            return new VirtualDesktopStub();
        });

        // --- Win32 -----------------------------------------------------------
        services.AddSingleton<IWindowCaptureService, Win32WindowCaptureService>();
        services.AddSingleton<IWindowRestoreService, Win32WindowRestoreService>();

        // --- Tray notification ----------------------------------------------
        // TrayNotificationService precisa ser resolvido tanto pela interface
        // (consumido pelos use cases) quanto pelo tipo concreto (consumido
        // pelo TrayIconController, que chama Bind após criar o TaskbarIcon).
        // Registramos a instância concreta como singleton e mapeamos a
        // interface para a MESMA instância via factory.
        services.AddSingleton<TrayNotificationService>();
        services.AddSingleton<ITrayNotificationService>(sp => sp.GetRequiredService<TrayNotificationService>());

        // --- Use cases (transient: state-free, baratos) ---------------------
        services.AddTransient<CaptureSnapshotUseCase>();
        services.AddTransient<RestoreSnapshotUseCase>();
        services.AddTransient<BootRestoreUseCase>();
        services.AddTransient<ManageProfilesUseCase>();

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// Garante que existe um perfil "default" e um <c>config.json</c> na
    /// primeira execução. Critério: <see cref="AppConfig.ActiveProfileId"/>
    /// igual a <see cref="Guid.Empty"/> indica config recém-criado pelos
    /// defaults do <see cref="JsonConfigRepository"/>.
    /// </summary>
    private static async Task EnsureDefaultProfileAndConfigAsync(IServiceProvider sp)
    {
        var configRepo = sp.GetRequiredService<IConfigRepository>();
        var config = await configRepo.LoadAsync().ConfigureAwait(false);

        if (config.ActiveProfileId != Guid.Empty)
        {
            return;
        }

        var profileRepo = sp.GetRequiredService<IProfileRepository>();
        var defaultProfile = new Profile
        {
            Id = Guid.NewGuid(),
            Name = "default",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await profileRepo.SaveAsync(defaultProfile).ConfigureAwait(false);

        config.ActiveProfileId = defaultProfile.Id;
        await configRepo.SaveAsync(config).ConfigureAwait(false);
    }
}
