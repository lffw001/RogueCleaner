# 白名单、虚拟盘只读诊断与厂商复核材料 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付用户可控白名单、网盘虚拟盘只读证据与火绒复核材料。

**Architecture:** 白名单以本地 JSON 保存稳定项目键，并在扫描结果层阻止选择和清理；虚拟盘只追加 `ReportOnly` 结果；厂商复核材料从当前 EXE 元数据和 SHA-256 生成 Markdown。

**Tech Stack:** C# WinForms、.NET Framework、JavaScriptSerializer、Windows DriveInfo、现有 JSON/烟测框架。

## Global Constraints

- 不引入运行时外部规则文件、第三方依赖或 GitHub Token。
- 不自动清理、卸载、禁用或删除网盘虚拟盘及相关设备。
- 未经用户明确要求，不提交、推送、更新版本或创建 Release。

---

### Task 1: 白名单持久化与扫描展示

**Files:**
- Modify: `src/v2/RogueCleanerV2.cs`
- Test: `src/v2/RogueCleanerV2.cs` 的自测入口

- [ ] 新增 `UserWhitelistStore`，以 `state/user-whitelist.json` 读写格式版本和稳定项目键。
- [ ] 扫描排序前标记命中的项目为“已白名单”，保留证据并令其不可勾选、不可批量清理。
- [ ] 添加“加入白名单/管理白名单”界面入口，用户只能从现有结果加入，并能移除条目。
- [ ] 添加稳定键、持久化与不可清理的自测。

### Task 2: 网盘虚拟盘只读诊断

**Files:**
- Modify: `src/v2/RogueCleanerV2.cs`
- Test: `src/v2/RogueCleanerV2.cs` 的扫描回归

- [ ] 用 `DriveInfo.GetDrives()` 枚举盘符，只采集名称、卷标、格式与 DriveType。
- [ ] 仅为网盘特征明确的盘符创建 `ReportOnly` 结果，显示“只读诊断，不修改设备或盘符”。
- [ ] 覆盖网络盘和命名网盘样例，断言结果永远不可清理。

### Task 3: 厂商复核材料与端到端验证

**Files:**
- Modify: `src/v2/RogueCleanerV2.cs`
- Modify: `README.md`

- [ ] 生成包含 EXE 版本、SHA-256、签名主体、生成时间和复核说明的本地 Markdown。
- [ ] 增加命令行烟测并确认哈希可复算、敏感路径不进入材料。
- [ ] 运行构建、扫描、UI、反馈以及新增烟测，记录报告位置。
