# GitHub Issue #8 本地核对（2026-08-13）

本文件是本轮本地修复的只读反馈快照；未修改任何公开 Issue、评论或标签，未提交、未推送、未发布。

| 项 | 内容 |
|---|---|
| Issue | [清理之后右键菜单完全崩溃且无法恢复 #8](https://github.com/aakk007/RogueCleaner/issues/8) |
| 症状 | 右键菜单崩溃；"显示更多选项"直接崩溃；AMD Software 无故自行启动；恢复中心不可用（某 42 条批次恢复 40 条、2 条不可恢复，提示"失败记录保留在恢复中心"但实际看不到） |
| 当前结论 | 根因已定位并本地修复；需真实设备回归后再决定公开回复/关闭 |

## 根因分析（三条链路）

1. **备份失败仍继续删除 → 永久不可恢复**
   `CleanerEngine.CleanOne()` 的 `DeleteRegistryKey` / `DeleteRegistryValue` 分支调用 `BackupRegistry()`（内部 `reg export`）后不检查返回值，即使导出失败（返回 null）仍继续删除注册表键/值。一旦删除的是右键菜单注册项且没有备份，恢复中心就找不到 `.reg` 备份 → 菜单永久损坏，与症状 1/2 吻合。

2. **扫描分类只保护"命令仓库"里的核心动词 → 核心动词/系统命令可被误删**
   `ContextMenuDiagnosisPolicy.Classify()` 原逻辑 `!extension && entry.AdvancedOnly && IsCoreFileTypeVerb(...)` 只保护 `AdvancedOnly=true` 的"命令仓库"条目；`Software\Classes\*\shell\open`、`exefile\shell\open`、`Directory\Background\shell\cmd` 等真实系统动词/系统命令在身份被误判为第三方时会被归为 `ActionableCommand`，整键删除后 Explorer 右键菜单损坏；AMD 等 Shell 扩展反复加载也与这类误删有关。

3. **恢复中心不留失败项 → 用户看不到可重试项**
   `RestoreBatch()` 只返回汇总计数，从不把恢复结果写回 `manifest.json`；`RecoveryCenterForm.RestoreSelectedBatch()` 部分失败时只弹窗，批次里成功项仍显示为"已处理"，失败项没有独立状态，重试也不会真正执行（`RestoreResult` 对非 `Done` 状态一律返回"无需恢复"）。

## 修复内容（src/v2/RogueCleanerV2.cs）

1. **备份失败禁止删除**：`CleanOne()` 中注册表键/值删除前若 `BackupRegistry` 返回 null，标记 `Failed`、消息"注册表备份失败，已取消删除，避免右键菜单或系统设置无法恢复。"，不再执行删除；`MoveFileToBackup` 增加源文件不存在保护，避免误报成功。
2. **核心动词无条件保护**：`Classify()` 对 `open/edit/print/printto/new/runas/runasuser/play/preview` 无论是否 AdvancedOnly 一律返回 `Ignore`，绝不进入可清理分支。
3. **系统命令只提示**：新增 `SystemProtected` 分类，命令路径指向 `%SystemRoot%\System32` / `SysWOW64` / `System` / `explorer.exe` 的右键命令只生成"系统右键命令（保护）"只读条目，不自动删除。
4. **恢复中心回写失败项**：
   - `RestoreBatch()` 恢复成功项标记 `Restored`、失败项标记 `RestoreFailed` 并写入失败原因；
   - 新增 `RewriteBatchManifest()`：部分失败时把成功项从批次移除、只把失败项写回 `manifest.json`；
   - `RestoreSelectedBatch()` 部分失败后回写清单并刷新列表，提示"成功项已移除、失败项已保留可重试"；
   - `RestoreResult()` 允许对 `RestoreFailed` 项重试（不再误判为"无需恢复"）；
   - 列表/网格计数与配色纳入 `RestoreFailed`（红色"恢复失败"）。
5. **新增回归**：`Issue8RegressionChecks`（VALIDATION 构建）覆盖备份失败取消删除、部分恢复回写 manifest、核心动词/系统命令保护三条链路。

## 验证

- 构建：`Build-Exe.ps1 -ValidationBuild`、`Build-Exe.ps1` 均成功（csc 编译无警告错误）。
- 冒烟：`--context-menu-smoke`、`--special-menu-smoke`、`--advanced-menu-smoke`、`--ui-smoke` 全部退出码 0。
- 验收：`--acceptance-test` RunnableCases=16，其中新增 Issue8 3 项全部 Pass；其余 5 Fail + 1 SetupFailed 与 HEAD 基线（stash 后同机同日重跑）完全一致，属本机环境既有问题（计划任务 COM E_FAIL、部分 HKCU 右键根、Shell 扩展恢复复核、服务创建），与本次改动无关。

## 剩余风险 / 下一步

- 本次为本地修复与回归，未公开回复/关闭 Issue；建议真实设备复现"清理→右键菜单→恢复中心"后再决定发布口径。
- 预存的环境性验收失败（计划任务/服务/HKCU 根枚举）不在本 Issue 范围，可另立跟踪。
- AMD Software 自启仍可能与 Explorer 扩展加载相关，需真实样本确认是否由 CLSID/扩展误删引发；当前已通过"核心动词/系统命令保护 + 备份失败中止"降低同类误删风险。
- 未提交、未推送、未发布；发布说明若涉及本修复需中文并走既有发布流程。

## 公开回复记录（2026-08-13）

- 已按用户明确授权，向 Issue #8 发布公开回复：[issuecomment-5274536483](https://github.com/aakk007/RogueCleaner/issues/8#issuecomment-5274536483)。
- 回复内容：说明本地已完成三条链路修复（备份失败不再删除、核心右键动词/系统命令保护、恢复中心部分失败后保留失败项并写回 manifest），并请用户提交真实样本（最近批次 manifest.json、右键菜单崩溃截图与「显示更多选项」事件日志、AMD Software 版本与进程路径、复现步骤），承诺拿到样本后做真实环境回归再决定发布/关闭。
- 未关闭 Issue、未添加标签、未 commit、未 push、未 release。