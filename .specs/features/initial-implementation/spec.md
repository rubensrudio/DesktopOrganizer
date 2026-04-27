# DesktopOrganizer — Especificação da Implementação Inicial

## Problem Statement

Usuários de Windows com múltiplos monitores e virtual desktops perdem tempo reconfigurando manualmente o layout de janelas cada vez que reiniciam o computador ou mudam de contexto de trabalho (ex: do modo "Desenvolvimento" para "Reunião"). Não existe ferramenta nativa do Windows que salve e restaure snapshots completos de workspace, incluindo posicionamento de janelas, virtual desktops e perfis de contexto. O DesktopOrganizer resolve esse problema capturando e restaurando o estado completo do desktop com um clique.

## Goals

- [ ] Permitir ao usuário salvar o estado atual de todas as janelas abertas (posição, tamanho, monitor, virtual desktop, processo) em um snapshot nomeado
- [ ] Restaurar automaticamente um snapshot salvo no boot do Windows, reabrindo e reposicionando janelas nos locais corretos
- [ ] Suportar múltiplos perfis de snapshot (ex: Trabalho, Estudos, Dev, Reunião) com ativação manual via tray
- [ ] Gerar um instalador distribuível para o aplicativo

## Out of Scope

Explicitamente excluído para evitar scope creep nesta implementação inicial.

| Feature | Razão |
| ------- | ----- |
| Sincronização de snapshots em nuvem | Complexidade de infraestrutura; fora do foco do MVP |
| Suporte a macOS ou Linux | Stack escolhida é Win32/WPF, exclusivamente Windows |
| Captura de estado interno de aplicativos (abas, documentos abertos) | Depende de APIs específicas por app; inviável genericamente |
| Agendamento automático de snapshots (por horário) | Não mencionado na descrição; P3+ futuro |
| Hotkeys globais | Não mencionado; pode ser adicionado em versão futura |
| Histórico de snapshots com diff | Fora do escopo inicial |
| Interface de configuração avançada (além do tray) | Tray é suficiente para o MVP |

---

## User Stories

### P1: Capturar Snapshot do Workspace Atual ⭐ MVP

**User Story**: Como usuário do DesktopOrganizer, quero salvar o estado atual de todas as janelas abertas (incluindo posição, tamanho, monitor, virtual desktop e processo) para que eu possa restaurar exatamente esse layout posteriormente.

**Por que P1**: É a funcionalidade central do produto. Sem ela, nenhuma outra funcionalidade faz sentido.

**Acceptance Criteria**:

1. WHEN o usuário clica em "Salvar snapshot atual" no tray THEN o sistema SHALL enumerar todas as janelas abertas visíveis via `EnumWindows`
2. WHEN o snapshot é capturado THEN o sistema SHALL registrar para cada janela: posição (x, y), tamanho (largura, altura), estado (normal/maximizado/minimizado), monitor de origem, PID do processo, título da janela e caminho do executável
3. WHEN o snapshot é capturado THEN o sistema SHALL identificar e registrar em qual virtual desktop cada janela está via Virtual Desktop COM APIs
4. WHEN o snapshot é capturado THEN o sistema SHALL salvar todas as janelas de todos os virtual desktops ativos no mesmo arquivo de snapshot
5. WHEN o snapshot é salvo com sucesso THEN o sistema SHALL exibir notificação de confirmação via tray
6. WHEN uma janela não possui caminho de executável acessível (ex: processo de sistema) THEN o sistema SHALL ignorá-la e continuar sem erro

**Independent Test**: Abrir Notepad, VS Code e um navegador em posições e monitores distintos → clicar em "Salvar snapshot atual" → verificar que o arquivo de snapshot contém os três processos com posição, tamanho e monitor corretos.

---

### P1: Restaurar Snapshot Manualmente ⭐ MVP

**User Story**: Como usuário do DesktopOrganizer, quero restaurar um snapshot salvo com um clique para que meu layout de trabalho seja reconstituído sem configuração manual.

