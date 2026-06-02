# Збірка інсталятора NetSdrMonitor

Скрипт `build-installer.ps1` публікує WPF-застосунок і пакує його в один інсталятор `.exe`
через Inno Setup. Інсталятор сам ставить .NET Desktop Runtime 10, якщо його ще немає.

## Потрібно один раз поставити

- **.NET 10 SDK** — щоб `dotnet publish` працював (той самий, чим збираєш проєкт).
- **Inno Setup 6+** — https://jrsoftware.org/isdl.php (скрипт сам знаходить `ISCC.exe`).
- У цій теці має лежати `windowsdesktop-runtime-10.0.8-win-x64.exe` — уже є.

## Як зібрати

З теки `install\`:

```powershell
.\build-installer.ps1                 # версія 1.0.0
.\build-installer.ps1 -Version 1.2.3  # своя версія
```

Результат: `install\NetSdrMonitorSetup-<версія>.exe`.

## Викласти як реліз на GitHub

1. На сторінці репозиторію → **Releases** → **Draft a new release**.
2. **Choose a tag** → введи новий тег, напр. `v1.0.0` (Target: `main`).
3. Заголовок і опис релізу — за бажанням (кнопка *Generate release notes* підтягне коміти).
4. У блок **Attach binaries** перетягни `NetSdrMonitorSetup-1.0.0.exe`.
5. **Publish release**.

Версія тега й `-Version` у скрипті мають збігатися — щоб ім'я файлу й тег не розходились.
