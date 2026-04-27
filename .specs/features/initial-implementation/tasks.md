# Tarefas — DesktopOrganizer: Implementação Inicial

## Resumo

- Total de tarefas: 26
- Tarefas paralelizáveis: 18
- Caminho crítico estimado: TASK-001 → TASK-002 → TASK-003 → TASK-007 → TASK-008 → TASK-009 → TASK-013 → TASK-014 → TASK-020 → TASK-024 → TASK-025 → TASK-026

### Distribuição por Risco
- Crítico: 0
- Alto: 11
- Médio: 12
- Baixo: 3

### Distribuição por QA
- full: 11
- wave: 12
- smoke: 3
- auto: 0

### Distribuição por Perfil
- frontend: 3
- backend: 20
- infra: 3
- misto: 0

## Legenda

- `[P]` = Paralelizável com outras `[P]` que não compartilham arquivos
- Esforço: S / M / L
- Tipo: lógica-negócio | crud-padrão | ui-puro | integração-externa | migration | config | refactor | infra | teste
- Risco: crítico | alto | médio | baixo
- QA: full | wave | smoke | auto
- Perfil: frontend | backend | infra | misto

---

## Tarefas

### TASK-001 — Estrutura de solução e projetos .NET

- **Esforço**: S
- **Paralelizável**: Não
- **Depende de**: —
- **Tipo**: config
- **Risco**: médio
- **QA**: wave
- **Perfil**: infra
- **Arquivos**:
  - `DesktopOrganizer.sln`
  - `DesktopOrganizer.App/DesktopOrganizer.App.csproj`
- **Descrição**: Criar a solução .NET 8 com os quatro projetos: `DesktopOrganizer.App` (WPF), `DesktopOrganizer.Core` (class library), `DesktopOrganizer.Infrastructure` (class library), `DesktopOrganizer.Installer` (placeholder WiX). Configurar referências entre projetos (App → Core, App → Infrastructure, Infrastructure → Core). Adicionar NuGet `H.NotifyIcon.Wpf` no App.
- **Critério de verificação**: `dotnet build DesktopOrganizer.sln` conclui sem erros; os quatro projetos aparecem na solução.

---

### TASK-002 — Modelos de domínio (Core)

- **Esforço**: S
- **Paralelizável**: Não
- **Depende de**: TASK-001
- **Tipo**: lógica-negócio
- **Risco**: alto
- **QA**: full
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Core/Domain/Snapshot.cs`
  - `DesktopOrganizer.Core/Domain/WindowEntry.cs`
- **Descrição**: Implementar as entidades `Snapshot` (agregado raiz com Id, Name, ProfileId, CapturedAt, Windows) e `WindowEntry` (Id, ProcessName, ExecutablePath, WindowTitle, X, Y, Width, Height, WindowState, MonitorDeviceName, VirtualDesktopId) com enumeração `WindowState` (Normal/Maximized/Minimized). Sem dependências externas — C# puro.
- **Critério de verificação**: Projeto Core compila; instâncias das classes podem ser criadas e serializadas via `System.Text.Json` em teste manual ou unitário.

---

### TASK-003 [P] — Modelos de domínio: Profile e AppConfig (Core)

- **Esforço**: S
- **Paralelizável**: Sim
- **Depende de**: TASK-001
- **Tipo**: lógica-negócio
- **Risco**: médio
- **QA**: wave
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Core/Domain/Profile.cs`
  - `DesktopOrganizer.Core/Domain/AppConfig.cs`
- **Descrição**: Implementar `Profile` (Id, Name, CreatedAt) e `AppConfig` (ActiveProfileId, DefaultBootSnapshotId, StartupEnabled, RestoreTimeoutSeconds com default 30). Value objects puros sem dependências externas.
- **Critério de verificação**: Projeto Core compila; `AppConfig` inicializa com `RestoreTimeoutSeconds = 30` por padrão.

---

### TASK-004 [P] — Value objects auxiliares e interfaces de serviço (Core)