**Por que P1**: Par indissociável da captura. Um snapshot sem restauração não entrega valor.

**Acceptance Criteria**:

1. WHEN o usuário clica em "Restaurar snapshot" no tray THEN o sistema SHALL apresentar a lista de snapshots salvos para o perfil ativo
2. WHEN um snapshot é selecionado para restauração THEN o sistema SHALL, para cada janela registrada: verificar se o processo já está aberto
3. WHEN o processo da janela não está aberto THEN o sistema SHALL iniciar o executável registrado no snapshot
4. WHEN o processo está aberto ou foi iniciado THEN o sistema SHALL mover a janela para o virtual desktop correto via Virtual Desktop COM APIs
5. WHEN a janela foi movida para o virtual desktop correto THEN o sistema SHALL reposicioná-la e redimensioná-la via `SetWindowPos` com os valores do snapshot
6. WHEN a janela deve ser maximizada/minimizada conforme o snapshot THEN o sistema SHALL aplicar o estado correto via `ShowWindow`
7. WHEN um aplicativo demora a inicializar (navegador, IDE, Teams, Discord) THEN o sistema SHALL aguardar até a janela principal estar disponível antes de reposicionar, com timeout configurável
8. WHEN o timeout de espera é atingido sem a janela aparecer THEN o sistema SHALL registrar o item como falha parcial e continuar com os demais
9. WHEN a restauração é iniciada THEN o sistema SHALL remover todos os virtual desktops existentes e recriar exatamente os virtual desktops definidos no snapshot, substituindo o estado atual do ambiente
10. WHEN um ou mais itens falharam na restauração THEN o sistema SHALL exibir ao usuário lista explícita dos aplicativos/janelas que não foram carregados, antes da mensagem de conclusão
11. WHEN a restauração é concluída (com ou sem falhas parciais) THEN o sistema SHALL exibir a notificação: "O carregamento de desktops foi concluído"

**Independent Test**: Salvar snapshot com Notepad e Chrome em posições específicas → fechar ambos → clicar em "Restaurar snapshot" → verificar que ambos são reabertos nas posições originais nos monitores e virtual desktops corretos.

---

### P1: Restauração Automática no Boot do Windows ⭐ MVP

**User Story**: Como usuário do DesktopOrganizer, quero que o aplicativo inicie com o Windows e restaure automaticamente um snapshot selecionado para que meu ambiente de trabalho seja reconstituído sem nenhuma ação manual após cada reinicialização.

**Por que P1**: É um dos diferenciais centrais descritos na feature. Sem restauração no boot, o usuário ainda precisa intervir manualmente após reiniciar.

**Acceptance Criteria**:

