# KeyHub Desktop

KeyHub Desktop 是一个面向 Windows 单用户环境的本机密钥工作台，用于集中管理 API Key、Token、密码、SSH 私钥和服务器配置。它通过普通环境变量把密钥交给项目，不要求项目依赖专用 SDK。

## 功能

- `.NET 10 + WPF + WPF-UI` 原生桌面端
- 使用当前 Windows 用户的 DPAPI 逐条加密密钥
- 密钥搜索、标签、到期时间、复制和操作记录
- 扫描并确认导入 `.env*` 与 SSH 私钥
- 读取项目 `.keyhub.json`，在本机建立环境变量映射
- `keyhub run` 启动项目并只在进程环境中注入密钥
- 显式导出 `.env` 或 JSON，支持原子替换
- 管理 Linux/Windows SSH 服务器和主机指纹
- 通过 SSH.NET 从内存加载私钥，部署环境配置并按预设重启服务
- 使用系统 OpenSSH 生成 Ed25519 密钥

## 快速开始

开发运行：

```powershell
dotnet run --project .\src\KeyHub.Desktop\KeyHub.Desktop.csproj
```

运行测试：

```powershell
dotnet test .\KeyHub.slnx -c Release
```

项目接入流程：

1. 在桌面端新增或导入密钥。
2. 新增项目并映射环境变量，例如 `OPENAI_API_KEY` → `OpenAI API`。
3. 使用 KeyHub CLI 启动项目。

```powershell
keyhub run --project demo-api -- dotnet run --project .\src\Demo.Api
```

项目仍然按普通方式读取环境变量：

```csharp
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
```

## 项目清单

项目可以提交一个不含密钥的 `.keyhub.json`：

```json
{
  "schema_version": 1,
  "project_id": "demo-api",
  "display_name": "Demo API",
  "required_environment": [
    "OPENAI_API_KEY",
    "SMTP_PASSWORD"
  ],
  "default_command": "dotnet run --project src/Demo.Api"
}
```

清单只声明变量名。具体密钥映射、个人目录和服务器信息均保存在本机数据库中。

## CLI

```powershell
keyhub doctor
keyhub run --project <project-id> -- <command> [args...]
keyhub export --project <project-id> --format dotenv --output .env
keyhub export --project <project-id> --format json --output secrets.json
keyhub deploy <deployment-id>
```

`keyhub run` 不创建中间文件。目标进程及其子进程可以读取注入的环境变量，进程退出后这些变量随进程消失。

安装版会把安装目录下的 `cli` 子目录加入当前用户 PATH。便携版请直接运行 `cli\keyhub.exe`，或自行把该目录加入 PATH。

## SSH 与部署

- SSH 认证密钥可以导入 KeyHub、在 KeyHub 中生成，或使用 `file://C:/Users/name/.ssh/id_ed25519` 引用现有文件。
- 首次连接必须在桌面端测试并确认 SHA256 主机指纹。
- Linux 重启命令只允许 `systemctl restart <service>`。
- Windows 重启命令只允许 `Restart-Service -Name <service>`。
- 服务器上的目标配置是明文文件；传输过程通过 SSH 加密。

## 数据位置与边界

数据库位于 `%LocalAppData%\KeyHubDesktop\keyhub.db`。密钥值使用 `DataProtectionScope.CurrentUser` 加密，没有云同步、恢复密码或自动备份。Windows 用户配置丢失后，原数据库可能无法解密。

KeyHub 的目标是减少密钥散落和误提交，不用于抵御同一 Windows 用户下运行的恶意软件、管理员攻击或内存窃取。详见 [SECURITY.md](SECURITY.md) 和 [PRIVACY.md](PRIVACY.md)。

## 发布

推送 `v*` 标签后，GitHub Actions 会运行测试和密钥扫描，并生成：

- 自包含 x64 便携 ZIP
- Inno Setup 用户级安装包
- SHA256 校验文件

当前发布物未签名，Windows SmartScreen 可能显示提示。

## English

KeyHub Desktop is a local-first Windows secret desk for API keys, tokens, SSH keys, project environment injection, and SSH-based configuration deployment. Secrets are encrypted with Windows DPAPI for the current user. It has no cloud sync, telemetry, team sharing, or browser autofill.

## License

[MIT](LICENSE)