- **Esforço**: S
- **Paralelizável**: Sim
- **Depende de**: TASK-001
- **Tipo**: lógica-negócio
- **Risco**: médio
- **QA**: wave
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Core/Domain/MonitorInfo.cs`
  - `DesktopOrganizer.Core/Services/IWindowCaptureService.cs`
- **Descrição**: Implementar `MonitorInfo` (DeviceName, Bounds) e `VirtualDesktopId` (wrapper sobre Guid). Declarar as interfaces `IWindowCaptureService`, `IWindowRestoreService`, `ISnapshotRepository`, `IProfileRepository`, `IStartupService`, `ITrayNotificationService` e `IVirtualDesktopManager` com as assinaturas exatas definidas no plano (Seção 5). Também declarar `RestoreResult` com campos Status (enum: Success/Skipped/Failed) e Message.
- **Critério de verificação**: Projeto Core compila sem erros; todas as interfaces estão declaradas no namespace correto.

---

### TASK-005 [P] — Declarações P/Invoke Win32 (Infrastructure)

- **Esforço**: M
- **Paralelizável**: Sim
- **Depende de**: TASK-001
- **Tipo**: integração-externa
- **Risco**: alto
- **QA**: full
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Infrastructure/Win32/Win32Interop.cs`
  - `DesktopOrganizer.Infrastructure/Win32/NativeStructs.cs`
- **Descrição**: Declarar todas as assinaturas P/Invoke necessárias: `EnumWindows`, `IsWindowVisible`, `GetWindowRect`, `GetWindowPlacement`, `GetWindowThreadProcessId`, `QueryFullProcessImageName`, `SetWindowPos`, `ShowWindow`, `MonitorFromWindow`, `GetMonitorInfo`. Definir structs nativas: `RECT`, `WINDOWPLACEMENT`, `MONITORINFOEX`. Usar atributos `[DllImport]` corretos com tipos `IntPtr` e marshaling explícito. Nenhuma lógica de negócio neste arquivo.
- **Critério de verificação**: Projeto Infrastructure compila; nenhum aviso de marshaling incorreto. Pode-se chamar `IsWindowVisible(IntPtr.Zero)` sem exceção de compilação.

---

### TASK-006 [P] — Caminhos de persistência e configuração de diretórios (Infrastructure)

- **Esforço**: S
- **Paralelizável**: Sim
- **Depende de**: TASK-001
- **Tipo**: config
- **Risco**: baixo
- **QA**: smoke
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Infrastructure/Persistence/AppDataPaths.cs`
  - `DesktopOrganizer.Infrastructure/Persistence/JsonProfileRepository.cs`
- **Descrição**: Implementar `AppDataPaths` que resolve `%APPDATA%\DesktopOrganizer\` e os subdiretórios `profiles\[nome]\snapshots\`. Implementar `JsonProfileRepository` com os métodos `SaveAsync`, `GetAllAsync` e `DeleteAsync` (deleção em cascata dos snapshots do perfil). Criar diretórios automaticamente se não existirem. Usar `System.Text.Json`.
- **Critério de verificação**: Executar o método `SaveAsync` com um `Profile` de teste → arquivo `profile.json` criado no diretório correto dentro de `%APPDATA%\DesktopOrganizer\profiles\`.

---

### TASK-007 [P] — JsonSnapshotRepository (Infrastructure)

- **Esforço**: S
- **Paralelizável**: Sim
- **Depende de**: TASK-002, TASK-006
- **Tipo**: crud-padrão
- **Risco**: médio
- **QA**: wave
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Infrastructure/Persistence/JsonSnapshotRepository.cs`
  - `DesktopOrganizer.Infrastructure/Persistence/AppDataPaths.cs`
- **Descrição**: Implementar `JsonSnapshotRepository` com `SaveAsync`, `GetByIdAsync`, `GetByProfileIdAsync` e `DeleteAsync`. Cada snapshot é salvo em `profiles\[profileId]\snapshots\snapshot-[id].json`. Tratar corrupção de arquivo (arquivo JSON inválido → logar e ignorar entrada, não abortar enumeração).
- **Critério de verificação**: Salvar um `Snapshot` com dois `WindowEntry` → ler de volta pelo id → campos coincidem; deletar → arquivo removido do disco.

---

### TASK-008 [P] — RegistryStartupService (Infrastructure)

