# 全局响应式界面与反馈闭环 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让流氓软件克星在常见 Windows DPI 和窄窗口下完整可用，并建立每轮改动必查的 GitHub Issue 反馈闭环。

**Architecture:** 主窗体继续由 `MainForm.BuildUi()` 组装；新增轻量布局协调逻辑，只在容器尺寸变化时改变统计卡网格和详情分栏，业务扫描逻辑不动。Issue 反馈结论写入项目文档，公开 Issue 不做任何写入。

**Tech Stack:** C# WinForms、.NET Framework 编译器、PowerShell 构建脚本、GitHub CLI。

## Global Constraints

- 不缩小字体或裁切按钮文本；不足空间用重排或滚动。
- 不改变扫描、清理、备份和管理员权限业务逻辑。
- 不提交、推送、更新版本号或变更公开 Issue 状态。
- 所有公开反馈均先通过本地代码和 UI 回归验证后才能标为已覆盖。

---

### Task 1: 建立反馈核对清单

**Files:**
- Create: `docs/validation/2026-08-08-github-issue-triage.md`

- [ ] **Step 1: 获取 Issues 的正文和评论**

Run: `gh issue list --repo aakk007/RogueCleaner --state all --limit 100 --json number,title,state,body,comments,url`

Expected: 记录当前所有 Issue 的可复核输入，不写入 GitHub。

- [ ] **Step 2: 将每项反馈映射到证据和下一步**

Write a table for #1, #3, #4, #5 and #7. Include status, supporting source/validation evidence, and an explicit next action.

### Task 2: 改造主窗体的响应式布局

**Files:**
- Modify: `src/v2/RogueCleanerV2.cs:4564-4815`

- [ ] **Step 1: 使顶部工具和标题操作可横向访问**

Use non-wrapping `FlowLayoutPanel` instances with `AutoScroll = true`; retain the current button minimum sizes and no text clipping.

- [ ] **Step 2: 让统计卡适应可用宽度**

Retain the four summary cards, but recompute the `TableLayoutPanel` row/column placement on `SizeChanged`: use four columns when each card can fit, otherwise two columns/two rows. Update the content row height to 62 or 112 logical pixels as required.

- [ ] **Step 3: 保护结果表和右侧详情栏**

Set a DPI-aware detail panel minimum width; clamp `contentSplit.SplitterDistance` after resize so the details are not clipped and the result table stays usable. Preserve existing grid column minimums and scrolling.

- [ ] **Step 4: 让页脚在窄窗口下降级显示**

Replace the fixed 400px author region with a percent/AutoSize layout that preserves the status and links without overlap.

### Task 3: 扩展 UI 自动回归与验证

**Files:**
- Modify: `src/v2/RogueCleanerV2.cs` (existing `--ui-smoke` implementation)

- [ ] **Step 1: 增加四档 DPI 布局检查**

Use the existing UI smoke harness to set representative client sizes and verify each primary action, all summary-card titles, result grid and detail panel remain visible without negative or overlapping bounds.

- [ ] **Step 2: 编译和运行回归**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Exe.ps1`; then `git diff --check`; then start `流氓软件克星.exe --ui-smoke` via `Start-Process -PassThru -Wait`.

Expected: all commands return 0 and reports contain screenshots.

### Task 4: 复核反馈状态并记录后续项

**Files:**
- Modify: `docs/validation/2026-08-08-github-issue-triage.md`

- [ ] **Step 1: 回写本轮验证证据**

Mark #3 as fixed only if the new UI smoke evidence proves its reported DPI/toolbar cases. Mark #1, #4, #5 and #7 as separate product/security follow-ups unless code and a relevant regression test are completed in this same change.

- [ ] **Step 2: 复查未完成项范围**

Confirm that no open feedback has silently been dismissed: whitelist (#1), rule-store proposal (#4), virtual-drive governance (#5), and antivirus false-positive investigation (#7) each have a documented safe next step.
