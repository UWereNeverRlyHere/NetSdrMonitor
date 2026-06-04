# Збірка інсталятора NetSdrMonitor

Скрипт `build-installer.ps1` публікує WPF-застосунок і пакує його в один інсталятор `.exe`
через Inno Setup. Інсталятор сам ставить .NET Desktop Runtime 10, якщо його ще немає.

## Потрібно один раз поставити

- **.NET 10 SDK** — щоб `dotnet publish` працював
- **Inno Setup 6+** — https://jrsoftware.org/isdl.php (скрипт сам знаходить `ISCC.exe`).
- У цій теці має лежати `windowsdesktop-runtime-10.0.8-win-x64.exe`.

## Як зібрати

З теки `install\`:

```powershell
.\build-installer.ps1                 # версія 1.0.0
.\build-installer.ps1 -Version 1.2.3  # своя версія
```

Результат: `install\NetSdrMonitorSetup-<версія>.exe`.

Версія тега й `-Version` у скрипті мають збігатися — щоб ім'я файлу й тег не розходились.