- **Esforço**: S
- **Paralelizável**: Sim
- **Depende de**: TASK-004
- **Tipo**: integração-externa
- **Risco**: alto
- **QA**: full
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Infrastructure/Startup/RegistryStartupService.cs`
  - `DesktopOrganizer.Core/Services/IStartupService.cs`
- **Descrição**: Implementar `RegistryStartupService` que escreve/lê/remove a chave `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DesktopOrganizer` com o caminho do executável. Métodos: `Enable(string executablePath)`, `Disable()`, `IsEnabled()`. Usar `Microsoft.Win32.Registry` (incluso no .NET 8).
- **Critério de verificação**: Chamar `Enable(path)` → verificar no regedit que a chave existe; chamar `Disable()` → chave removida; `IsEnabled()` retorna valor correto em ambos os casos.

---

### TASK-009 [P] — VirtualDesktopStub e interface IVirtualDesktopManager (Infrastructure)

- **Esforço**: S
- **Paralelizável**: Sim
- **Depende de**: TASK-004
- **Tipo**: lógica-negócio
- **Risco**: médio
- **QA**: wave
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Infrastructure/VirtualDesktop/VirtualDesktopStub.cs`
  - `DesktopOrganizer.Infrastructure/VirtualDesktop/IVirtualDesktopManager.cs`
- **Descrição**: Declarar `IVirtualDesktopManager` com `GetWindowDesktopId(IntPtr hwnd)` e `MoveWindowToDesktop(IntPtr hwnd, Guid desktopId)` no namespace Infrastructure (pode ser cópia da interface do Core ou referência). Implementar `VirtualDesktopStub` (noop): `GetWindowDesktopId` retorna `null`, `MoveWindowToDesktop` retorna `false` sem lançar exceção. Este stub é o fallback quando as COM APIs não estiverem disponíveis.
- **Critério de verificação**: `VirtualDesktopStub` instanciado e ambos os métodos chamados sem exceção; retornos correspondem aos valores documentados.

---

### TASK-010 — VirtualDesktopManagerImpl via COM (Infrastructure)

- **Esforço**: L
- **Paralelizável**: Não
- **Depende de**: TASK-009
- **Tipo**: integração-externa
- **Risco**: alto
- **QA**: full
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Infrastructure/VirtualDesktop/VirtualDesktopManagerImpl.cs`
  - `DesktopOrganizer.Infrastructure/VirtualDesktop/VirtualDesktopInterfaces.cs`
- **Descrição**: Implementar `VirtualDesktopManagerImpl` usando as interfaces COM internas do Windows (`IVirtualDesktopManager` COM, obtidas via `IServiceProvider`/`IObjectArray` conforme referência da comunidade, ex: biblioteca `VirtualDesktop` de Markus Gaßner). Implementar `GetWindowDesktopId` e `MoveWindowToDesktop`. Envolver toda a inicialização COM em try/catch: se falhar, registrar aviso e sinalizar que o Stub deve ser usado. Compatibilidade alvo: Windows 10 1903+ e Windows 11.
- **Critério de verificação**: Em máquina com Windows 10/11 com 2+ virtual desktops, `GetWindowDesktopId` retorna Guid diferente de null para janelas em desktops distintos; `MoveWindowToDesktop` move a janela visualmente para o desktop correto.

---

### TASK-011 [P] — Win32WindowCaptureService (Infrastructure)

- **Esforço**: M
- **Paralelizável**: Sim
- **Depende de**: TASK-005, TASK-002, TASK-009
- **Tipo**: integração-externa
- **Risco**: alto
- **QA**: full
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Infrastructure/Win32/Win32WindowCaptureService.cs`
  - `DesktopOrganizer.Infrastructure/Win32/Win32Interop.cs`
- **Descrição**: Implementar `Win32WindowCaptureService.CaptureAllWindowsAsync()`. Usar `EnumWindows` para enumerar janelas de nível superior; filtrar com `IsWindowVisible`; para cada janela: obter `GetWindowRect`/`GetWindowPlacement` (posição, tamanho, estado), `GetWindowThreadProcessId` + `QueryFullProcessImageName` (executável), `MonitorFromWindow`+`GetMonitorInfo` (monitor), `IVirtualDesktopManager.GetWindowDesktopId` (virtual desktop). Ignorar janelas sem executável acessível (capturar exceção, continuar). Retornar `IReadOnlyList<WindowEntry>`.
- **Critério de verificação**: Executar `CaptureAllWindowsAsync()` com Notepad e VS Code abertos → resultado contém ambos os processos com campos `ExecutablePath`, `X`, `Y`, `Width`, `Height`, `WindowState` e `MonitorDeviceName` preenchidos corretamente.

