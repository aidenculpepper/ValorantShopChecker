# Nightshift · Valorant Shop Checker

A Windows 10/11 desktop app for your daily Valorant shop and Night Market. Uses your existing signed-in Riot Client session. Valorant does not need to be running.

## Install

Download **ValorantShopCheckerSetup.exe** from [the latest release](https://github.com/aidenculpepper/ValorantShopChecker/releases/latest). The Inno Setup installer installs for the current Windows account, creates a Start menu shortcut, and offers an optional desktop shortcut. An older installer checks for the latest release before installing; if that check fails, interactive setup asks before installing its bundled version.

Open the app and click **Refresh shop**. Enable Stay signed in in Riot Client first. If needed, the app opens Riot Client in the background, waits for its signed-in session, fetches your offers, and closes only the client processes it started. A pre-existing Riot Client or game stays open. Unrevealed Night Market cards may require opening Valorant first.

## Settings and updates

Click the settings button at the lower left. Region choice is saved when changed in the shop. Settings are stored at `%LOCALAPPDATA%\ValorantShopChecker\settings.json`.

- Refresh shop on launch: off by default.
- Check for updates automatically: on by default, at startup and every six hours while open.
- Install updates automatically: off by default. Requires automatic checks; waits for shop refresh and the settings dialog to finish.
- Check for updates: checks the latest stable GitHub release. An Install update button appears for newer versions.
- Manual and automatic updates both use a silent installer, restart the app, and retain settings and shortcuts.
- Installed copies can open their uninstaller from Settings. Portable copies show Uninstall disabled.

Installer downloads require an exact repository/tag/asset URL, a reasonable asset size, and a matching SHA-256 digest from GitHub. Failed checks do not run an installer. The app and installer are currently unsigned.

This is an unofficial read-only integration. It does not store Riot passwords or session tokens, buy items, or send account information to the public skin catalog. Riot may change or restrict the private storefront endpoints. Public artwork comes from valorant-api.com.

## Build and release

Requires Windows, .NET Framework 4.x (included in supported Windows), and Inno Setup 6.7 or newer.

```powershell
.\Build.ps1 -InnoCompiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
.\ValorantShopChecker.exe --self-test tests.txt
.\ValorantShopChecker.exe --update-test update-tests.txt
```

Set the three-part version in **version.json** before building. Build.ps1 generates the assembly/file version and passes that same version to Inno Setup, then generates release.json with the installer hash. Commit the sources, newly built ValorantShopChecker.exe, ValorantShopCheckerSetup.exe, release.json, and RELEASE_NOTES.md together.

The GitHub workflow validates release.json and publishes a stable `vX.Y.Z` release from the exact pushed commit. Existing version tags cannot be overwritten: bump the version for each new release. This matches CopyCheck's release publishing approach.

Self-tests cover storefront parsing, client ownership/lifecycle cleanup, settings persistence, release validation, hash failures, incomplete downloads, and update flags. `--preview <path.png>` and `--settings-preview <path.png>` render screenshots without opening Riot Client. A fully closed real Riot Client cold-start scenario still needs verification on a machine with no pre-existing client; automated lifecycle tests cover delayed and respawned helpers.