1. WHEN o usuário ativa a opção "Restaurar ao iniciar Windows" no tray THEN o sistema SHALL registrar a entrada de startup no Registry do Windows (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`)
2. WHEN o Windows é iniciado com a entrada no Registry ativa THEN o sistema SHALL iniciar o DesktopOrganizer automaticamente em background (somente tray, sem janela)
3. WHEN o DesktopOrganizer inicia via startup THEN o sistema SHALL aguardar a inicialização completa do Windows (desktop pronto) antes de iniciar a restauração
4. WHEN a restauração no boot é iniciada THEN o sistema SHALL aplicar o snapshot configurado como padrão para o boot seguindo as mesmas regras de restauração manual
5. WHEN o usuário desativa a opção "Restaurar ao iniciar Windows" THEN o sistema SHALL remover a entrada do Registry

**Independent Test**: Ativar "Restaurar ao iniciar Windows" → reiniciar o PC → verificar que o DesktopOrganizer aparece no tray e que as janelas do snapshot padrão são abertas e posicionadas corretamente.

---

### P2: Perfis de Snapshot (Trabalho, Dev, Reunião etc.)

**User Story**: Como usuário do DesktopOrganizer, quero organizar meus snapshots em perfis nomeados para que eu possa alternar rapidamente entre contextos de trabalho diferentes.

**Por que P2**: Aumenta significativamente o valor do produto sem ser bloqueador para o MVP. A versão mínima pode operar com um único perfil padrão.

**Acceptance Criteria**:

1. WHEN o usuário acessa o tray THEN o sistema SHALL exibir a lista de perfis existentes
2. WHEN o usuário cria um novo perfil THEN o sistema SHALL solicitar um nome e criar o perfil vazio
3. WHEN um perfil está ativo THEN o sistema SHALL associar os snapshots salvos e restaurados a esse perfil
4. WHEN o usuário troca de perfil ativo THEN o sistema SHALL atualizar o contexto para que capturas e restaurações operem no novo perfil
5. WHEN o usuário deleta um perfil THEN o sistema SHALL solicitar confirmação antes de remover o perfil e todos os snapshots associados

**Independent Test**: Criar perfis "Trabalho" e "Dev" → salvar snapshots distintos em cada um → alternar entre perfis → verificar que cada perfil restaura seu próprio conjunto de janelas.

---

### P3: Instalador Distribuível

**User Story**: Como usuário do DesktopOrganizer, quero instalar o aplicativo via um instalador padrão do Windows para que a instalação, criação de atalhos e desinstalação sejam gerenciadas de forma convencional.

**Por que P3**: Melhora a experiência de distribuição, mas não bloqueia o uso do produto durante o desenvolvimento e testes.

**Acceptance Criteria**:

1. WHEN o instalador é executado THEN o sistema SHALL instalar os binários do DesktopOrganizer em um diretório padrão (`Program Files`)
2. WHEN a instalação é concluída THEN o sistema SHALL criar atalho no menu Iniciar
3. WHEN o usuário desinstala via Painel de Controle THEN o sistema SHALL remover todos os arquivos e entradas de Registry criados pelo aplicativo
4. WHEN o instalador é gerado via build THEN o resultado SHALL ser um único arquivo `.exe` ou `.msi` distribuível

**Independent Test**: Executar o instalador em máquina limpa → verificar presença dos atalhos → desinstalar via Configurações → verificar que nenhum rastro permanece.

---

## Edge Cases

- WHEN uma janela registrada no snapshot não possui mais o executável no caminho salvo THEN o sistema SHALL registrar aviso e pular a entrada sem abortar a restauração
- WHEN dois processos com o mesmo executável estão abertos (ex: dois Notepads) THEN o sistema SHALL tratar cada janela individualmente pelo título e PID, não fundir entradas
- WHEN o monitor referenciado no snapshot não está conectado no momento da restauração THEN o sistema SHALL posicionar a janela no monitor principal disponível e registrar o ajuste
- WHEN a restauração de um snapshot é iniciada THEN o sistema SHALL remover todos os virtual desktops existentes e recriar exatamente os desktops definidos no snapshot, substituindo o estado atual
- WHEN um aplicativo registrado no snapshot não pode ser aberto (executável ausente, permissão negada, timeout atingido) THEN o sistema SHALL acumular os itens com falha e exibir ao usuário uma lista explícita do que não foi carregado antes da mensagem de conclusão
- WHEN a restauração é concluída (com ou sem falhas parciais) THEN o sistema SHALL exibir a mensagem: "O carregamento de desktops foi concluído"
- WHEN o usuário tenta salvar um snapshot sem janelas abertas relevantes THEN o sistema SHALL exibir aviso e não criar snapshot vazio
- WHEN o aplicativo não tem permissão para mover uma janela de processo elevado (UAC) THEN o sistema SHALL pular a janela e registrar aviso, sem travar a restauração
- WHEN o timeout de inicialização de um app lento é atingido THEN o sistema SHALL continuar a restauração dos demais itens sem bloquear

---

## Requirement Traceability

| Requirement ID | Story | Fase | Status |
| -------------- | ----- | ---- | ------ |
| DORG-01 | P1: Capturar Snapshot — enumerar janelas via EnumWindows | Design | Pending |
| DORG-02 | P1: Capturar Snapshot — registrar posição, tamanho, estado, monitor, PID, título, executável | Design | Pending |
| DORG-03 | P1: Capturar Snapshot — identificar virtual desktop por janela | Design | Pending |
| DORG-04 | P1: Capturar Snapshot — salvar múltiplos virtual desktops no mesmo snapshot | Design | Pending |
| DORG-05 | P1: Capturar Snapshot — notificação de confirmação via tray | Design | Pending |
| DORG-06 | P1: Restaurar Snapshot — listar snapshots do perfil ativo | Design | Pending |
| DORG-07 | P1: Restaurar Snapshot — detectar se processo já está aberto | Design | Pending |
| DORG-08 | P1: Restaurar Snapshot — iniciar executável se processo fechado | Design | Pending |
| DORG-09 | P1: Restaurar Snapshot — mover janela para virtual desktop correto | Design | Pending |
| DORG-10 | P1: Restaurar Snapshot — reposicionar e redimensionar via SetWindowPos | Design | Pending |
| DORG-11 | P1: Restaurar Snapshot — aplicar estado (maximizado/minimizado) via ShowWindow | Design | Pending |
| DORG-12 | P1: Restaurar Snapshot — aguardar inicialização de apps lentos com timeout | Design | Pending |
| DORG-13 | P1: Restaurar Snapshot — notificação de resultado (restauradas vs. falhas) | Design | Pending |
| DORG-14 | P1: Boot — registrar entrada no Registry (HKCU Run) | Design | Pending |
| DORG-15 | P1: Boot — iniciar somente no tray ao abrir pelo Registry | Design | Pending |
| DORG-16 | P1: Boot — aguardar desktop pronto antes de restaurar | Design | Pending |
| DORG-17 | P1: Boot — remover entrada do Registry ao desativar | Design | Pending |
| DORG-18 | P2: Perfis — listar, criar, ativar e deletar perfis no tray | - | Pending |
| DORG-19 | P2: Perfis — associar snapshots ao perfil ativo | - | Pending |
| DORG-20 | P3: Instalador — gerar .exe/.msi com instalação, atalho e desinstalação limpa | - | Pending |

**Formato de ID:** `DORG-[NÚMERO]`

**Valores de Status:** Pending → In Design → In Tasks → Implementing → Verified

**Cobertura:** 20 requisitos no total, 0 mapeados para tasks, 20 não mapeados (aguardando fase de Tasks)

---

## Premissas Técnicas

- Stack definida: C# / .NET, WPF, Win32 APIs (`EnumWindows`, `GetWindowRect`, `SetWindowPos`, `ShowWindow`), Virtual Desktop COM APIs, Startup via Registry
- Plataforma exclusiva: Windows 10/11 (necessário para suporte a Virtual Desktops via COM)
- [LACUNA: versão mínima do Windows não definida — Windows 10 1903+ é o mínimo recomendado para Virtual Desktop COM APIs estáveis, confirmar com o autor]
- [LACUNA: ferramenta de geração do instalador não definida — opções comuns: WiX Toolset, NSIS, Inno Setup, Visual Studio Installer Projects — confirmar preferência]
- [LACUNA: formato de persistência dos snapshots não definido — JSON, SQLite ou XML? Impacta design da camada de dados]
- [LACUNA: timeout padrão para apps lentos não definido — valor sugerido: 30 segundos, confirmar]

---

## Success Criteria

Como sabemos que a implementação inicial foi bem-sucedida:

- [ ] Usuário consegue salvar um snapshot com janelas em múltiplos monitores e virtual desktops em menos de 3 segundos
- [ ] Usuário consegue restaurar um snapshot completo (5+ janelas) com taxa de sucesso >= 80% das janelas posicionadas corretamente
- [ ] Aplicativo inicia junto com o Windows e restaura o snapshot sem intervenção do usuário
- [ ] Pelo menos 2 perfis distintos podem ser criados, salvos e restaurados de forma independente
- [ ] Instalador instala e desinstala sem deixar rastros no sistema