---

### TASK-012 [P] — Win32WindowRestoreService: lançamento e polling de processo (Infrastructure)

- **Esforço**: M
- **Paralelizável**: Sim
- **Depende de**: TASK-005, TASK-004
- **Tipo**: lógica-negócio
- **Risco**: alto
- **QA**: full
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Infrastructure/Win32/Win32WindowRestoreService.cs`
  - `DesktopOrganizer.Infrastructure/Win32/Win32Interop.cs`
- **Descrição**: Implementar a parte de lançamento e espera de `Win32WindowRestoreService.RestoreWindowAsync`: (1) verificar se processo já está aberto (match por ExecutablePath + WindowTitle com fallback para correspondência parcial); (2) se não, `Process.Start(ExecutablePath)`; (3) aguardar janela principal com polling de 500ms e `CancellationToken` com timeout configurável (default 30s); (4) em caso de timeout, retornar `RestoreResult.Failed(WindowNotFound)`. Ainda não implementar reposicionamento (TASK-013).
- **Critério de verificação**: Chamar `RestoreWindowAsync` com Notepad fechado → Notepad abre; timeout de 2s com app inexistente → retorna `Failed` com código `WindowNotFound` sem bloquear o caller.

---

### TASK-013 — Win32WindowRestoreService: reposicionamento e virtual desktop (Infrastructure)

- **Esforço**: M
- **Paralelizável**: Não
- **Depende de**: TASK-010, TASK-012
- **Tipo**: integração-externa
- **Risco**: alto
- **QA**: full
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Infrastructure/Win32/Win32WindowRestoreService.cs`
  - `DesktopOrganizer.Infrastructure/VirtualDesktop/VirtualDesktopManagerImpl.cs`
- **Descrição**: Completar `RestoreWindowAsync` adicionando: (1) `IVirtualDesktopManager.MoveWindowToDesktop(hwnd, desktopId)` (pular se VirtualDesktopId for null ou mover falhar — registrar `VirtualDesktopMoveFailed` e continuar); (2) `SetWindowPos(hwnd, x, y, width, height)`; (3) `ShowWindow(hwnd, SW_RESTORE/SW_MAXIMIZE/SW_MINIMIZE)` conforme `WindowState`. Tratar `AccessDenied` (processo elevado UAC) → retornar `RestoreResult.Skipped(AccessDenied)`. Tratar `SetWindowPos` retornando false → `RepositionFailed`.
- **Critério de verificação**: Restaurar `WindowEntry` com posição e estado específicos → janela aparece nas coordenadas corretas no monitor e virtual desktop indicados; processo elevado → retorna `Skipped` sem abortar.

---

### TASK-014 — CaptureSnapshotUseCase (Core)

- **Esforço**: M
- **Paralelizável**: Não
- **Depende de**: TASK-002, TASK-004, TASK-007, TASK-011
- **Tipo**: lógica-negócio
- **Risco**: alto
- **QA**: full
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Core/UseCases/CaptureSnapshotUseCase.cs`
  - `DesktopOrganizer.Core/Services/IWindowCaptureService.cs`
- **Descrição**: Implementar `CaptureSnapshotUseCase`: (1) chamar `IWindowCaptureService.CaptureAllWindowsAsync()`; (2) se lista vazia, não criar snapshot e retornar indicação de aviso para o caller exibir (DORG AC5 do edge case); (3) criar `Snapshot` com metadados (Id novo, nome recebido por parâmetro, profileId ativo, timestamp); (4) chamar `ISnapshotRepository.SaveAsync`; (5) chamar `ITrayNotificationService.ShowSuccess`. Injetar dependências via construtor.
- **Critério de verificação**: Teste unitário: mock de `IWindowCaptureService` retornando 2 janelas → `SaveAsync` é chamado uma vez com snapshot contendo 2 `WindowEntry`; mock retornando lista vazia → `SaveAsync` não é chamado.

---

### TASK-015 — RestoreSnapshotUseCase (Core)

- **Esforço**: M
- **Paralelizável**: Não
- **Depende de**: TASK-004, TASK-013
- **Tipo**: lógica-negócio
- **Risco**: alto
- **QA**: full
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Core/UseCases/RestoreSnapshotUseCase.cs`
  - `DesktopOrganizer.Core/Services/IWindowRestoreService.cs`
