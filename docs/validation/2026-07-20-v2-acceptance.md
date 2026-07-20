# v2 发布前验收记录

日期：2026-07-20

## 结论

当前不能声明“所有清理功能已验证通过”。

已验证：

- v2 可以编译成单文件 `流氓软件克星.exe`。
- 独立目录运行 `--scan-smoke` 成功，退出码 `0`。
- 单文件运行时只在 exe 同目录创建 `流氓软件克星数据`。
- `dist\流氓软件克星` 发布目录只包含 exe 和 README。
- 规则词库已补充 360 看图/AI 图片、WPS 图片/金山云/旺仔、百度网盘看图/同步、搜狗弹窗/守护、迅雷自启等常见名称。

未验证通过：

- 当前 Codex 进程不是管理员。
- 当前环境禁止创建 HKCU 注册表测试工件，`HKCU\Software\Classes`、`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`、Chrome NativeMessagingHosts、`.png\OpenWithProgids` 均返回 `Access denied`。
- 当前环境无法创建计划任务测试工件。
- 因为模拟工件无法创建，所以右键菜单、启动项、浏览器插件、文件打开方式、计划任务的“清理后真实消失、恢复后真实回来”还没有在本机通过。
- 后台服务清理需要管理员权限，本次未创建模拟服务，未验证。
- 360/WPS/百度网盘/搜狗/迅雷主程序或全家桶静默卸载尚未实现，也未验证；当前 v2 只清理右键、自启、计划任务、服务、插件和文件关联残留，不承诺完整卸载主程序。

## 自动验收命令

```powershell
.\流氓软件克星.exe --acceptance-test
```

该命令会创建 `CodexRogueCleanerTest` 前缀的模拟工件，调用正式扫描、清理和恢复核心，并写入：

```text
流氓软件克星数据\reports\acceptance-*.json
```

判定标准：

- `Pass`：扫描命中，清理后回读消失，恢复后回读出现。
- `SetupFailed`：当前系统权限或策略导致模拟工件没创建成功，不能算功能通过。
- `Skipped`：例如服务测试在非管理员状态跳过，不能算服务功能通过。

## 本次独立目录结果

独立运行目录：

```text
D:\Codex\Workspace\RogueCleanerAcceptanceLab-20260720-223955
```

验收摘要：

```text
RunnableCases=8, Passed=0, Failed=0, SetupFailed=8, Skipped=1
```

失败原因集中在测试工件创建阶段：

- 注册表项创建被拒绝。
- 计划任务创建后无法回读。
- 服务测试因非管理员跳过。

## 下一步发布门槛

发布前必须在普通 Windows 10/11 虚拟机或真实测试机上，以管理员方式运行 `--acceptance-test`，并拿到：

```text
Failed=0
SetupFailed=0
```

如果服务测试仍跳过，不能宣称服务清理已验证。

如果要加入“全家桶静默卸载”，必须单独做卸载器白名单、静默参数白名单、安装/卸载 VM 验收和失败回滚说明，不能和残留清理混成同一个默认一键动作。
