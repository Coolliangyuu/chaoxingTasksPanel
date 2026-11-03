# chaoxingTasksPanel

**English** | [简体中文](README.zh-CN.md)

A Windows desktop dashboard for Chaoxing (Xuexitong) assignments. See what is due across your courses, check submission status, and open assignment details or download attachments from the dashboard.

`Windows x64` · `C# / .NET 10` · `WinForms` · `v1.4.9`

## Features

| Feature | Description |
| --- | --- |
| Assignment dashboard | See each assignment, its course, submission status, deadline in Beijing time, and time remaining. |
| Filtering and sorting | Filter pending or completed assignments. Click a status, deadline, or time-remaining column header to toggle its sort order. Click the assignment header to restore the default order. |
| Automatic refresh | The dashboard checks for updates every five minutes. Select Refresh Now to sync on demand. |
| Assignment details and downloads | Open each assignment in its own window and switch between assignments via Alt+Tab or taskbar previews. Choose where to save downloaded attachments. |
| Multiple accounts | Add, switch, or sign out of accounts independently. Account management displays each account's name and masked phone number when available. |
| Sign-in status | The app checks the current session at startup and shows whether you are signed in, whether the session is temporary, and its expiration when available. |
| Version history | Open the built-in changelog from the bottom-right corner of the dashboard. |

## Getting started

1. Run the Windows x64 executable or build the app from source. The app requires the Microsoft Edge WebView2 Runtime.
2. Sign in through the official Chaoxing page inside the app. To add another account, open **账号管理** (Account Management) and select **添加账号** (Add Account).
3. Use the filter and sortable headers to find assignments. Select **查看详情** (View Details) to open the original assignment page.

### Staying signed in

Select **下次自动登录** (Sign in automatically next time) on the Chaoxing sign-in page to let the app reuse the official session. Chaoxing determines how long that session lasts.

For an individual account, you can also enable automatic re-sign-in using **永久登录** (Persistent Sign-in) in Account Management. When the session expires, the app attempts to sign in again through the official page. Complete any CAPTCHA, SMS code, or other verification on that page when prompted.

## Build from source

On Windows, install the **.NET 10 SDK** and the **Microsoft Edge WebView2 Runtime**. From the repository root, run the app with the following command.

```powershell
dotnet run --project StudyTasks/StudyTasks.csproj
```

To publish a self-contained, single-file build for Windows x64, run the following command.

```powershell
dotnet publish StudyTasks/StudyTasks.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

The executable is saved to `dist/StudyTasks.exe`.

## Local data

- Browser sessions are stored under `%LOCALAPPDATA%\ChaoxingStudyTasks\Browser`.
- The account list is stored in `%LOCALAPPDATA%\ChaoxingStudyTasks\accounts.json`. It includes display names, masked phone numbers, session identifiers, and the active account.
- If you enable automatic re-sign-in, the app stores the account credentials in Windows Credential Manager for the current Windows user. Disabling automatic re-sign-in or signing out of that account removes its stored credentials.

## Repository layout

```text
chaoxingTasksPanel/
├── StudyTasks/                # Windows app source, web scripts, and icon
├── tools/
│   └── render-study-icon.ps1  # Icon generation script
└── version-history.txt       # Changelog bundled with the app
```

## Changelog

See the [full version history](version-history.txt) in Simplified Chinese.