- **Descrição**: Implementar `RestoreSnapshotUseCase`: executar em `Task.Run` (background); para cada `WindowEntry` do snapshot, chamar `IWindowRestoreService.RestoreWindowAsync` com `CancellationToken` individual de timeout; coletar resultados; ao fim, chamar `ITrayNotificationService.ShowResult(restauradas, falhas)`. Executar janelas com concorrência limitada (ex: `SemaphoreSlim(3)`). Nenhuma falha individual deve propagar exceção para o fluxo principal.
- **Critério de verificação**: Teste unitário: snapshot com 3 janelas onde 1 retorna `Failed` → notificação chamada com `restauradas=2, falhas=1`; nenhuma exceção lançada para o caller.

---

### TASK-016 [P] — BootRestoreUseCase (Core)

- **Esforço**: S
- **Paralelizável**: Sim
- **Depende de**: TASK-015, TASK-003
- **Tipo**: lógica-negócio
- **Risco**: médio
- **QA**: wave
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Core/UseCases/BootRestoreUseCase.cs`
  - `DesktopOrganizer.Core/Domain/AppConfig.cs`
- **Descrição**: Implementar `BootRestoreUseCase`: aguardar sinal de "desktop pronto" (via `SystemEvents` ou delay configurável), carregar `AppConfig.DefaultBootSnapshotId`, obter o `Snapshot` via `ISnapshotRepository.GetByIdAsync`, e delegar para `RestoreSnapshotUseCase`. Se `DefaultBootSnapshotId` for null, não fazer nada. Injetar dependências via construtor.
- **Critério de verificação**: Teste unitário: `AppConfig` com `DefaultBootSnapshotId` null → `RestoreSnapshotUseCase` não é invocado; com id válido → `GetByIdAsync` é chamado com o id correto e use case é executado.

---

### TASK-017 [P] — ManageProfilesUseCase (Core)

- **Esforço**: M
- **Paralelizável**: Sim
- **Depende de**: TASK-003, TASK-004, TASK-006
- **Tipo**: crud-padrão
- **Risco**: médio
- **QA**: wave
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Core/UseCases/ManageProfilesUseCase.cs`
  - `DesktopOrganizer.Core/Domain/Profile.cs`
- **Descrição**: Implementar `ManageProfilesUseCase` com métodos: `GetAllProfilesAsync()`, `CreateProfileAsync(string name)` (gera Id, persiste via `IProfileRepository.SaveAsync`), `ActivateProfileAsync(Guid id)` (atualiza `AppConfig.ActiveProfileId` e persiste), `DeleteProfileAsync(Guid id)` (chama `IProfileRepository.DeleteAsync` que faz deleção em cascata). Persistir `AppConfig` atualizado via repositório de config.
- **Critério de verificação**: Teste unitário: criar perfil → `SaveAsync` chamado com nome correto; ativar perfil → `AppConfig.ActiveProfileId` atualizado; deletar perfil → `IProfileRepository.DeleteAsync` chamado com o id correto.

---

### TASK-018 — Bootstrap da aplicação WPF: App.xaml.cs e DI container

