# scripts/

按用途分三组:

## 构建辅助 (.NET 侧)

> `build-dotnet.cmd` 不在本目录,在仓库根 (跟 `build_native.cmd` 同级);clean 在本目录。

| 脚本 | 位置 | 用途 |
|---|---|---|
| `build-dotnet.cmd [Configuration]` | `..\build-dotnet.cmd` | `dotnet restore` + `dotnet build -c <CFG>`,默认 Release;native 走 `..\build_native.cmd` |
| `clean-dotnet.cmd [Configuration]` | `scripts\clean-dotnet.cmd` | `dotnet clean` + 递归 prune 每个 `<csproj-dir>\bin` + `\obj` |

## 发布 / 启动 (面向客户 + 新手)

| 脚本 | 用途 |
|---|---|
| `stage.ps1` | 唯一运行包物化入口：默认 build/publish managed；`-BuildNative` 时同时构建 native；生成 `artifacts/stage/<Configuration>` 与 SHA256 manifest，不写 `deploy/` |
| `deploy.ps1` | 校验既有 Stage 的 manifest/hash 后原子发布到目标目录；不触发 build |
| `publish-deploy.ps1` | 兼容快捷入口：依次调用 Stage → Deploy；默认同时构建 native，可选 `-SkipBuild` / `-SkipNativeBuild` / `-Zip` |
| `launch.ps1` | deploy 形态的启动器,**通常不直接跑,由 `deploy/start.bat` 调入** |

### 典型用法

```cmd
REM 开发期 (改完代码即跑)
dotnet build CreoToolkitRefactor.slnx -c Release
scripts\stage.ps1 -Configuration Release -BuildNative
scripts\deploy.ps1 -StageRoot artifacts\stage\Release -Destination deploy
deploy\start-protk-samples-winforms.bat   REM 7 个主题 dialog + CreoToolkit 菜单

REM 发版本 (打 deploy.zip 发给客户)
scripts\publish-deploy.ps1 -Clean -Zip

REM 客户机器 (已解压 deploy/)
deploy\start.bat                         REM catalog 第一个 app，当前为 core/nogui edition
deploy\start-protk-samples-winforms.bat  REM 完整交互 sample
```

> `manual-launch-samples.ps1` 仍保留作历史诊断脚本，但其旧 `NativeHost\out\managed`
> 开发目录契约已停用，不应作为当前启动入口。当前开发和客户启动均以 Stage/Deploy 布局为准。

## CI / lint / 运维

| 脚本 | 用途 |
|---|---|
| `ci-gate.ps1` | 统一 CI 门禁入口:无 Creo build + test + lint,一条命令跑全部回归门 |
| `lint-no-creoworkdir-hardcode.ps1` / `.cmd` | grep gate:禁硬编码工作目录路径与旧 host-log 路径形态 (cmd 为转发 shim) |
| `lint-encoding.ps1` | 编码卫生门:`.def` 全 ASCII / 含中文 `.ps1` 必须 UTF-8 BOM / `.cmd`/`.bat` 全 ASCII |
| `enable-wer-localdumps.cmd` / `disable-wer-localdumps.cmd` | 注册/撤销 WER LocalDumps (崩溃 dump 兜底,零代码接管) |
| `run-user-tests.ps1` | 用户自定义测试:启 Creo 跑元素树导出 (需 `CTK_REAL_CREO=1` + `CTK_CREO_LAUNCHER`) |
| `generate-drawing-fixture.ps1` | 生成工程图测试 fixture (需要交互式 Creo 会话) |

---

## 失败定位

| 退出码 | 含义 |
|---|---|
| `publish-deploy.ps1` 91-96 | dotnet SDK / native build / dotnet build / deploy 完整性缺失 |
| 其他 | 透传 dotnet / cmake / cmd 自身退出码 |
