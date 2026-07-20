# 国内捆绑/右键/弹窗覆盖矩阵

本文件记录 v2 内置规则覆盖范围。它不是“永久全覆盖”的承诺，后续应按用户反馈和真机样本持续补充。

## 已覆盖厂商/组件族

- 360 系列：安全卫士、软件管家、压缩、浏览器、看图、AI 图片、主动防御、右键扩展、守护服务。
- WPS / 金山：WPS Office、金山文档、WPS 图片、WPS 云盘/磁盘入口、WPS AI/灵犀/旺仔、更新任务、云服务、右键扩展。
- 百度 / 百度网盘：网盘主程序、看图组件、同步组件、YunShell、Native Messaging Host、检测/守护服务。
- 搜狗：输入法、浏览器、弹窗、新闻、皮肤/推荐、云服务、守护、自启动。
- 迅雷：主程序、下载助手浏览器扩展、XLService/ThunderPlatform、ThunderStart、ThunderBrowser。
- 腾讯系：QQ 浏览器、电脑管家、QQProtect、QQPCRTP、腾讯文档、微信/企业微信相关右键或启动残留。
- 2345 系列：浏览器、看图王、好压、软件管家、安全卫士、MiniPage/迷你页、保护服务。
- 猎豹 / 金山毒霸：猎豹浏览器、金山毒霸、KSafe/KWatch/KAV 常驻项。
- 驱动/硬件检测工具：驱动精灵、驱动人生、鲁大师、MasterLu、检测守护和新闻弹窗。
- 国产压缩/看图工具：快压、好压、2345Zip、360 压缩、2345Pic、看图王等。
- 国产浏览器/导航：360、搜狗、QQ、2345、猎豹、傲游、UC、百度浏览器等。
- Flash 中国特供组件：FlashHelperService、FlashCenter、Flash 大厅等。
- 手机助手/设备助手：爱思、PP、91、豌豆荚、应用宝、华为/小米手机助手等。
- 国产影音/游戏大厅：爱奇艺、优酷、酷狗、酷我、PPTV、暴风、腾讯视频、芒果、WeGame 等。
- PDF/办公捆绑工具：极速 PDF、迅捷 PDF、福昕、CAJViewer、嗨格式等。
- 预装管家/厂商助手：联想、华为、荣耀、小米、部分 OEM 管家类工具。
- 弹窗广告/推广组件：SogouNews、SogouPopup、2345MiniPage、AdPop、WpsNotify、热点资讯、迷你页等。
- 守护/自动恢复组件：QHWatchdog、SGImeGuard、XLServicePlatform、BaiduYunDetect、QQProtect、2345Protect、KSafeSvc、FlashHelperService 等。

## 扫描位置

- 右键菜单：`shell`、`shellex\ContextMenuHandlers`、图片/视频/音频/文本/压缩包专用右键、磁盘/文件夹/快捷方式右键。
- CLSID 反查：右键扩展只有 GUID 时，反查 `CLSID\{...}` 的默认名、`InprocServer32`、`LocalServer32` 和 `ProgID`。
- 自启动：HKCU/HKLM `Run`、`RunOnce` 和启动文件夹，`.lnk` 会解析真实目标。
- 服务：`Win32_Service`，匹配后备份启动状态并禁用。
- 计划任务：Task Scheduler COM，匹配后备份 XML 和启用状态并禁用。
- 浏览器插件/宿主：Chrome、Edge、Mozilla Native Messaging Host，Chrome/Edge 强制扩展策略。
- 文件关联：常见图片、视频、压缩包、下载、Office/PDF 文件的默认打开程序和 OpenWith 残留。
- 隐藏卸载入口：Uninstall 注册表里标记 `SystemComponent`、`NoRemove`、无显示名、无卸载命令或挂在父组件下的条目，只报告不自动卸载。
- 运行进程：对弹窗、推广、守护和自动恢复组件只报告，不默认杀进程。

## 安全边界

- 主程序和“全家桶”静默卸载未默认启用。
- 隐藏卸载入口只报告，不一键卸载。
- 默认打开程序只报告，不一键改。
- 泛化词不能直接用于删除判断，例如 `Protect`、`UpdateService` 这类词会误伤系统组件，必须使用具体组件名或结合厂商证据。
- 新增厂商规则后必须跑 `--scan-smoke`，并检查 Windows 自带项是否误报。