- **Esforço**: M
- **Paralelizável**: Não
- **Depende de**: TASK-003, TASK-004, TASK-007, TASK-008, TASK-009, TASK-010, TASK-011, TASK-013
- **Tipo**: config
- **Risco**: médio
- **QA**: wave
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.App/App.xaml`
  - `DesktopOrganizer.App/App.xaml.cs`
- **Descrição**: Configurar `App.xaml.cs` com: (1) `ShutdownMode="OnExplicitShutdown"`; (2) verificação de single-instance via `Mutex` global nomeado — se já existe, encerrar graciosamente; (3) detecção de flag `--boot` nos argumentos de linha de comando; (4) container DI (Microsoft.Extensions.DependencyInjection) registrando todas as interfaces do Core para suas implementações de Infrastructure; (5) inicialização do `TrayIconController` (implementado em TASK-019). Tentar inicializar `VirtualDesktopManagerImpl` — se falhar, registrar `VirtualDesktopStub` no lugar.
- **Critério de verificação**: Aplicação inicia sem janela visível; ícone aparece no tray; segunda instância encerrada imediatamente; `--boot` passado como argumento → `BootRestoreUseCase` é invocado após inicialização.

---

### TASK-019 — TrayIconController: menus e ações básicas (App)

- **Esforço**: M
- **Paralelizável**: Não
- **Depende de**: TASK-018, TASK-014, TASK-015, TASK-017
- **Tipo**: ui-puro
- **Risco**: médio
- **QA**: wave
- **Perfil**: frontend
- **Arquivos**:
  - `DesktopOrganizer.App/TrayIcon/TrayIconController.cs`
  - `DesktopOrganizer.App/TrayIcon/TrayMenu.xaml`
- **Descrição**: Implementar `TrayIconController` usando `H.NotifyIcon.Wpf`: (1) exibir ícone no tray; (2) menu de contexto com itens: "Salvar snapshot atual" (dispara `CaptureSnapshotUseCase`), "Restaurar snapshot" (submenu com lista de snapshots do perfil ativo — dispara `RestoreSnapshotUseCase`), separador, lista de perfis com indicação do ativo, separador, "Sair"; (3) menu dinâmico: recarregar lista de snapshots e perfis ao abrir o menu; (4) cleanup correto do `NotifyIcon` no `Application.Exit`.
- **Critério de verificação**: Ícone visível no tray; clique com botão direito exibe menu; itens de snapshot e perfil listados dinamicamente; clicar em "Salvar snapshot atual" dispara a captura com notificação de confirmação.

---

### TASK-020 — Diálogos WPF: nome de snapshot e confirmação de deleção de perfil (App)

- **Esforço**: S
- **Paralelizável**: Não
- **Depende de**: TASK-019
- **Tipo**: ui-puro
- **Risco**: baixo
- **QA**: smoke
- **Perfil**: frontend
- **Arquivos**:
  - `DesktopOrganizer.App/Views/SnapshotNameDialog.xaml`
  - `DesktopOrganizer.App/Views/SnapshotNameDialog.xaml.cs`
- **Descrição**: Implementar dois diálogos WPF mínimos: (1) `SnapshotNameDialog` — campo de texto para nome do snapshot + botões OK/Cancelar; (2) diálogo de confirmação de deleção de perfil (pode reutilizar `MessageBox` nativo do Windows). Integrar `SnapshotNameDialog` no fluxo de `CaptureSnapshotUseCase` — o use case solicita nome via callback/delegate injetado pelo App.
- **Critério de verificação**: Abrir `SnapshotNameDialog` → digitar nome → clicar OK → nome retornado ao caller; clicar Cancelar → captura cancelada sem erro.

---

### TASK-021 [P] — TrayNotificationService (Infrastructure)

- **Esforço**: S
- **Paralelizável**: Sim
- **Depende de**: TASK-004, TASK-018
- **Tipo**: ui-puro
- **Risco**: baixo
- **QA**: smoke
- **Perfil**: frontend
- **Arquivos**:
  - `DesktopOrganizer.App/TrayIcon/TrayNotificationService.cs`
  - `DesktopOrganizer.Core/Services/ITrayNotificationService.cs`
- **Descrição**: Implementar `TrayNotificationService` que expõe `ShowSuccess(string message)` e `ShowResult(int restored, int failed)` exibindo balloon notifications via `H.NotifyIcon.Wpf`. Mensagens: sucesso → "Snapshot salvo: [nome]"; resultado de restauração → "Restauração concluída: [N] janelas restauradas, [M] falhas".
- **Critério de verificação**: Chamar `ShowSuccess("Dev Setup")` → notificação balloon aparece no tray com o texto correto; chamar `ShowResult(3, 1)` → notificação com "3 janelas restauradas, 1 falha".

---

### TASK-022 [P] — Opção "Restaurar ao iniciar Windows" no tray (App + Infrastructure)

- **Esforço**: S
- **Paralelizável**: Sim
- **Depende de**: TASK-008, TASK-019
- **Tipo**: lógica-negócio
- **Risco**: alto
- **QA**: full
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.App/TrayIcon/TrayIconController.cs`
  - `DesktopOrganizer.Infrastructure/Startup/RegistryStartupService.cs`
