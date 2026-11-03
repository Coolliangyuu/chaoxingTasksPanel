# chaoxingTasksPanel · 学习通作业看板

[English](README.md) | **简体中文**

一个用来汇总超星学习通作业的 Windows 桌面应用。打开看板即可查看各门课的作业、提交状态和截止倒计时，也能直接打开作业详情、下载附件和切换账号。

`Windows x64` · `C# / .NET 10` · `WinForms` · `v1.4.9`

## 界面截图

截图来自离线演示版，作业和账号均为虚构数据。

### 作业看板

![显示提交状态和截止时间的作业看板](screenshots/demo-dashboard.png)

### 账号管理

![显示四个虚构账号的账号管理窗口](screenshots/demo-accounts.png)

## 功能

| 功能 | 说明 |
| --- | --- |
| 作业看板 | 显示作业、课程、提交状态、北京时间截止时间和剩余时间。 |
| 筛选与排序 | 筛选待处理或已完成的作业。点击「提交状态」「截止时间」或「距离截止」切换排序方向，点击「作业」恢复默认顺序。 |
| 自动刷新 | 看板每 5 分钟检查一次，也可以点击「立即刷新」。 |
| 详情与附件 | 点击「查看详情」独立打开每个作业，可通过 Alt+Tab 或任务栏预览切换。下载附件时可以选择文件名和保存位置。 |
| 多账号管理 | 自动显示姓名和脱敏手机号，各账号的登录会话独立保存。支持添加、切换和退出账号。 |
| 登录状态 | 启动时检查会话状态，显示是否已登录、会话是否仅限本次以及可确认的保留期限。 |
| 内置版本日志 | 点击看板右下角的「查看日志」，可以滚动查看并复制版本记录。 |

## 开始使用

1. 双击 Windows x64 版 exe，或按下方说明从源码运行。应用需要 Microsoft Edge WebView2 运行时。
2. 首次使用时，在应用内的超星官方页面登录。需要使用其他账号时，在右上角选择「账号管理」→「添加账号」。
3. 登录后，使用筛选器和可排序的表头查找作业。点击一行右侧的「查看详情」即可打开作业原页。

### 登录保留与自动重登

登录时勾选超星官方的「下次自动登录」，应用会优先沿用该会话，保留期限由学习通决定。

在账号管理中，也可以通过「永久登录」为单个账号启用自动重登。应用检测到会话失效时，会在超星官方页面尝试重新登录。遇到短信验证码、滑块或其他验证时，由用户在页面上完成。

## 从源码构建

在 Windows 安装 **.NET 10 SDK** 和 **Microsoft Edge WebView2 运行时**。在仓库根目录执行以下命令启动应用。

```powershell
dotnet run --project StudyTasks/StudyTasks.csproj
```

构建 Windows x64 单文件版本。

```powershell
dotnet publish StudyTasks/StudyTasks.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

生成的可执行文件位于 `dist/StudyTasks.exe`。

## 本机数据

- 浏览器会话保存在 `%LOCALAPPDATA%\ChaoxingStudyTasks\Browser`。
- 账号列表保存在 `%LOCALAPPDATA%\ChaoxingStudyTasks\accounts.json`，其中包括显示姓名、脱敏手机号、会话标识和当前账号。
- 启用自动重登后，账号密码保存在当前 Windows 用户的凭据管理器中。关闭自动重登或退出对应账号时，应用会清除该账号保存的凭据。

## 仓库结构

```text
chaoxingTasksPanel/
├── StudyTasks/                # Windows 应用源码、网页读取脚本和图标
├── tools/
│   └── render-study-icon.ps1  # 图标生成脚本
└── version-history.txt       # 构建时内置到应用的版本日志
```

## 版本记录

查看从首次发布到当前版本的[完整版本日志](version-history.txt)。
