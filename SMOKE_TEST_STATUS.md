# Smoke Test — Status atual

**Data**: 2026-04-27
**Branch**: `feature/initial-implementation-integration`
**MSI versão**: 1.0.6.0
**MSI path**: `DesktopOrganizer.Installer/bin/Release/DesktopOrganizer.msi`
**PR**: https://github.com/rubensrudio/DesktopOrganizer/pull/1

## Bugs encontrados e corrigidos durante smoke test

| # | Bug | Fix commit | Versão MSI |
|---|-----|------------|------------|
| 1 | App crash silent — `H.NotifyIcon` rejeita `InteropBitmap` | `443f67c` | 1.0.0 → 1.0.1 |
| 2 | App crash — `BitmapImage.StreamSource` ignorado, espera `UriSource` | `5c809b3` | 1.0.2 |
| 3 | Tray icon não registrava — `IconSource` (ImageSource) não funciona, usa `Icon` (System.Drawing.Icon) + `ForceCreate()` | `8522a83` | 1.0.3 |
| 4 | `SnapshotNameDialog` — "Cannot set Owner Property to a Window that has not been shown previously" (app tray-only) | `a7c0c9e` | 1.0.4 |
| 5 | App fechava sem trace — adicionado crash logger global em `%APPDATA%\DesktopOrganizer\crash.log` | `06c3124` | 1.0.5 |
| 6 | Diálogo crash — `AutomationProperties.LabeledBy` self-reference recursiva | `3b20638` | 1.0.6 |
| 7 | Janelas restauradas em posições erradas — splash screens trocam handle; agora re-aplica `SetWindowPos` 2x com delays (1.5s + 1s) | `3b20638` | 1.0.6 |

## Funcionalidade validada

- ✅ Single-instance Mutex
- ✅ Tray icon registra e aparece
- ✅ Menu de contexto abre
- ✅ Diálogo de nome de snapshot funciona
- ✅ Captura de snapshot persiste
- ✅ Restauração reabre apps
- ⚠️ Posicionamento — re-aplicado 2x agora; **PRECISA RE-VALIDAR após instalar 1.0.6**

## Pendente de validar manualmente (próxima sessão)

### Em ambiente Windows real (instalar 1.0.6 primeiro)
1. **Captura/restauração com 3+ apps** — Notepad, Chrome, VS Code em monitores e virtual desktops distintos. Validar posições EXATAS após restore (1.0.6 deve corrigir splash issue).
2. **Multi-monitor**: confirmar que `MonitorDeviceName` é respeitado.
3. **Multi virtual desktop**: confirmar `VirtualDesktopId` move janela pro desktop certo.
4. **"Restaurar ao iniciar Windows"** — marcar, reboot, validar app aparece no tray. Sem boot snapshot configurado, nada restaura (limitação conhecida — UI faltando).
5. **Desinstalação**: Configurações → Apps → Desinstalar → confirmar `C:\Program Files\DesktopOrganizer\` removido. `%APPDATA%\DesktopOrganizer\` deve permanecer.
6. **App elevado (UAC)**: testar restauração com Task Manager aberto (admin) — deve pular silenciosamente sem crash.
7. **Path inexistente**: editar JSON manualmente para apontar pra exe deletado, restaurar — deve marcar como `ExecutableNotFound`.

### Comandos úteis
```powershell
# Desinstalar
Get-Process DesktopOrganizer* | Stop-Process -Force
# Configurações → Apps → DesktopOrganizer → Desinstalar

# Re-instalar
msiexec /i "D:\Sistemas\DesktopOrganizer\DesktopOrganizer.Installer\bin\Release\DesktopOrganizer.msi"

# Ver crash log
Get-Content "$env:APPDATA\DesktopOrganizer\crash.log"

# Ver snapshots/perfis no disco
ls "$env:APPDATA\DesktopOrganizer"

# Verificar startup Registry
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v DesktopOrganizer
```

## Funcionalidades NÃO implementadas (ainda)

Confirmadas pelo product owner — ficam para próxima feature:

- **UI para definir `DefaultBootSnapshotId`**: sem isso, "Restaurar ao iniciar Windows" liga app no boot mas nada restaura
- **UI criar/deletar perfis** via tray (apenas "default" auto-criado)
- **DORG-09 AC9**: recriação destrutiva de virtual desktops do snapshot — limitação API pública
- **Lista nominal de apps que falharam** antes da notificação final (atualmente só números)
- **Mensagem fim**: texto exato do spec ("O carregamento de desktops foi concluído")
- **Monitor desconectado fallback**: posicionar no primário com aviso

## Lacunas resolvidas

- L1: Windows 11 mínimo (commit `fcfe060`)
- L5: BootRestoreUseCase notifica usuário se snapshot deletado (commit `fcfe060`)

## Como retomar

1. Pull latest: `git checkout feature/initial-implementation-integration && git pull`
2. Build: `dotnet build DesktopOrganizer.sln -c Release`
3. MSI em `DesktopOrganizer.Installer/bin/Release/DesktopOrganizer.msi`
4. Roda smoke test conforme lista acima
5. Se bugs novos, mesmo padrão: fix → bump versão WiX → commit → push → re-instala