- **Descrição**: Adicionar item "Restaurar ao iniciar Windows" no menu do tray com checkmark que reflete `IStartupService.IsEnabled()`. Ao marcar: chamar `Enable(executablePath)` e atualizar `AppConfig.StartupEnabled = true`. Ao desmarcar: chamar `Disable()` e `AppConfig.StartupEnabled = false`. Persistir `AppConfig` após cada alteração.
- **Critério de verificação**: Marcar o item → chave no regedit criada; desmarcar → chave removida; reiniciar a app → checkmark reflete o estado atual do Registry.

---

### TASK-023 [P] — Persistência de AppConfig (Infrastructure)

- **Esforço**: S
- **Paralelizável**: Sim
- **Depende de**: TASK-003, TASK-006
- **Tipo**: crud-padrão
- **Risco**: médio
- **QA**: wave
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Infrastructure/Persistence/JsonConfigRepository.cs`
  - `DesktopOrganizer.Core/Domain/AppConfig.cs`
- **Descrição**: Implementar `JsonConfigRepository` com `LoadAsync()` e `SaveAsync(AppConfig)` que lê/escreve `config.json` em `%APPDATA%\DesktopOrganizer\`. Se o arquivo não existir, retornar `AppConfig` com valores padrão (criar perfil "default" e salvar automaticamente). Declarar interface `IConfigRepository` no Core se ainda não existir.
- **Critério de verificação**: Primeira execução sem `config.json` → arquivo criado com valores padrão; alterar `RestoreTimeoutSeconds` e salvar → valor persistido e relido corretamente.

---

### TASK-024 — Testes unitários dos use cases (Core)

- **Esforço**: M
- **Paralelizável**: Não
- **Depende de**: TASK-014, TASK-015, TASK-016, TASK-017
- **Tipo**: teste
- **Risco**: médio
- **QA**: wave
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Tests/UseCases/CaptureSnapshotUseCaseTests.cs`
  - `DesktopOrganizer.Tests/UseCases/RestoreSnapshotUseCaseTests.cs`
- **Descrição**: Criar projeto `DesktopOrganizer.Tests` (xUnit) e implementar testes unitários para: `CaptureSnapshotUseCase` (cenário lista vazia → não salva; lista com janelas → salva e notifica); `RestoreSnapshotUseCase` (1 sucesso + 1 falha → notificação correta; nenhuma exceção propagada); `BootRestoreUseCase` (null DefaultBootSnapshotId → não invoca restore; id válido → invoca). Usar mocks via `NSubstitute` ou `Moq`.
- **Critério de verificação**: `dotnet test` no projeto Tests passa com todos os testes em verde; zero falhas.

---

### TASK-025 — Testes unitários de ManageProfilesUseCase e JsonRepositories (Core + Infrastructure)

- **Esforço**: M
- **Paralelizável**: Não
- **Depende de**: TASK-017, TASK-007, TASK-023, TASK-024
- **Tipo**: teste
- **Risco**: médio
- **QA**: wave
- **Perfil**: backend
- **Arquivos**:
  - `DesktopOrganizer.Tests/UseCases/ManageProfilesUseCaseTests.cs`
  - `DesktopOrganizer.Tests/Persistence/JsonSnapshotRepositoryTests.cs`
- **Descrição**: Adicionar testes para: `ManageProfilesUseCase` (criar, ativar, deletar perfil); `JsonSnapshotRepository` (save/get/delete com diretório temporário); `JsonConfigRepository` (valores padrão na primeira carga, persistência de alteração). Usar diretório temporário (`Path.GetTempPath`) para testes de persistência — não tocar em `%APPDATA%` real.
- **Critério de verificação**: `dotnet test` passa com todos os novos testes em verde; testes de repositório não criam arquivos fora do diretório temporário.

