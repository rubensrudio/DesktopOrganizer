# Plano Técnico — DesktopOrganizer: Implementação Inicial

## 1. Resumo Executivo

O DesktopOrganizer é uma aplicação desktop Windows (C# / .NET / WPF) que resolve um problema real de produtividade: usuários com múltiplos monitores e virtual desktops perdem tempo reconfigurando manualmente o layout de janelas após reinicializações ou trocas de contexto. A implementação inicial cobre o ciclo completo de captura e restauração de snapshots de workspace, restauração automática no boot do Windows, gerenciamento de múltiplos perfis de contexto e geração de instalador distribuível.

A stack é exclusivamente Windows (Win32 APIs + Virtual Desktop COM APIs), sem qualquer dependência de backend ou nuvem. Toda a persistência é local. O aplicativo roda primariamente como ícone na system tray, sem janela principal, o que impõe restrições específicas no design de UX e no ciclo de vida da aplicação WPF.

O projeto é classificado como **Large** pelo critério do spec-driven: envolve múltiplos componentes, integração com APIs de sistema operacional de baixo nível (Win32 + COM), tratamento de edge cases complexos (UAC, timeouts, monitores desconectados, virtual desktops inexistentes) e geração de instalador. O plano cobre as fases P1 (MVP), P2 (Perfis) e P3 (Instalador) conforme priorização do spec.

A maior área de risco técnico é a integração com as Virtual Desktop COM APIs, que não são APIs oficialmente documentadas pelo Windows — são interfaces COM internas que a comunidade fez engenharia reversa e que podem variar entre versões do Windows 10/11. Essa é a decisão arquitetural mais crítica a ser resolvida antes da implementação.

---

## 2. Premissas e Lacunas do Spec

### Lacunas explícitas (marcadas com `[LACUNA: ...]` no spec)

| # | Lacuna | Impacto | Premissa adotada neste plano |
|---|--------|---------|------------------------------|
| L1 | Versão mínima do Windows não definida | Afeta quais Virtual Desktop COM APIs são disponíveis e estáveis | Premissa: **Windows 10 versão 1903 (build 18362) ou superior**. É o mínimo documentado para COM APIs de Virtual Desktop razoavelmente estáveis. Windows 11 é suportado sem ressalvas. **Requer confirmação do autor antes da implementação.** |
| L2 | Ferramenta de geração do instalador não definida | Afeta toda a task P3 e o toolchain de build | Premissa: **WiX Toolset v4** (integra com .NET build pipeline via MSBuild, é open-source, amplamente adotado para .NET apps, suporta desinstalação limpa e Registry). Alternativas: Inno Setup (mais simples, menos integrado ao MSBuild), NSIS (mais antigo), Visual Studio Installer Projects (limitado). **Requer aprovação antes da task P3.** |
| L3 | Formato de persistência dos snapshots não definido | Afeta o design completo da camada de dados | Premissa: **JSON** (via `System.Text.Json`, já incluso no .NET 6+). Justificativa: legibilidade humana (facilita debug e suporte), sem dependência externa, portabilidade trivial, simplicidade de evolução de schema. SQLite seria mais adequado se houvesse consultas complexas ou volume alto — não é o caso de snapshots de layout. |
| L4 | Timeout padrão para apps lentos não definido | Afeta DORG-12 e a UX de restauração | Premissa: **30 segundos** como timeout padrão, configurável via arquivo de configuração da aplicação. Valor sugerido pelo próprio spec. |
| L5 | Comportamento ao virtual desktop referenciado não existir mais | Afeta DORG-09 e o fluxo de restauração | Lacuna **não resolvida neste plano** — o spec marca explicitamente como comportamento preferido não definido. As duas opções são: (a) criar novo virtual desktop para manter fidelidade ao snapshot; (b) posicionar no desktop atual para simplicidade. **Decisão deve ser tomada pelo autor antes da implementação de DORG-09.** |

### Premissas adicionais (não marcadas no spec, inferidas)

| # | Premissa | Raciocínio |
|---|----------|------------|
| P1 | Framework: **.NET 8 LTS** | Versão LTS mais recente na data do plano; suporte garantido até novembro 2026. Evita .NET Framework (legado) e .NET 6 (EOL em novembro 2024). |
| P2 | O aplicativo roda como **processo único** (single instance) | Necessário para evitar conflitos no controle do tray icon, Registry startup e arquivos de snapshot. Implementar via `Mutex` nomeado global. |
| P3 | Snapshots são **por usuário do Windows** (`%APPDATA%\DesktopOrganizer\`) | Alinhado com o Registry target `HKCU` (por usuário). Não há suporte multi-usuário nesta versão. |
| P4 | O aplicativo **não requer elevação UAC** para funcionar | Janelas de processos elevados (admin) serão ignoradas na restauração, conforme edge case do spec. A aplicação roda com privilégios de usuário padrão. |
| P5 | **Não há testes automatizados** definidos no spec | O spec define "Independent Tests" como verificação manual. O plano propõe estrutura de testes unitários para lógica de domínio, mas não há critério de cobertura obrigatório no spec. |

---

## 3. Arquitetura Proposta

### 3.1 Visão de Componentes

O sistema é organizado em camadas com separação clara entre domínio, infraestrutura e apresentação. A estrutura de projeto segue as convenções padrão de aplicações .NET/WPF.

```
DesktopOrganizer/
├── DesktopOrganizer.App/           # Projeto WPF (entry point, tray, UI mínima)
│   ├── App.xaml / App.xaml.cs      # Bootstrap, single-instance mutex, DI container
│   ├── TrayIcon/                   # NotifyIcon, menus de contexto, notificações
│   └── Views/                      # Diálogos mínimos (nome de snapshot, confirmação)
│
├── DesktopOrganizer.Core/          # Domínio puro — sem dependências de UI ou Win32
│   ├── Domain/
│   │   ├── Snapshot.cs             # Agregado: snapshot com lista de WindowEntry
│   │   ├── WindowEntry.cs          # Entidade: estado de uma janela capturada
│   │   ├── Profile.cs              # Entidade: perfil com lista de snapshots
│   │   ├── MonitorInfo.cs          # Value object: identificador de monitor
│   │   └── VirtualDesktopId.cs     # Value object: ID do virtual desktop
│   ├── Services/
│   │   ├── IWindowCaptureService.cs
│   │   ├── IWindowRestoreService.cs
│   │   ├── ISnapshotRepository.cs
│   │   ├── IProfileRepository.cs
│   │   ├── IStartupService.cs
│   │   └── ITrayNotificationService.cs
│   └── UseCases/
│       ├── CaptureSnapshotUseCase.cs
│       ├── RestoreSnapshotUseCase.cs
│       ├── BootRestoreUseCase.cs
│       └── ManageProfilesUseCase.cs
│
├── DesktopOrganizer.Infrastructure/ # Implementações de infraestrutura
│   ├── Win32/
│   │   ├── Win32WindowCaptureService.cs   # EnumWindows, GetWindowRect, GetWindowPlacement
│   │   ├── Win32WindowRestoreService.cs   # SetWindowPos, ShowWindow, Process.Start
│   │   └── Win32Interop.cs               # P/Invoke declarations
│   ├── VirtualDesktop/
│   │   ├── IVirtualDesktopManager.cs     # Abstração sobre as COM APIs
│   │   ├── VirtualDesktopManagerImpl.cs  # Implementação via COM (IVirtualDesktopManager)
│   │   └── VirtualDesktopStub.cs         # Fallback se COM APIs indisponíveis
│   ├── Persistence/
│   │   ├── JsonSnapshotRepository.cs     # Leitura/escrita de snapshots em JSON
│   │   ├── JsonProfileRepository.cs      # Leitura/escrita de perfis em JSON
│   │   └── AppDataPaths.cs               # Resolve %APPDATA%\DesktopOrganizer\
│   └── Startup/
│       └── RegistryStartupService.cs     # HKCU\Software\Microsoft\Windows\CurrentVersion\Run
│
└── DesktopOrganizer.Installer/     # Projeto WiX Toolset (P3)
    ├── Package.wxs
    └── Shortcuts.wxs
```

### 3.2 Fluxo Principal

**Fluxo: Capturar Snapshot (DORG-01 a DORG-05)**

```
Usuário clica "Salvar snapshot atual" no tray
  → TrayIcon dispara CaptureSnapshotUseCase
    → Win32WindowCaptureService.EnumWindows()
      → Para cada janela visível:
          → GetWindowRect / GetWindowPlacement (posição, tamanho, estado)
          → GetWindowThreadProcessId → Process.GetProcessById (PID, executável)
          → IVirtualDesktopManager.GetWindowDesktopId (virtual desktop)
          → Ignora janelas sem executável acessível (processos de sistema)
      → Monta lista de WindowEntry
    → Solicita nome ao usuário (diálogo mínimo)
    → Cria Snapshot com metadados + lista de WindowEntry + perfil ativo
    → JsonSnapshotRepository.Save(snapshot)
    → ITrayNotificationService.ShowSuccess("Snapshot salvo: [nome]")
```

**Fluxo: Restaurar Snapshot (DORG-06 a DORG-13)**

```
Usuário clica "Restaurar snapshot" no tray
  → TrayIcon lista snapshots do perfil ativo
  → Usuário seleciona snapshot
    → RestoreSnapshotUseCase(snapshot)
      → Para cada WindowEntry em paralelo (com limite de concorrência):
          → Verifica se processo está aberto (by executable path)
          → Se não: Process.Start(executável)
          → Aguarda janela principal aparecer (polling com timeout de 30s)
          → IVirtualDesktopManager.MoveWindowToDesktop(hwnd, desktopId)
          → SetWindowPos(hwnd, x, y, width, height)
          → ShowWindow(hwnd, estado: normal/maximizado/minimizado)
          → Registra resultado (sucesso ou falha parcial)
      → ITrayNotificationService.ShowResult(restauradas, falhas)
```

**Fluxo: Boot Automático (DORG-14 a DORG-17)**

```
Windows inicia → DesktopOrganizer.exe é chamado pelo Registry
  → App.xaml.cs detecta flag --boot (ou ausência de janela ativa)
  → Aguarda SystemEvents.SessionSwitchEventArgs (desktop pronto)
    → BootRestoreUseCase()
      → Carrega snapshot padrão do perfil ativo
      → Executa RestoreSnapshotUseCase (mesmo fluxo acima)
```

### 3.3 Decisões Arquiteturais

**DA-01: Virtual Desktop COM APIs — abstração obrigatória**

As interfaces COM para Virtual Desktops (`IVirtualDesktopManager`, `IApplicationViewCollection`) não são públicas — são interfaces internas do Windows obtidas via engenharia reversa (projetos como `VirtualDesktop` de Markus Gaßner são a referência da comunidade .NET). Elas podem mudar entre builds do Windows. Decisão: isolar toda a interação com essas APIs atrás de `IVirtualDesktopManager` (interface no Core), com implementação no Infrastructure. Adicionar `VirtualDesktopStub` (noop) que permite o app funcionar sem suporte a virtual desktops caso as COM APIs falhem na inicialização.

Trade-off: adiciona uma camada de indireção; mitiga o risco de quebra por atualização do Windows.

**DA-02: Identificação de janelas para restauração**

Identificar "qual processo já está aberto" não é trivial: o mesmo executável pode ter múltiplas instâncias (dois Notepads). Decisão: a estratégia de matching usa combinação de (1) caminho do executável + (2) título da janela como chave de correspondência. Se o processo está aberto mas a janela-alvo não é encontrada pelo título exato, tenta correspondência parcial (contains). Se ainda não encontrado, trata como processo novo.

Trade-off: correspondência por título é frágil para apps que mudam o título dinamicamente (VS Code, navegadores). Documentado como limitação conhecida do MVP.

**DA-03: Persistência em JSON com estrutura de diretórios por perfil**

```
%APPDATA%\DesktopOrganizer\
├── config.json                 # Configurações gerais (perfil ativo, timeout, startup)
├── profiles\
│   ├── default\
│   │   ├── profile.json        # Metadados do perfil
│   │   └── snapshots\
│   │       ├── snapshot-[id].json
│   │       └── ...
│   └── [nome-perfil]\
│       └── ...
```

Trade-off: estrutura de diretórios é fácil de navegar manualmente e fazer backup, mas requer lógica de enumeração de arquivos. SQLite seria mais robusto para buscas futuras — adiado para versão posterior.

**DA-04: Restauração com fila de workers e timeout por janela**

Para não bloquear a UI do tray durante a restauração, o `RestoreSnapshotUseCase` é executado em background (`Task.Run`). Cada janela tem seu próprio CancellationToken com timeout de 30s. O polling por janela usa `Task.Delay(500ms)` entre verificações de disponibilidade. Janelas que excederem o timeout são marcadas como falha parcial e a restauração continua.

Trade-off: aumenta complexidade de concorrência; mitiga bloqueio por apps lentos (Teams, Discord, IDEs).

**DA-05: Tray-only como modo primário, sem janela principal**

A aplicação WPF é configurada com `ShutdownMode="OnExplicitShutdown"` e o `MainWindow` não é instanciado. O `NotifyIcon` (via `System.Windows.Forms.NotifyIcon` ou biblioteca `Hardcodet.NotifyIcon.Wpf`) é o ponto de entrada da UX. Diálogos são `Window` WPF simples instanciados sob demanda.

Trade-off: simplifica o modelo de ciclo de vida; requer atenção ao cleanup do `NotifyIcon` no `Application.Exit`.

---

## 4. Modelos de Dados

### Entidades e Agregados

**Snapshot (Agregado raiz)**

| Campo | Tipo | Descrição |
|-------|------|-----------|
| Id | Guid | Identificador único |
| Name | string | Nome dado pelo usuário |
| ProfileId | Guid | Perfil ao qual pertence |
| CapturedAt | DateTimeOffset | Timestamp da captura |
| Windows | List\<WindowEntry\> | Lista de janelas capturadas |

**WindowEntry (Entidade dentro de Snapshot)**

| Campo | Tipo | Descrição |
|-------|------|-----------|
| Id | Guid | Identificador único da entrada |
| ProcessName | string | Nome do processo (ex: notepad) |
| ExecutablePath | string | Caminho completo do executável |
| WindowTitle | string | Título da janela no momento da captura |
| X | int | Posição horizontal (coordenadas de tela) |
| Y | int | Posição vertical |
| Width | int | Largura em pixels |
| Height | int | Altura em pixels |
| WindowState | enum | Normal / Maximized / Minimized |
| MonitorDeviceName | string | Nome do monitor (ex: `\\.\DISPLAY1`) |
| VirtualDesktopId | Guid? | GUID do virtual desktop (null se não suportado) |

**Profile (Entidade independente)**

| Campo | Tipo | Descrição |
|-------|------|-----------|
| Id | Guid | Identificador único |
| Name | string | Nome do perfil (ex: "Trabalho", "Dev") |
| CreatedAt | DateTimeOffset | Data de criação |

**AppConfig (Value Object persistido)**

| Campo | Tipo | Descrição |
|-------|------|-----------|
| ActiveProfileId | Guid | Perfil atualmente ativo |
| DefaultBootSnapshotId | Guid? | Snapshot a restaurar no boot |
| StartupEnabled | bool | Se restauração automática está ativa |
| RestoreTimeoutSeconds | int | Timeout por janela (default: 30) |

### Estrutura JSON de exemplo (snapshot)

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Dev Setup",
  "profileId": "...",
  "capturedAt": "2026-04-27T09:00:00-03:00",
  "windows": [
    {
      "id": "...",
      "processName": "Code",
      "executablePath": "C:\\Users\\...\\Code.exe",
      "windowTitle": "projeto - Visual Studio Code",
      "x": 0,
      "y": 0,
      "width": 1920,
      "height": 1080,
      "windowState": "Normal",
      "monitorDeviceName": "\\\\.\\DISPLAY1",
      "virtualDesktopId": "a1b2c3d4-..."
    }
  ]
}
```

---

## 5. Contratos de API

Esta aplicação não expõe APIs HTTP. Os contratos relevantes são as interfaces de serviço internas e os métodos Win32/COM invocados.

### Interfaces de serviço (contratos internos)

**IWindowCaptureService**
- `Task<IReadOnlyList<WindowEntry>> CaptureAllWindowsAsync()` — retorna todas as janelas visíveis capturáveis; ignora janelas de sistema sem lançar exceção.

**IWindowRestoreService**
- `Task<RestoreResult> RestoreWindowAsync(WindowEntry entry, CancellationToken ct)` — tenta restaurar uma única janela; retorna `RestoreResult` com status (Success / Skipped / Failed) e mensagem de diagnóstico.

**ISnapshotRepository**
- `Task SaveAsync(Snapshot snapshot)`
- `Task<Snapshot?> GetByIdAsync(Guid id)`
- `Task<IReadOnlyList<Snapshot>> GetByProfileIdAsync(Guid profileId)`
- `Task DeleteAsync(Guid id)`

**IProfileRepository**
- `Task SaveAsync(Profile profile)`
- `Task<IReadOnlyList<Profile>> GetAllAsync()`
- `Task DeleteAsync(Guid id)` — deleta o perfil e todos os snapshots associados

**IStartupService**
- `void Enable(string executablePath)` — escreve no Registry `HKCU\...\Run`
- `void Disable()` — remove do Registry
- `bool IsEnabled()` — verifica presença da chave

**IVirtualDesktopManager (abstração sobre COM)**
- `Guid? GetWindowDesktopId(IntPtr hwnd)` — retorna null se não suportado
- `bool MoveWindowToDesktop(IntPtr hwnd, Guid desktopId)` — retorna false se falhar

### Chamadas Win32 relevantes (P/Invoke)

| Função Win32 | Uso |
|-------------|-----|
| `EnumWindows` | Enumeração de todas as janelas de nível superior |
| `IsWindowVisible` | Filtro para janelas visíveis |
| `GetWindowRect` | Posição e tamanho da janela |
| `GetWindowPlacement` | Estado (normal/maximizado/minimizado) |
| `GetWindowThreadProcessId` | Associa janela ao PID |
| `QueryFullProcessImageName` | Caminho completo do executável pelo PID |
| `SetWindowPos` | Reposiciona e redimensiona janela |
| `ShowWindow` | Aplica estado (SW_RESTORE, SW_MAXIMIZE, SW_MINIMIZE) |
| `MonitorFromWindow` | Identifica monitor da janela |
| `GetMonitorInfo` | Obtém nome/device do monitor |

### Códigos de erro / diagnóstico de restauração

| Código | Significado |
|--------|-------------|
| `ExecutableNotFound` | Caminho do executável não existe mais |
| `LaunchFailed` | Falha ao iniciar o processo |
| `WindowNotFound` | Processo iniciado mas janela não apareceu dentro do timeout |
| `VirtualDesktopMoveFailed` | COM API retornou erro ao mover para virtual desktop |
| `AccessDenied` | Janela pertence a processo elevado (UAC) |
| `RepositionFailed` | SetWindowPos retornou false |

---

## 6. Componentes Afetados

Projeto greenfield — nenhum arquivo existente é modificado. A tabela abaixo lista os módulos a criar e o impacto de cada decisão arquitetural sobre eles.

| Módulo / Arquivo | Impacto | Complexidade |
|-----------------|---------|--------------|
| `App.xaml.cs` | Bootstrap da aplicação, single-instance Mutex, DI container, detecção de modo boot | Alta |
| `TrayIcon/TrayIconController.cs` | Controla NotifyIcon, menus dinâmicos (perfis, snapshots), disparo de use cases | Alta |
| `Win32/Win32WindowCaptureService.cs` | EnumWindows + GetWindowRect + GetWindowPlacement + QueryFullProcessImageName + MonitorFromWindow | Alta |
| `Win32/Win32WindowRestoreService.cs` | Process.Start + polling de janela + SetWindowPos + ShowWindow + timeout handling | Alta |
| `Win32/Win32Interop.cs` | Todas as declarações P/Invoke com tipos corretos (IntPtr, RECT, WINDOWPLACEMENT) | Média |
| `VirtualDesktop/VirtualDesktopManagerImpl.cs` | Interop COM com interfaces internas do Windows — área de maior risco técnico | Muito Alta |
| `VirtualDesktop/VirtualDesktopStub.cs` | Implementação noop para fallback | Baixa |
| `Persistence/JsonSnapshotRepository.cs` | Leitura/escrita de JSON, criação de diretórios, tratamento de corrupção de arquivo | Média |
| `Persistence/JsonProfileRepository.cs` | Gestão de diretórios por perfil, deleção em cascata de snapshots | Média |
| `Startup/RegistryStartupService.cs` | Leitura/escrita no Registry HKCU, detecção de presença da chave | Baixa |
| `UseCases/CaptureSnapshotUseCase.cs` | Orquestra captura, solicita nome, persiste, notifica | Média |
| `UseCases/RestoreSnapshotUseCase.cs` | Orquestra restauração com concorrência limitada e tratamento de falhas parciais | Alta |
| `UseCases/BootRestoreUseCase.cs` | Aguarda desktop pronto (SystemEvents), delega para RestoreSnapshotUseCase | Média |
| `UseCases/ManageProfilesUseCase.cs` | CRUD de perfis, troca de perfil ativo, atualização de config | Média |
| `Installer/Package.wxs` | Definição do pacote WiX: arquivos, atalhos, Registry, uninstall | Alta |

---

## 7. Dependências Externas

| Dependência | Tipo | Justificativa | Status |
|-------------|------|---------------|--------|
| **.NET 8 SDK** | Runtime/SDK | Framework base | Já definido na stack |
| **WPF** (incluso no .NET 8 Windows) | Framework UI | Stack definida no spec | Já definido |
| **System.Text.Json** (incluso no .NET 8) | Biblioteca | Persistência JSON sem dependência extra | Sem instalação adicional |
| **Hardcodet.NotifyIcon.Wpf** ou **H.NotifyIcon** | NuGet | NotifyIcon integrado ao WPF (WPF não tem NotifyIcon nativo) | Requer aprovação — pacote mais popular é `H.NotifyIcon` (mantido ativamente em 2024-2026) |
| **VirtualDesktop (biblioteca da comunidade)** | NuGet / código fonte | Wrapper sobre COM APIs de Virtual Desktops para .NET | Requer aprovação — referência: `VirtualDesktop` de Markus Gaßner (github.com/MScholtes/VirtualDesktop) ou `WindowsVirtualDesktop` — **área de maior risco; pode ser necessário implementar diretamente** |
| **WiX Toolset v4** | Build tool | Geração do instalador .msi/.exe | Requer aprovação (lacuna L2) |
| **Microsoft.Win32.Registry** (incluso no .NET 8) | Biblioteca | Acesso ao Registry para startup | Sem instalação adicional |

Sem dependências de serviços de nuvem, APIs externas ou credenciais de terceiros.

---

## 8. Áreas Sensíveis

- **Autenticação/autorização/sessão**: NÃO. A aplicação não tem autenticação — é desktop local, por usuário do Windows.

- **Pagamento/faturamento/cálculo financeiro real**: NÃO.

- **Dados pessoais/sensíveis (PII, saúde, financeiro)**: SIM (baixo risco). Os snapshots armazenam caminhos de executáveis e títulos de janelas, que podem indiretamente revelar quais aplicativos e documentos o usuário tem abertos. Os dados ficam em `%APPDATA%` local — sem transmissão de dados. Componentes envolvidos: `JsonSnapshotRepository.cs`, `WindowEntry.cs`. Mitigação: não transmitir dados para nenhum servidor; dados ficam exclusivamente locais.

- **Migration de dados em tabela com produção**: NÃO. Persistência local via arquivos JSON. Evolução de schema afeta apenas usuários que atualizarem o app — não há banco de dados compartilhado.

- **Lógica regulatória/fiscal/compliance**: NÃO.

- **Endpoint público sem autenticação prévia**: NÃO. Aplicação desktop local sem servidor.

- **Criptografia/manuseio de chaves**: NÃO. Dados não são criptografados localmente (não há requisito no spec). Observação: caminhos de arquivos em `%APPDATA%` são protegidos pelo filesystem do Windows por permissões de usuário.

- **Integração externa nova com terceiro**: SIM. A integração com **Virtual Desktop COM APIs internas do Windows** é equivalente a uma integração com terceiro em termos de risco: são interfaces não documentadas oficialmente, que podem quebrar em atualizações do Windows. Componentes envolvidos: `VirtualDesktopManagerImpl.cs`, `IVirtualDesktopManager.cs`, `VirtualDesktopStub.cs`. Risco classificado como **Alto** — ver Seção 9.

---

## 9. Riscos e Mitigações

| Prioridade | Risco | Impacto | Probabilidade | Mitigação |
|-----------|-------|---------|---------------|-----------|
| 1 | **Virtual Desktop COM APIs quebram em atualização do Windows** — interfaces internas não documentadas, obtidas por engenharia reversa | Alto — funcionalidade central DORG-03, DORG-04, DORG-09 deixam de funcionar | Alta (Windows Update é frequente) | Isolar atrás de `IVirtualDesktopManager`; implementar `VirtualDesktopStub` (noop) como fallback; app funciona sem virtual desktops mas com aviso ao usuário |
| 2 | **Lacuna L5 não resolvida (comportamento ao virtual desktop inexistente)** — decisão não tomada no spec | Médio — task DORG-09 não pode ser implementada sem essa decisão | Certa (lacuna explícita) | Bloquear task DORG-09 até decisão do autor; implementar as demais tasks enquanto aguarda |
| 3 | **Matching de janelas na restauração é impreciso** — títulos de janela mudam dinamicamente (VS Code, navegadores) | Médio — restauração pode falhar ou posicionar janela errada | Alta (comportamento comum de apps) | Documentar como limitação conhecida do MVP; implementar matching por título com fallback para correspondência parcial; registrar falhas no log |
| 4 | **Processos elevados (UAC) bloqueiam SetWindowPos** — app roda sem elevação, não consegue mover janelas de processos admin | Médio — janelas de apps admin não são restauradas | Média (depende do setup do usuário) | Detectar `AccessDenied` no SetWindowPos, pular e registrar no resultado de restauração; não abortar |
| 5 | **App lento demora mais que 30s para inicializar** (Teams, Docker Desktop) | Baixo — janela não reposicionada; usuário precisa fazer manualmente | Baixa | Timeout configurável via `config.json`; notificação de falha parcial informa o usuário |
| 6 | **Ferramenta de instalador não aprovada (Lacuna L2)** — se WiX não for aprovado, toda a task P3 muda de toolchain | Médio — retrabalho em P3 | Baixa (WiX é padrão de mercado para .NET) | Obter aprovação antes de iniciar task P3 |
| 7 | **Versão mínima do Windows não confirmada (Lacuna L1)** — impacta quais COM APIs usar | Baixo no desenvolvimento; Médio para usuário com Windows 10 < 1903 | Baixa | Adicionar verificação de versão na inicialização; exibir mensagem clara se versão incompatível |
| 8 | **Snapshot sem executável acessível (processo de sistema)** — QueryFullProcessImageName falha | Baixo — janela deve ser ignorada silenciosamente | Alta (janelas de sistema existem sempre) | Capturar exceção na enumeração, logar em modo debug, continuar sem abortar (DORG-01 AC6) |

---

## 10. Critérios de Aceite Técnicos

Os critérios abaixo indicam que o plano foi bem executado, complementando os Success Criteria do spec:

**Funcional (P1 — MVP)**
- `CaptureSnapshotUseCase` enumerada com sucesso em máquina com 3+ apps abertos em 2+ monitores e 2+ virtual desktops; arquivo JSON gerado com dados corretos de posição, tamanho, monitor e virtual desktop
- `RestoreSnapshotUseCase` reabre apps fechados e os posiciona nas coordenadas do snapshot com taxa >= 80% de sucesso; falhas parciais notificadas via tray sem travar a execução
- Entrada no Registry `HKCU\...\Run` criada e removida corretamente via `RegistryStartupService`; app inicia no boot sem janela principal, apenas ícone no tray
- `VirtualDesktopStub` ativado automaticamente quando COM APIs falham na inicialização; app não crasha

**Funcional (P2 — Perfis)**
- Criação, ativação e deleção de perfis via tray funciona sem interferência entre perfis
- Snapshots salvos no Perfil A não aparecem ao restaurar no Perfil B

**Funcional (P3 — Instalador)**
- Instalador gerado como único `.msi` ou `.exe` que instala em `Program Files`, cria atalho no menu Iniciar e registra uninstaller
- Desinstalação remove todos os arquivos de `Program Files` e entradas do Registry criadas pelo instalador (dados de usuário em `%APPDATA%` podem ser mantidos por padrão, comportamento a confirmar)

**Arquitetural**
- Todas as chamadas Win32 e COM estão encapsuladas em `Win32Interop.cs` e `VirtualDesktopManagerImpl.cs` — o Core não referencia `System.Runtime.InteropServices` nem P/Invoke diretamente
- Qualquer falha em janela individual durante restauração não propaga exceção para o fluxo principal — o `RestoreResult` registra a falha e a execução continua
- Nenhuma thread de UI é bloqueada durante captura ou restauração (operações são executadas em background com `Task.Run`)
- Single-instance Mutex é verificado no startup; segunda instância é encerrada graciosamente com foco redirecionado para a instância ativa