---

### TASK-026 — Projeto WiX e geração do instalador (Installer)

- **Esforço**: L
- **Paralelizável**: Não
- **Depende de**: TASK-018, TASK-019, TASK-020, TASK-021, TASK-022, TASK-023
- **Tipo**: infra
- **Risco**: médio
- **QA**: wave
- **Perfil**: infra
- **Arquivos**:
  - `DesktopOrganizer.Installer/Package.wxs`
  - `DesktopOrganizer.Installer/Shortcuts.wxs`
- **Descrição**: Configurar projeto WiX Toolset v4 integrado ao MSBuild. Definir em `Package.wxs`: diretório de instalação (`Program Files\DesktopOrganizer`), lista de arquivos binários gerados pelo publish, registro de uninstaller no Painel de Controle. Definir em `Shortcuts.wxs`: atalho no menu Iniciar. A desinstalação deve remover todos os arquivos de `Program Files` e entradas do Registry criadas pelo instalador (dados de `%APPDATA%` mantidos). O build deve gerar um único `.msi` distribuível via `dotnet build` ou `dotnet publish`.
- **Critério de verificação**: Executar o instalador em máquina limpa → app presente em `Program Files`, atalho no menu Iniciar; executar desinstalação via Configurações → nenhum arquivo residual em `Program Files`; `dotnet build` gera o `.msi` sem erros de toolchain.

---

## Grupos de Paralelização Sugeridos

- **Onda 1** (pode começar imediatamente):
  - TASK-001

- **Onda 2** (após TASK-001):
  - TASK-002 — modelos Snapshot e WindowEntry
  - TASK-003 [P] — modelos Profile e AppConfig
  - TASK-004 [P] — value objects e interfaces de serviço
  - TASK-005 [P] — declarações P/Invoke Win32
  - TASK-006 [P] — AppDataPaths e JsonProfileRepository

- **Onda 3** (após Onda 2):
  - TASK-007 [P] — JsonSnapshotRepository (depende de TASK-002 e TASK-006)
  - TASK-008 [P] — RegistryStartupService (depende de TASK-004)
  - TASK-009 [P] — VirtualDesktopStub (depende de TASK-004)
  - TASK-023 [P] — JsonConfigRepository (depende de TASK-003 e TASK-006)

- **Onda 4** (após Onda 3):
  - TASK-010 — VirtualDesktopManagerImpl (depende de TASK-009)
  - TASK-011 [P] — Win32WindowCaptureService (depende de TASK-005, TASK-002, TASK-009)
  - TASK-012 [P] — Win32WindowRestoreService: lançamento e polling (depende de TASK-005, TASK-004)

- **Onda 5** (após Onda 4):
  - TASK-013 — Win32WindowRestoreService: reposicionamento (depende de TASK-010 e TASK-012)

- **Onda 6** (após Onda 5):
  - TASK-014 — CaptureSnapshotUseCase (depende de TASK-002, TASK-004, TASK-007, TASK-011)
  - TASK-015 — RestoreSnapshotUseCase (depende de TASK-004, TASK-013)
  - TASK-017 [P] — ManageProfilesUseCase (depende de TASK-003, TASK-004, TASK-006)

- **Onda 7** (após Onda 6):
  - TASK-016 [P] — BootRestoreUseCase (depende de TASK-015 e TASK-003)

- **Onda 8** (após Onda 7):
  - TASK-018 — Bootstrap App.xaml.cs e DI container (depende de múltiplas tasks de Onda 3-7)

- **Onda 9** (após Onda 8):
  - TASK-019 — TrayIconController (depende de TASK-018, TASK-014, TASK-015, TASK-017)
  - TASK-021 [P] — TrayNotificationService (depende de TASK-004 e TASK-018)

- **Onda 10** (após Onda 9):
  - TASK-020 — Diálogos WPF (depende de TASK-019)
  - TASK-022 [P] — Opção startup no tray (depende de TASK-008 e TASK-019)

- **Onda 11** (após Onda 10):
  - TASK-024 — Testes unitários dos use cases
  - TASK-025 — Testes unitários de perfis e repositórios

- **Onda 12** (após Onda 11):
  - TASK-026 — Projeto WiX e geração do instalador
