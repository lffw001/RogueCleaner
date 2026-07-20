[CmdletBinding()]
param(
    [switch]$Scan,
    [switch]$Interactive,
    [switch]$Apply,
    [string]$Selection,
    [switch]$Restore,
    [string]$Backup,
    [switch]$NoGui,
    [switch]$IncludeTrusted,
    [switch]$IncludeInstalledApps,
    [switch]$ValidateGui
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$Script:BundleRoot = if ($env:ROGUE_CLEANER_BUNDLE) {
    [System.IO.Path]::GetFullPath($env:ROGUE_CLEANER_BUNDLE)
} elseif ($PSScriptRoot) {
    $PSScriptRoot
} else {
    Split-Path -Parent $PSCommandPath
}
$Script:Root = if ($env:ROGUE_CLEANER_HOME) {
    [System.IO.Path]::GetFullPath($env:ROGUE_CLEANER_HOME)
} else {
    $Script:BundleRoot
}
$Script:RulesDir = if ($env:ROGUE_CLEANER_RULES_DIR) {
    [System.IO.Path]::GetFullPath($env:ROGUE_CLEANER_RULES_DIR)
} else {
    Join-Path $Script:BundleRoot 'rules'
}
$Script:ReportsDir = Join-Path $Script:Root 'reports'
$Script:BackupsDir = Join-Path $Script:Root 'backups'
$Script:CurrentFindings = @()
$Script:LastReportPath = $null

New-Item -ItemType Directory -Path $Script:ReportsDir -Force | Out-Null
New-Item -ItemType Directory -Path $Script:BackupsDir -Force | Out-Null

function Read-JsonFile {
    param([Parameter(Mandatory)][string]$Path)
    if (!(Test-Path -LiteralPath $Path)) {
        throw "缺少规则文件：$Path"
    }
    Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

$Script:VendorRules = Read-JsonFile (Join-Path $Script:RulesDir 'vendors.json')
$Script:Locations = Read-JsonFile (Join-Path $Script:RulesDir 'locations.json')
$Script:BehaviorRules = Read-JsonFile (Join-Path $Script:RulesDir 'behaviors.json')

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
}

function ConvertTo-NativeRegistryPath {
    param([Parameter(Mandatory)][string]$Path)
    $p = $Path -replace '^Microsoft\.PowerShell\.Core\\Registry::', ''
    $p = $p -replace '^HKEY_LOCAL_MACHINE', 'HKLM'
    $p = $p -replace '^HKEY_CURRENT_USER', 'HKCU'
    $p = $p -replace '^HKLM:\\', 'HKLM\'
    $p = $p -replace '^HKCU:\\', 'HKCU\'
    $p
}

function ConvertTo-ProviderRegistryPath {
    param([Parameter(Mandatory)][string]$Path)
    $p = $Path -replace '^HKEY_LOCAL_MACHINE', 'HKLM:'
    $p = $p -replace '^HKEY_CURRENT_USER', 'HKCU:'
    $p = $p -replace '^HKLM\\', 'HKLM:\'
    $p = $p -replace '^HKCU\\', 'HKCU:\'
    $p
}

function Get-RegDefaultValue {
    param([Parameter(Mandatory)][string]$Path)
    try {
        (Get-Item -LiteralPath $Path -ErrorAction Stop).GetValue('')
    } catch {
        $null
    }
}

function Get-RegValueNamesSafe {
    param([Parameter(Mandatory)][string]$Path)
    try {
        (Get-Item -LiteralPath $Path -ErrorAction Stop).GetValueNames()
    } catch {
        @()
    }
}

function Resolve-ExecutablePath {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }

    $expanded = [Environment]::ExpandEnvironmentVariables($Text.Trim())
    if ($expanded -match '^\s*"([^"]+\.(?:exe|dll|com|bat|cmd|ps1))"') {
        return $Matches[1]
    }
    if ($expanded -match '([A-Za-z]:\\[^\r\n,"]+?\.(?:exe|dll|bat|cmd|ps1|com))(?=\s|$|")') {
        return $Matches[1].Trim()
    }
    if ($expanded -match '^\s*([^\s"]+\.(?:exe|dll|com|bat|cmd|ps1))') {
        $candidate = $Matches[1]
        $cmd = Get-Command $candidate -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($cmd -and $cmd.Source) { return $cmd.Source }
        return $candidate
    }
    return $null
}

function Resolve-Clsid {
    param([string]$Clsid)
    if ([string]::IsNullOrWhiteSpace($Clsid)) { return $null }
    $id = $Clsid.Trim()
    if ($id -match '(\{[0-9A-Fa-f-]{36}\})') {
        $id = $Matches[1]
    } else {
        return $null
    }

    foreach ($root in @(
        "HKCU:\Software\Classes\CLSID\$id",
        "HKLM:\Software\Classes\CLSID\$id",
        "HKCU:\Software\Classes\WOW6432Node\CLSID\$id",
        "HKLM:\Software\Classes\WOW6432Node\CLSID\$id"
    )) {
        if (Test-Path -LiteralPath $root) {
            $server = Get-RegDefaultValue (Join-Path $root 'InprocServer32')
            if (!$server) { $server = Get-RegDefaultValue (Join-Path $root 'LocalServer32') }
            return [pscustomobject]@{
                Clsid = $id
                Name = Get-RegDefaultValue $root
                RegistryPath = $root
                Server = $server
            }
        }
    }
    [pscustomobject]@{
        Clsid = $id
        Name = $null
        RegistryPath = $null
        Server = $null
    }
}

function Get-FileEvidence {
    param([string]$CommandOrPath)
    $target = Resolve-ExecutablePath $CommandOrPath
    if (!$target) {
        return [pscustomobject]@{
            Path = $null
            Exists = $null
            Company = $null
            Product = $null
            Description = $null
            Signature = $null
            Signer = $null
        }
    }

    $target = [Environment]::ExpandEnvironmentVariables($target.Trim('"'))
    $exists = $false
    try {
        $exists = Test-Path -LiteralPath $target -PathType Leaf -ErrorAction Stop
    } catch {
        $exists = $false
    }
    $company = $null
    $product = $null
    $description = $null
    $signature = $null
    $signer = $null

    if ($exists) {
        try {
            $item = Get-Item -LiteralPath $target
            $company = $item.VersionInfo.CompanyName
            $product = $item.VersionInfo.ProductName
            $description = $item.VersionInfo.FileDescription
        } catch {}
        try {
            $sig = Get-AuthenticodeSignature -LiteralPath $target
            $signature = [string]$sig.Status
            if ($sig.SignerCertificate) { $signer = $sig.SignerCertificate.Subject }
        } catch {}
    }

    [pscustomobject]@{
        Path = $target
        Exists = $exists
        Company = $company
        Product = $product
        Description = $description
        Signature = $signature
        Signer = $signer
    }
}

function Join-Text {
    param([object[]]$Parts)
    ($Parts | Where-Object { $_ -ne $null -and "$_".Trim().Length -gt 0 }) -join ' '
}

function Get-ObjectPropertyValue {
    param(
        [object]$Object,
        [Parameter(Mandatory)][string]$Name
    )
    if ($null -eq $Object) { return $null }
    if ($Object.PSObject.Properties.Name -contains $Name) { return $Object.$Name }
    return $null
}

function Resolve-Vendor {
    param([string]$Text)
    foreach ($vendor in $Script:VendorRules.vendors) {
        foreach ($pattern in $vendor.patterns) {
            if ($Text -match [regex]::Escape($pattern)) {
                return $vendor
            }
        }
    }
    return $null
}

function Test-TrustedText {
    param([string]$Text)
    if ($Text -match 'Microsoft Corporation|Microsoft\\Windows|\\Windows\\System32\\|\\Windows\\SysWOW64\\|WindowsApps\\Microsoft\.|com\.microsoft\.|Microsoft Edge|Microsoft Visual C\+\+') {
        return $true
    }
    foreach ($publisher in $Script:VendorRules.trustedPublishers) {
        if ($Text -match [regex]::Escape($publisher)) { return $true }
    }
    return $false
}

function Get-RiskName {
    param([int]$Score)
    if ($Score -ge [int]$Script:BehaviorRules.riskBands.high) { return '高' }
    if ($Score -ge [int]$Script:BehaviorRules.riskBands.medium) { return '中' }
    return '低'
}

function Get-ActionName {
    param([string]$SourceType)
    $map = $Script:BehaviorRules.defaultActions
    if ($map.PSObject.Properties.Name -contains $SourceType) { return $map.$SourceType }
    return 'ReportOnly'
}

function New-Finding {
    param(
        [Parameter(Mandatory)][string]$SourceType,
        [Parameter(Mandatory)][string]$Title,
        [string]$Location,
        [string]$Command,
        [string]$RegistryPath,
        [string]$RegistryValueName,
        [string]$TargetPath,
        [object]$ActionData,
        [string]$Extra,
        [switch]$ForceInclude
    )

    $evidence = Get-FileEvidence (Join-Text @($TargetPath, $Command))
    $text = Join-Text @($Title, $Location, $Command, $RegistryPath, $RegistryValueName, $TargetPath, $Extra, $evidence.Path, $evidence.Company, $evidence.Product, $evidence.Description, $evidence.Signer)
    $vendor = Resolve-Vendor $text
    $trusted = Test-TrustedText $text
    $score = 0

    if ($vendor) { $score += [int]$Script:BehaviorRules.riskWeights.knownVendor + [int]$vendor.riskBoost }
    switch ($SourceType) {
        'ContextMenu' { $score += [int]$Script:BehaviorRules.riskWeights.contextMenu }
        'StartupRegistry' { $score += [int]$Script:BehaviorRules.riskWeights.startup }
        'StartupFolder' { $score += [int]$Script:BehaviorRules.riskWeights.startup }
        'ScheduledTask' { $score += [int]$Script:BehaviorRules.riskWeights.scheduledTask }
        'Service' { $score += [int]$Script:BehaviorRules.riskWeights.service }
        'BrowserExtension' { $score += [int]$Script:BehaviorRules.riskWeights.browserExtension }
        'FileAssociation' { $score += [int]$Script:BehaviorRules.riskWeights.fileAssociation }
        'InstalledApplication' { $score += 70 }
        'UninstallResidue' { $score += [int]$Script:BehaviorRules.riskWeights.uninstallResidue }
        'AppxContextMenu' { $score += [int]$Script:BehaviorRules.riskWeights.contextMenu }
    }
    if ($vendor) {
        foreach ($component in $vendor.knownBadComponents) {
            if ($text -match [regex]::Escape($component)) {
                $score += [int]$Script:BehaviorRules.riskWeights.knownBadComponent
                break
            }
        }
    }
    if ($evidence.Exists -eq $false) { $score += [int]$Script:BehaviorRules.riskWeights.missingTarget }
    if ($evidence.Signature -match 'NotSigned|HashMismatch|NotTrusted|UnknownError') { $score += [int]$Script:BehaviorRules.riskWeights.unsignedTarget }
    if ($evidence.Path -match '\\Users\\|\\AppData\\') { $score += [int]$Script:BehaviorRules.riskWeights.userWritablePath }
    if ($evidence.Path -match '\\ProgramData\\') { $score += [int]$Script:BehaviorRules.riskWeights.programDataPath }
    if ($evidence.Path -match '\\Temp\\|\\Windows\\Temp\\') { $score += [int]$Script:BehaviorRules.riskWeights.tempPath }
    if ($Title -match '[A-Fa-f0-9]{12,}|[a-z]{6,}\d{3,}') { $score += [int]$Script:BehaviorRules.riskWeights.randomLookingName }
    if ($trusted) {
        if ($text -match 'Microsoft Corporation') { $score += [int]$Script:BehaviorRules.riskWeights.microsoftPublisher }
        else { $score += [int]$Script:BehaviorRules.riskWeights.trustedPublisher }
    }

    if (!$ForceInclude -and !$IncludeTrusted -and !$vendor -and $score -lt [int]$Script:BehaviorRules.riskBands.low) {
        return $null
    }
    if (!$IncludeTrusted -and $trusted -and !$vendor -and $score -lt [int]$Script:BehaviorRules.riskBands.medium) {
        return $null
    }

    $action = Get-ActionName $SourceType
    $actionType = Get-ObjectPropertyValue $ActionData 'ActionType'
    if (![string]::IsNullOrWhiteSpace($actionType)) { $action = $actionType }
    if ($SourceType -eq 'InstalledApplication') { $action = 'InvokeUninstaller' }
    if ($trusted -and !$vendor) { $action = 'ReportOnly' }
    if ($SourceType -eq 'InstalledApplication' -and $score -lt [int]$Script:BehaviorRules.riskBands.high) {
        $score = [int]$Script:BehaviorRules.riskBands.high
    }
    $risk = Get-RiskName $score
    $requiresAdmin = $Location -match '^HKLM|^HKEY_LOCAL_MACHINE' -or $RegistryPath -match '^HKLM|^HKEY_LOCAL_MACHINE' -or $SourceType -in @('Service', 'ScheduledTask', 'InstalledApplication')
    $selected = $false
    $vendorName = if ($vendor) { $vendor.name } elseif ($trusted) { '可信/系统' } else { '未知第三方' }
    $snark = if ($vendor) { $vendor.snark } elseif ($trusted) { '系统项，别把锅随便甩给它。' } else { '没报家门，先拎出来看看。' }

    [pscustomobject]@{
        Id = 0
        Selected = $selected
        Risk = $risk
        Score = $score
        Vendor = $vendorName
        SourceType = $SourceType
        Title = $Title
        Location = $Location
        Command = $Command
        TargetPath = if ($evidence.Path) { $evidence.Path } else { $TargetPath }
        Company = $evidence.Company
        Product = $evidence.Product
        Signature = $evidence.Signature
        RecommendedAction = $action
        RequiresAdmin = $requiresAdmin
        Snark = $snark
        RegistryPath = $RegistryPath
        RegistryValueName = $RegistryValueName
        ActionData = $ActionData
        Extra = $Extra
        Status = '待处理'
    }
}

function Add-Finding {
    param(
        [System.Collections.Generic.List[object]]$List,
        [object]$Finding
    )
    if ($null -eq $Finding) { return }
    $key = Join-Text @($Finding.SourceType, $Finding.Location, $Finding.RegistryPath, $Finding.RegistryValueName, $Finding.Title)
    foreach ($item in $List) {
        $existing = Join-Text @($item.SourceType, $item.Location, $item.RegistryPath, $item.RegistryValueName, $item.Title)
        if ($existing -eq $key) { return }
    }
    $Finding.Id = $List.Count + 1
    $List.Add($Finding)
}

function Scan-ContextMenus {
    param([System.Collections.Generic.List[object]]$Findings)
    foreach ($root in $Script:Locations.contextMenuRoots) {
        if (!(Test-Path -LiteralPath $root)) { continue }
        foreach ($key in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            $path = $key.PSPath
            $display = Get-RegDefaultValue $path
            $props = Get-ItemProperty -LiteralPath $path -ErrorAction SilentlyContinue
            if (!$display -and $props -and ($props.PSObject.Properties.Name -contains 'MUIVerb')) { $display = $props.MUIVerb }
            $commandPath = Join-Path $path 'command'
            $command = Get-RegDefaultValue $commandPath
            $clsid = $display
            $server = $null
            $clsidName = $null
            if ($props -and ($props.PSObject.Properties.Name -contains 'ExplorerCommandHandler')) { $clsid = $props.ExplorerCommandHandler }
            $clsidInfo = Resolve-Clsid $clsid
            if ($clsidInfo) {
                $server = $clsidInfo.Server
                $clsidName = $clsidInfo.Name
            }
            $title = Join-Text @($key.PSChildName, $display, $clsidName)
            $native = ConvertTo-NativeRegistryPath $path
            $actionData = [pscustomobject]@{
                ActionType = 'DeleteRegistryKey'
                RegistryPath = $native
            }
            Add-Finding $Findings (New-Finding -SourceType 'ContextMenu' -Title $title -Location $native -Command $command -RegistryPath $native -TargetPath $server -ActionData $actionData -Extra $clsid)
        }
    }
}

function Scan-StartupRegistry {
    param([System.Collections.Generic.List[object]]$Findings)
    foreach ($root in $Script:Locations.startupRegistryRoots) {
        if (!(Test-Path -LiteralPath $root)) { continue }
        $item = Get-Item -LiteralPath $root
        foreach ($name in $item.GetValueNames()) {
            if ($name -eq '') { continue }
            $value = $item.GetValue($name)
            $native = ConvertTo-NativeRegistryPath $root
            $actionData = [pscustomobject]@{
                ActionType = 'DeleteRegistryValue'
                RegistryPath = $native
                ValueName = $name
            }
            Add-Finding $Findings (New-Finding -SourceType 'StartupRegistry' -Title $name -Location "$native::$name" -Command $value -RegistryPath $native -RegistryValueName $name -ActionData $actionData -Extra '开机启动项')
        }
    }
}

function Scan-StartupFolders {
    param([System.Collections.Generic.List[object]]$Findings)
    foreach ($folderRaw in $Script:Locations.startupFolders) {
        $folder = [Environment]::ExpandEnvironmentVariables($folderRaw)
        if (!(Test-Path -LiteralPath $folder)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $folder -Force -ErrorAction SilentlyContinue) {
            $target = $file.FullName
            if ($file.Extension -ieq '.lnk') {
                try {
                    $shell = New-Object -ComObject WScript.Shell
                    $target = $shell.CreateShortcut($file.FullName).TargetPath
                } catch {}
            }
            $actionData = [pscustomobject]@{
                ActionType = 'MoveFileToBackup'
                Path = $file.FullName
            }
            Add-Finding $Findings (New-Finding -SourceType 'StartupFolder' -Title $file.Name -Location $file.FullName -TargetPath $target -ActionData $actionData -Extra '启动文件夹')
        }
    }
}

function Scan-ScheduledTasks {
    param([System.Collections.Generic.List[object]]$Findings)
    try {
        $tasks = Get-ScheduledTask -ErrorAction SilentlyContinue
    } catch {
        return
    }
    foreach ($task in $tasks) {
        $actions = @($task.Actions | ForEach-Object {
            $execute = if ($_.PSObject.Properties.Name -contains 'Execute') { $_.Execute } else { $null }
            $arguments = if ($_.PSObject.Properties.Name -contains 'Arguments') { $_.Arguments } else { $null }
            $workingDirectory = if ($_.PSObject.Properties.Name -contains 'WorkingDirectory') { $_.WorkingDirectory } else { $null }
            $className = $_.CimClass.CimClassName
            Join-Text @($className, $execute, $arguments, $workingDirectory)
        })
        $text = Join-Text @($task.TaskPath, $task.TaskName, $actions)
        $vendor = Resolve-Vendor $text
        if (!$vendor -and !$IncludeTrusted) { continue }
        $actionData = [pscustomobject]@{
            ActionType = 'DisableScheduledTask'
            TaskName = $task.TaskName
            TaskPath = $task.TaskPath
        }
        Add-Finding $Findings (New-Finding -SourceType 'ScheduledTask' -Title $task.TaskName -Location ($task.TaskPath + $task.TaskName) -Command ($actions -join ' | ') -ActionData $actionData -Extra '计划任务' -ForceInclude:([bool]$vendor))
    }
}

function Scan-Services {
    param([System.Collections.Generic.List[object]]$Findings)
    try {
        $services = Get-CimInstance Win32_Service -ErrorAction SilentlyContinue
    } catch {
        return
    }
    foreach ($svc in $services) {
        $text = Join-Text @($svc.Name, $svc.DisplayName, $svc.PathName, $svc.Description)
        $vendor = Resolve-Vendor $text
        if (!$vendor -and !$IncludeTrusted) { continue }
        $actionData = [pscustomobject]@{
            ActionType = 'DisableService'
            ServiceName = $svc.Name
        }
        Add-Finding $Findings (New-Finding -SourceType 'Service' -Title $svc.DisplayName -Location $svc.Name -Command $svc.PathName -ActionData $actionData -Extra ("服务：{0}" -f $svc.StartMode) -ForceInclude:([bool]$vendor))
    }
}

function Scan-BrowserExtensions {
    param([System.Collections.Generic.List[object]]$Findings)
    foreach ($root in $Script:Locations.browserRegistryRoots) {
        if (!(Test-Path -LiteralPath $root)) { continue }
        $item = Get-Item -LiteralPath $root -ErrorAction SilentlyContinue
        foreach ($name in $item.GetValueNames()) {
            $value = $item.GetValue($name)
            $native = ConvertTo-NativeRegistryPath $root
            $actionData = [pscustomobject]@{
                ActionType = 'DeleteRegistryValue'
                RegistryPath = $native
                ValueName = $name
            }
            Add-Finding $Findings (New-Finding -SourceType 'BrowserExtension' -Title $name -Location "$native::$name" -Command $value -RegistryPath $native -RegistryValueName $name -ActionData $actionData -Extra '浏览器扩展策略/宿主')
        }
        foreach ($child in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            $native = ConvertTo-NativeRegistryPath $child.PSPath
            $props = Get-ItemProperty -LiteralPath $child.PSPath -ErrorAction SilentlyContinue | Out-String
            $actionData = [pscustomobject]@{
                ActionType = 'DeleteRegistryKey'
                RegistryPath = $native
            }
            Add-Finding $Findings (New-Finding -SourceType 'BrowserExtension' -Title $child.PSChildName -Location $native -Command $props -RegistryPath $native -ActionData $actionData -Extra '浏览器外部扩展/Native Host')
        }
    }
}

function Scan-FileAssociations {
    param([System.Collections.Generic.List[object]]$Findings)
    foreach ($ext in $Script:Locations.fileAssociationExtensions) {
        foreach ($base in @('HKCU:\Software\Classes', 'HKLM:\Software\Classes')) {
            $extPath = Join-Path $base $ext
            if (!(Test-Path -LiteralPath $extPath)) { continue }
            $default = Get-RegDefaultValue $extPath
            if ($default) {
                $classPath = Join-Path $base $default
                if (Test-Path -LiteralPath $classPath) {
                    $native = ConvertTo-NativeRegistryPath $classPath
                    $cmd = Get-RegDefaultValue (Join-Path $classPath 'shell\open\command')
                    $actionData = [pscustomobject]@{
                        ActionType = 'DeleteRegistryKey'
                        RegistryPath = $native
                    }
                    Add-Finding $Findings (New-Finding -SourceType 'FileAssociation' -Title "$ext -> $default" -Location $native -Command $cmd -RegistryPath $native -ActionData $actionData -Extra '默认文件类型类')
                }
            }
            foreach ($sub in @('OpenWithList', 'OpenWithProgids')) {
                $subPath = Join-Path $extPath $sub
                if (!(Test-Path -LiteralPath $subPath)) { continue }
                $subItem = Get-Item -LiteralPath $subPath
                foreach ($name in $subItem.GetValueNames()) {
                    if ($name -eq 'MRUList') { continue }
                    $native = ConvertTo-NativeRegistryPath $subPath
                    $actionData = [pscustomobject]@{
                        ActionType = 'DeleteRegistryValue'
                        RegistryPath = $native
                        ValueName = $name
                    }
                    Add-Finding $Findings (New-Finding -SourceType 'FileAssociation' -Title "$ext 打开方式：$name" -Location "$native::$name" -RegistryPath $native -RegistryValueName $name -ActionData $actionData -Extra '打开方式残留')
                }
                foreach ($child in Get-ChildItem -LiteralPath $subPath -ErrorAction SilentlyContinue) {
                    $native = ConvertTo-NativeRegistryPath $child.PSPath
                    $actionData = [pscustomobject]@{
                        ActionType = 'DeleteRegistryKey'
                        RegistryPath = $native
                    }
                    Add-Finding $Findings (New-Finding -SourceType 'FileAssociation' -Title "$ext 打开方式：$($child.PSChildName)" -Location $native -RegistryPath $native -ActionData $actionData -Extra '打开方式子键')
                }
            }
        }
    }
}

function Scan-InstalledApplications {
    param([System.Collections.Generic.List[object]]$Findings)
    foreach ($root in $Script:Locations.uninstallRoots) {
        if (!(Test-Path -LiteralPath $root)) { continue }
        foreach ($key in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            $props = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
            $name = Get-ObjectPropertyValue $props 'DisplayName'
            if (!$name) { continue }
            $publisher = Get-ObjectPropertyValue $props 'Publisher'
            $installLocation = Get-ObjectPropertyValue $props 'InstallLocation'
            $uninstallString = Get-ObjectPropertyValue $props 'UninstallString'
            $quietUninstallString = Get-ObjectPropertyValue $props 'QuietUninstallString'
            $text = Join-Text @($name, $publisher, $installLocation, $uninstallString, $quietUninstallString)
            $vendor = Resolve-Vendor $text
            if (!$vendor -and !$IncludeTrusted) { continue }
            $native = ConvertTo-NativeRegistryPath $key.PSPath
            $sourceType = 'InstalledApplication'
            if ($uninstallString -and !(Resolve-ExecutablePath $uninstallString)) { $sourceType = 'UninstallResidue' }
            $actionData = [pscustomobject]@{
                ActionType = if ($sourceType -eq 'InstalledApplication') { 'InvokeUninstaller' } else { 'ReportOnly' }
                RegistryPath = $native
                DisplayName = $name
                UninstallString = $uninstallString
                QuietUninstallString = $quietUninstallString
                InstallLocation = $installLocation
            }
            Add-Finding $Findings (New-Finding -SourceType $sourceType -Title $name -Location $native -Command (Join-Text @($quietUninstallString, $uninstallString)) -RegistryPath $native -ActionData $actionData -Extra '已安装主程序/卸载项' -ForceInclude:([bool]$vendor))
        }
    }
}

function Scan-AppxContextMenus {
    param([System.Collections.Generic.List[object]]$Findings)
    try {
        $packages = Get-AppxPackage -ErrorAction SilentlyContinue
    } catch {
        return
    }
    foreach ($pkg in $packages) {
        $manifest = Join-Path $pkg.InstallLocation 'AppxManifest.xml'
        if (!(Test-Path -LiteralPath $manifest)) { continue }
        $lines = Select-String -LiteralPath $manifest -Pattern 'fileExplorerContextMenus|CreateWithDesigner|com.microsoft.windows.ai.actions|desktop5:Verb|ShellExtension' -CaseSensitive:$false -ErrorAction SilentlyContinue
        if (!$lines) { continue }
        $text = Join-Text @($pkg.Name, $pkg.PackageFullName, $pkg.Publisher, ($lines.Line -join ' '))
        $vendor = Resolve-Vendor $text
        $trusted = $text -match 'Microsoft Corporation'
        if (!$vendor -and !$trusted -and !$IncludeTrusted) { continue }
        $actionData = [pscustomobject]@{
            ActionType = 'ReportOnly'
            PackageFullName = $pkg.PackageFullName
        }
        Add-Finding $Findings (New-Finding -SourceType 'AppxContextMenu' -Title $pkg.Name -Location $pkg.PackageFullName -Command $pkg.InstallLocation -ActionData $actionData -Extra (($lines.Line | Select-Object -First 8) -join ' ') -ForceInclude:($IncludeTrusted -or [bool]$vendor))
    }
}

function Invoke-FullScan {
    $findings = [System.Collections.Generic.List[object]]::new()
    Scan-ContextMenus $findings
    Scan-StartupRegistry $findings
    Scan-StartupFolders $findings
    Scan-ScheduledTasks $findings
    Scan-Services $findings
    Scan-BrowserExtensions $findings
    Scan-FileAssociations $findings
    if ($IncludeInstalledApps) {
        Scan-InstalledApplications $findings
    }
    Scan-AppxContextMenus $findings

    $sorted = @($findings | Sort-Object @{ Expression = {
        switch ($_.Risk) {
            '高' { 0 }
            '中' { 1 }
            default { 2 }
        }
    }}, Vendor, SourceType, Title)
    $i = 1
    foreach ($finding in $sorted) {
        $finding.Id = $i
        $i++
    }
    $sorted
}

function Write-ScanReports {
    param([Parameter(Mandatory)][object[]]$Findings)
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $jsonPath = Join-Path $Script:ReportsDir "scan-$stamp.json"
    $mdPath = Join-Path $Script:ReportsDir "scan-$stamp.md"
    $Findings | ConvertTo-Json -Depth 10 | Out-File -LiteralPath $jsonPath -Encoding UTF8

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# 流氓软件克星扫描报告')
    $lines.Add('')
    $lines.Add(("时间：{0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
    $lines.Add(("发现项：{0}" -f $Findings.Count))
    $lines.Add('')
    $lines.Add('| 编号 | 风险 | 厂商 | 类型 | 名称 | 建议 | 位置 |')
    $lines.Add('|---:|---|---|---|---|---|---|')
    foreach ($f in $Findings) {
        $line = '| {0} | {1} | {2} | {3} | {4} | {5} | {6} |' -f $f.Id, $f.Risk, ($f.Vendor -replace '\|','/'), $f.SourceType, (($f.Title -replace '\|','/' -replace "`r?`n",' ') ), $f.RecommendedAction, (($f.Location -replace '\|','/' -replace "`r?`n",' '))
        $lines.Add($line)
    }
    $lines | Out-File -LiteralPath $mdPath -Encoding UTF8
    $Script:LastReportPath = $mdPath
    [pscustomobject]@{ Json = $jsonPath; Markdown = $mdPath }
}

function New-BackupSet {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $dir = Join-Path $Script:BackupsDir $stamp
    foreach ($sub in @('registry', 'tasks', 'services', 'browser', 'files')) {
        New-Item -ItemType Directory -Path (Join-Path $dir $sub) -Force | Out-Null
    }
    [pscustomobject]@{
        Path = $dir
        Manifest = [System.Collections.Generic.List[object]]::new()
    }
}

function Backup-Registry {
    param([object]$BackupSet, [string]$NativeKey)
    if ([string]::IsNullOrWhiteSpace($NativeKey)) { return $null }
    $safe = ($NativeKey -replace '[\\/:*?"<>|{} ]', '_')
    $dest = Join-Path (Join-Path $BackupSet.Path 'registry') "$safe.reg"
    & reg.exe export $NativeKey $dest /y *> $null
    if ($LASTEXITCODE -eq 0) { return $dest }
    return $null
}

function Get-UninstallInvocation {
    param([object]$ActionData)
    $raw = if ($ActionData.QuietUninstallString) { $ActionData.QuietUninstallString } else { $ActionData.UninstallString }
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }

    $exe = Resolve-ExecutablePath $raw
    if (!$exe) { return $null }
    $args = ''
    if ($raw -match '^\s*"[^"]+"\s*(.*)$') { $args = $Matches[1] }
    elseif ($raw -match '^\s*\S+\s+(.*)$') { $args = $Matches[1] }

    if ($ActionData.QuietUninstallString) {
        return [pscustomobject]@{ FilePath = $exe; Arguments = $args }
    }
    if ($exe -match 'msiexec\.exe$' -or $raw -match 'MsiExec') {
        if ($raw -match '(\{[0-9A-Fa-f-]{36}\})') {
            return [pscustomobject]@{ FilePath = 'msiexec.exe'; Arguments = "/x $($Matches[1]) /qn /norestart" }
        }
    }
    if ((Split-Path -Leaf $exe) -match 'unins|uninst|uninstall') {
        return [pscustomobject]@{ FilePath = $exe; Arguments = '/S' }
    }
    return $null
}

function Get-ActionDataValue {
    param(
        [object]$ActionData,
        [Parameter(Mandatory)][string]$Name
    )
    if ($null -eq $ActionData) { return $null }
    if ($ActionData.PSObject.Properties.Name -contains $Name) { return $ActionData.$Name }
    return $null
}

function Set-ObjectPropertySafe {
    param(
        [Parameter(Mandatory)][object]$Object,
        [Parameter(Mandatory)][string]$Name,
        [object]$Value
    )
    if ($Object.PSObject.Properties.Name -contains $Name) {
        $Object.$Name = $Value
    } else {
        $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value -Force
    }
}

function Test-RegistryKeyExists {
    param([string]$NativeKey)
    if ([string]::IsNullOrWhiteSpace($NativeKey)) { return $false }
    $providerPath = ConvertTo-ProviderRegistryPath $NativeKey
    try {
        return [bool](Test-Path -LiteralPath $providerPath -ErrorAction Stop)
    } catch {
        return $false
    }
}

function Test-RegistryValueExists {
    param(
        [string]$NativeKey,
        [string]$ValueName
    )
    if ([string]::IsNullOrWhiteSpace($NativeKey)) { return $false }
    $providerPath = ConvertTo-ProviderRegistryPath $NativeKey
    try {
        $item = Get-Item -LiteralPath $providerPath -ErrorAction Stop
        if ([string]::IsNullOrEmpty($ValueName)) {
            $sentinel = '__ROGUE_CLEANER_VALUE_MISSING__'
            return $item.GetValue('', $sentinel) -ne $sentinel
        }
        return @($item.GetValueNames()) -contains $ValueName
    } catch {
        return $false
    }
}

function Test-ActionApplied {
    param([object]$ActionData)
    $actionType = Get-ActionDataValue $ActionData 'ActionType'
    switch ($actionType) {
        'DeleteRegistryKey' {
            return -not (Test-RegistryKeyExists (Get-ActionDataValue $ActionData 'RegistryPath'))
        }
        'DeleteRegistryValue' {
            return -not (Test-RegistryValueExists (Get-ActionDataValue $ActionData 'RegistryPath') (Get-ActionDataValue $ActionData 'ValueName'))
        }
        'DisableScheduledTask' {
            try {
                $task = Get-ScheduledTask -TaskName (Get-ActionDataValue $ActionData 'TaskName') -TaskPath (Get-ActionDataValue $ActionData 'TaskPath') -ErrorAction Stop
                if ($task.Settings -and ($task.Settings.PSObject.Properties.Name -contains 'Enabled')) {
                    return -not [bool]$task.Settings.Enabled
                }
                return $true
            } catch {
                return $false
            }
        }
        'DisableService' {
            try {
                $serviceName = Get-ActionDataValue $ActionData 'ServiceName'
                $svc = Get-CimInstance Win32_Service -Filter ("Name='{0}'" -f ($serviceName -replace "'", "''")) -ErrorAction Stop
                return $svc.StartMode -eq 'Disabled'
            } catch {
                return $false
            }
        }
        'MoveFileToBackup' {
            $src = [Environment]::ExpandEnvironmentVariables((Get-ActionDataValue $ActionData 'Path'))
            return [string]::IsNullOrWhiteSpace($src) -or -not (Test-Path -LiteralPath $src)
        }
        'InvokeUninstaller' {
            return $true
        }
        'ReportOnly' {
            return $true
        }
        default {
            return $false
        }
    }
}

function Invoke-Cleanup {
    param([Parameter(Mandatory)][object[]]$Findings)
    $selected = @($Findings | Where-Object { $_.Selected })
    if (!$selected) { throw '没有选中任何项目。' }

    $backupSet = New-BackupSet
    $results = [System.Collections.Generic.List[object]]::new()

    foreach ($finding in $selected) {
        $status = 'Skipped'
        $message = ''
        $backupRef = $null
        $data = $finding.ActionData
        try {
            $actionType = Get-ActionDataValue $data 'ActionType'
            if ([string]::IsNullOrWhiteSpace($actionType)) {
                throw '缺少清理动作数据，无法确认要删除哪里。'
            }

            switch ($actionType) {
                'DeleteRegistryKey' {
                    $backupRef = Backup-Registry $backupSet $data.RegistryPath
                    & reg.exe delete $data.RegistryPath /f *> $null
                    if ($LASTEXITCODE -eq 0) { $status = 'Done'; $message = '注册表键已删除。' }
                    else { $status = 'Failed'; $message = '删除注册表键失败。' }
                }
                'DeleteRegistryValue' {
                    $backupRef = Backup-Registry $backupSet $data.RegistryPath
                    if ($data.ValueName) {
                        & reg.exe delete $data.RegistryPath /v $data.ValueName /f *> $null
                    } else {
                        & reg.exe delete $data.RegistryPath /ve /f *> $null
                    }
                    if ($LASTEXITCODE -eq 0) { $status = 'Done'; $message = '注册表值已删除。' }
                    else { $status = 'Failed'; $message = '删除注册表值失败。' }
                }
                'DisableScheduledTask' {
                    $taskFile = Join-Path (Join-Path $backupSet.Path 'tasks') (($data.TaskPath + $data.TaskName) -replace '[\\/:*?"<>|{} ]', '_')
                    $taskFile += '.xml'
                    try { Export-ScheduledTask -TaskName $data.TaskName -TaskPath $data.TaskPath | Out-File -LiteralPath $taskFile -Encoding UTF8; $backupRef = $taskFile } catch {}
                    Disable-ScheduledTask -TaskName $data.TaskName -TaskPath $data.TaskPath | Out-Null
                    $status = 'Done'
                    $message = '计划任务已禁用。'
                }
                'DisableService' {
                    $svc = Get-CimInstance Win32_Service -Filter ("Name='{0}'" -f ($data.ServiceName -replace "'", "''"))
                    $svcFile = Join-Path (Join-Path $backupSet.Path 'services') ($data.ServiceName + '.json')
                    $svc | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $svcFile -Encoding UTF8
                    $backupRef = $svcFile
                    Set-Service -Name $data.ServiceName -StartupType Disabled
                    $status = 'Done'
                    $message = '服务已禁用。'
                }
                'MoveFileToBackup' {
                    $src = [Environment]::ExpandEnvironmentVariables($data.Path)
                    if (Test-Path -LiteralPath $src) {
                        $dest = Join-Path (Join-Path $backupSet.Path 'files') (Split-Path -Leaf $src)
                        Move-Item -LiteralPath $src -Destination $dest -Force
                        $backupRef = $dest
                        $status = 'Done'
                        $message = '文件已移动到备份。'
                    } else {
                        $status = 'Done'
                        $message = '文件已经不存在。'
                    }
                }
                'InvokeUninstaller' {
                    $backupRef = Backup-Registry $backupSet $data.RegistryPath
                    $invocation = Get-UninstallInvocation $data
                    if (!$invocation) {
                        $status = 'Failed'
                        $message = '没有可确认的静默卸载命令，已跳过，避免弹窗。'
                    } else {
                        $p = Start-Process -FilePath $invocation.FilePath -ArgumentList $invocation.Arguments -WindowStyle Hidden -PassThru
                        if (!$p.WaitForExit(180000)) {
                            try { $p.Kill() } catch {}
                            $status = 'Failed'
                            $message = '卸载器超时，已尝试结束。'
                        } elseif ($p.ExitCode -eq 0) {
                            $status = 'Done'
                            $message = '静默卸载已完成。'
                        } else {
                            $status = 'Failed'
                            $message = "卸载器退出码：$($p.ExitCode)"
                        }
                    }
                }
                default {
                    $status = 'Skipped'
                    $message = '只报告，不执行清理。'
                }
            }
            if ($status -eq 'Done' -and -not (Test-ActionApplied $data)) {
                $status = 'Failed'
                $message = ($message + ' 复核失败：目标仍然存在。').Trim()
            }
        } catch {
            $status = 'Failed'
            $message = $_.Exception.Message
        }

        Set-ObjectPropertySafe -Object $finding -Name 'Status' -Value $status
        $entry = [pscustomobject]@{
            Id = $finding.Id
            Title = $finding.Title
            Vendor = $finding.Vendor
            SourceType = $finding.SourceType
            Action = $finding.RecommendedAction
            ActionData = $finding.ActionData
            Status = $status
            Message = $message
            Backup = $backupRef
            Location = $finding.Location
        }
        $backupSet.Manifest.Add($entry)
        $results.Add($entry)
    }

    $manifestPath = Join-Path $backupSet.Path 'manifest.json'
    $backupSet.Manifest | ConvertTo-Json -Depth 10 | Out-File -LiteralPath $manifestPath -Encoding UTF8
    try {
        Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 800
        Start-Process explorer.exe
    } catch {}

    $reportPath = Join-Path $Script:ReportsDir ("cleanup-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $results | ConvertTo-Json -Depth 10 | Out-File -LiteralPath $reportPath -Encoding UTF8
    [pscustomobject]@{
        Backup = $backupSet.Path
        Manifest = $manifestPath
        Report = $reportPath
        Results = @($results)
    }
}

function Restore-BackupSet {
    param([Parameter(Mandatory)][string]$BackupPath)
    if (!(Test-Path -LiteralPath $BackupPath)) { throw "备份目录不存在：$BackupPath" }

    $manifestPath = Join-Path $BackupPath 'manifest.json'
    $manifest = @()
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = @(Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json)
    }

    foreach ($reg in Get-ChildItem -LiteralPath (Join-Path $BackupPath 'registry') -Filter *.reg -ErrorAction SilentlyContinue) {
        & reg.exe import $reg.FullName *> $null
    }

    foreach ($entry in $manifest) {
        $data = $entry.ActionData
        if (!$data) { continue }
        try {
            switch ($data.ActionType) {
                'MoveFileToBackup' {
                    if ($entry.Backup -and (Test-Path -LiteralPath $entry.Backup) -and $data.Path) {
                        $parent = Split-Path -Parent $data.Path
                        if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
                        if (!(Test-Path -LiteralPath $data.Path)) {
                            Move-Item -LiteralPath $entry.Backup -Destination $data.Path -Force
                        }
                    }
                }
                'DisableScheduledTask' {
                    if ($entry.Backup -and (Test-Path -LiteralPath $entry.Backup)) {
                        $xml = Get-Content -LiteralPath $entry.Backup -Raw -Encoding UTF8
                        Register-ScheduledTask -TaskName $data.TaskName -TaskPath $data.TaskPath -Xml $xml -Force | Out-Null
                    }
                }
                'DisableService' {
                    if ($entry.Backup -and (Test-Path -LiteralPath $entry.Backup)) {
                        $svc = Get-Content -LiteralPath $entry.Backup -Raw -Encoding UTF8 | ConvertFrom-Json
                        $startupType = switch ($svc.StartMode) {
                            'Auto' { 'Automatic' }
                            'Manual' { 'Manual' }
                            'Disabled' { 'Disabled' }
                            default { 'Manual' }
                        }
                        Set-Service -Name $data.ServiceName -StartupType $startupType
                    }
                }
            }
        } catch {
            Write-Warning ("恢复项目失败：{0} :: {1}" -f $entry.Title, $_.Exception.Message)
        }
    }
}

function Start-Gui {
    if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
        if ($env:ROGUE_CLEANER_EXE -and (Test-Path -LiteralPath $env:ROGUE_CLEANER_EXE)) {
            Start-Process -FilePath $env:ROGUE_CLEANER_EXE
        } else {
            $pwsh = (Get-Process -Id $PID).Path
            Start-Process -FilePath $pwsh -ArgumentList @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
        }
        return
    }

    Add-Type -AssemblyName PresentationFramework
    Add-Type -AssemblyName PresentationCore
    Add-Type -AssemblyName WindowsBase

    [xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="流氓软件克星" Width="1240" Height="780" MinWidth="1080" MinHeight="680"
        WindowStartupLocation="CenterScreen" Background="#0f172a">
  <Window.Resources>
    <Style TargetType="Button">
      <Setter Property="Margin" Value="6,0,0,0"/>
      <Setter Property="Padding" Value="16,9"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Foreground" Value="#f8fafc"/>
      <Setter Property="Background" Value="#2563eb"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Cursor" Value="Hand"/>
    </Style>
    <Style TargetType="TextBox">
      <Setter Property="Margin" Value="0,0,8,0"/>
      <Setter Property="Padding" Value="10,7"/>
      <Setter Property="BorderBrush" Value="#334155"/>
      <Setter Property="Background" Value="#111827"/>
      <Setter Property="Foreground" Value="#e5e7eb"/>
    </Style>
    <Style TargetType="ComboBox">
      <Setter Property="Margin" Value="0,0,8,0"/>
      <Setter Property="Padding" Value="8,6"/>
      <Setter Property="Background" Value="#111827"/>
      <Setter Property="Foreground" Value="#e5e7eb"/>
    </Style>
  </Window.Resources>
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>
    <Border Grid.Row="0" Padding="24" Background="#111827">
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*"/>
          <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <StackPanel>
          <TextBlock Text="流氓软件克星" FontSize="32" FontWeight="Bold" Foreground="#f8fafc"/>
          <TextBlock Text="把偷偷摸摸塞进右键、自启、计划任务和浏览器里的东西拎出来晒太阳。" Margin="0,8,0,0" Foreground="#cbd5e1" FontSize="15"/>
          <TextBlock x:Name="AdminText" Margin="0,10,0,0" Foreground="#fbbf24" FontSize="13"/>
        </StackPanel>
        <Border Grid.Column="1" Background="#1e293b" CornerRadius="10" Padding="18" MinWidth="330">
          <StackPanel>
            <TextBlock Text="今日吐槽" Foreground="#93c5fd" FontWeight="Bold"/>
            <TextBlock x:Name="SnarkText" Text="还没扫描。先别急着开火，先把名单拉出来。" TextWrapping="Wrap" Margin="0,8,0,0" Foreground="#e5e7eb"/>
          </StackPanel>
        </Border>
      </Grid>
    </Border>
    <Border Grid.Row="1" Padding="18,14" Background="#172033">
      <DockPanel LastChildFill="True">
        <StackPanel Orientation="Horizontal" DockPanel.Dock="Right">
          <Button x:Name="ScanButton" Content="开始揪出来"/>
          <Button x:Name="ApplyButton" Content="清理选中项" Background="#dc2626"/>
          <Button x:Name="ExportButton" Content="导出报告" Background="#475569"/>
          <Button x:Name="AdminButton" Content="管理员重开" Background="#7c3aed"/>
        </StackPanel>
        <StackPanel Orientation="Horizontal">
          <TextBox x:Name="SearchBox" Width="260" ToolTip="按厂商、标题、路径过滤"/>
          <ComboBox x:Name="RiskFilter" Width="120">
            <ComboBoxItem Content="全部风险" IsSelected="True"/>
            <ComboBoxItem Content="高"/>
            <ComboBoxItem Content="中"/>
            <ComboBoxItem Content="低"/>
          </ComboBox>
          <ComboBox x:Name="TypeFilter" Width="170">
            <ComboBoxItem Content="全部类型" IsSelected="True"/>
            <ComboBoxItem Content="ContextMenu"/>
            <ComboBoxItem Content="StartupRegistry"/>
            <ComboBoxItem Content="StartupFolder"/>
            <ComboBoxItem Content="ScheduledTask"/>
            <ComboBoxItem Content="Service"/>
            <ComboBoxItem Content="BrowserExtension"/>
            <ComboBoxItem Content="FileAssociation"/>
            <ComboBoxItem Content="InstalledApplication"/>
            <ComboBoxItem Content="AppxContextMenu"/>
          </ComboBox>
        </StackPanel>
      </DockPanel>
    </Border>
    <Grid Grid.Row="2" Margin="18">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="260"/>
        <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>
      <Border Grid.Column="0" Background="#111827" CornerRadius="12" Padding="16" Margin="0,0,14,0">
        <StackPanel>
          <TextBlock Text="扫描摘要" Foreground="#f8fafc" FontSize="18" FontWeight="Bold"/>
          <TextBlock x:Name="SummaryText" Margin="0,14,0,0" Foreground="#cbd5e1" TextWrapping="Wrap" Text="还没有扫描结果。"/>
          <Separator Margin="0,18,0,18" Background="#334155"/>
          <TextBlock Text="风险说明" Foreground="#f8fafc" FontWeight="Bold"/>
          <TextBlock Text="高：服务、主程序卸载、强制插件。别一脚油门。" Margin="0,10,0,0" Foreground="#fecaca" TextWrapping="Wrap"/>
          <TextBlock Text="中：开机启动、计划任务、浏览器宿主。后台蹭饭重点户。" Margin="0,8,0,0" Foreground="#fde68a" TextWrapping="Wrap"/>
          <TextBlock Text="低：右键牛皮癣、打开方式残留。一般可以清。" Margin="0,8,0,0" Foreground="#bbf7d0" TextWrapping="Wrap"/>
        </StackPanel>
      </Border>
      <Border Grid.Column="1" Background="#111827" CornerRadius="12" Padding="8">
        <DataGrid x:Name="ResultsGrid" AutoGenerateColumns="False" CanUserAddRows="False"
                  HeadersVisibility="Column" GridLinesVisibility="Horizontal"
                  Background="#111827" Foreground="#e5e7eb" RowBackground="#111827"
                  AlternatingRowBackground="#182235" BorderThickness="0"
                  SelectionMode="Extended" IsReadOnly="False">
          <DataGrid.Columns>
            <DataGridCheckBoxColumn Header="选" Binding="{Binding Selected, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Width="45"/>
            <DataGridTextColumn Header="编号" Binding="{Binding Id}" Width="55" IsReadOnly="True"/>
            <DataGridTextColumn Header="风险" Binding="{Binding Risk}" Width="60" IsReadOnly="True"/>
            <DataGridTextColumn Header="厂商" Binding="{Binding Vendor}" Width="140" IsReadOnly="True"/>
            <DataGridTextColumn Header="类型" Binding="{Binding SourceType}" Width="145" IsReadOnly="True"/>
            <DataGridTextColumn Header="名称" Binding="{Binding Title}" Width="230" IsReadOnly="True"/>
            <DataGridTextColumn Header="建议" Binding="{Binding RecommendedAction}" Width="140" IsReadOnly="True"/>
            <DataGridTextColumn Header="位置" Binding="{Binding Location}" Width="420" IsReadOnly="True"/>
            <DataGridTextColumn Header="状态" Binding="{Binding Status}" Width="90" IsReadOnly="True"/>
          </DataGrid.Columns>
        </DataGrid>
      </Border>
    </Grid>
    <Border Grid.Row="3" Padding="18,10" Background="#0b1120">
      <TextBlock x:Name="StatusText" Foreground="#cbd5e1" Text="就绪。默认只扫描，不会偷偷清理。"/>
    </Border>
  </Grid>
</Window>
"@

    $reader = [System.Xml.XmlNodeReader]::new($xaml)
    $window = [Windows.Markup.XamlReader]::Load($reader)

    $scanButton = $window.FindName('ScanButton')
    $applyButton = $window.FindName('ApplyButton')
    $exportButton = $window.FindName('ExportButton')
    $adminButton = $window.FindName('AdminButton')
    $grid = $window.FindName('ResultsGrid')
    $statusText = $window.FindName('StatusText')
    $summaryText = $window.FindName('SummaryText')
    $snarkText = $window.FindName('SnarkText')
    $adminText = $window.FindName('AdminText')
    $searchBox = $window.FindName('SearchBox')
    $riskFilter = $window.FindName('RiskFilter')
    $typeFilter = $window.FindName('TypeFilter')

    $adminText.Text = if (Test-IsAdmin) { '当前是管理员权限：可以清 HKLM、服务和计划任务。' } else { '当前不是管理员权限：能扫描，清理 HKLM/服务/计划任务会失败或被跳过。' }

    function Update-GridView {
        $text = $searchBox.Text
        $risk = (($riskFilter.SelectedItem).Content)
        $type = (($typeFilter.SelectedItem).Content)
        $items = @($Script:CurrentFindings)
        if (![string]::IsNullOrWhiteSpace($text)) {
            $items = @($items | Where-Object { (Join-Text @($_.Vendor, $_.Title, $_.Location, $_.Command, $_.TargetPath, $_.Snark)) -match [regex]::Escape($text) })
        }
        if ($risk -and $risk -ne '全部风险') {
            $items = @($items | Where-Object { $_.Risk -eq $risk })
        }
        if ($type -and $type -ne '全部类型') {
            $items = @($items | Where-Object { $_.SourceType -eq $type })
        }
        $grid.ItemsSource = $items
    }

    function Update-Summary {
        $all = @($Script:CurrentFindings)
        $high = @($all | Where-Object Risk -eq '高').Count
        $medium = @($all | Where-Object Risk -eq '中').Count
        $low = @($all | Where-Object Risk -eq '低').Count
        $selected = @($all | Where-Object Selected).Count
        $groups = $all | Group-Object Vendor | Sort-Object Count -Descending | Select-Object -First 8
        $groupText = ($groups | ForEach-Object { "{0}：{1}" -f $_.Name, $_.Count }) -join "`n"
        $summaryText.Text = "总数：$($all.Count)`n高风险：$high`n中风险：$medium`n低风险：$low`n已勾选：$selected`n`n$groupText"
        if ($all.Count -gt 0) {
            $top = $all | Sort-Object Score -Descending | Select-Object -First 1
            $snarkText.Text = $top.Snark
        }
    }

    $scanButton.Add_Click({
        try {
            $statusText.Text = '扫描中：正在翻注册表、任务计划、服务和浏览器角落。'
            $window.Cursor = 'Wait'
            $Script:CurrentFindings = Invoke-FullScan
            Write-ScanReports -Findings $Script:CurrentFindings | Out-Null
            Update-GridView
            Update-Summary
            $statusText.Text = "扫描完成。报告：$Script:LastReportPath"
        } catch {
            [System.Windows.MessageBox]::Show($_.Exception.Message, '扫描失败', 'OK', 'Error') | Out-Null
            $statusText.Text = '扫描失败。'
        } finally {
            $window.Cursor = $null
        }
    })

    $applyButton.Add_Click({
        try {
            $selected = @($Script:CurrentFindings | Where-Object Selected)
            if (!$selected) {
                [System.Windows.MessageBox]::Show('没有勾选任何项目。先选目标，再开刀。', '没有选中项', 'OK', 'Information') | Out-Null
                return
            }
            $high = @($selected | Where-Object Risk -eq '高').Count
            $adminNeeded = @($selected | Where-Object RequiresAdmin).Count
            $msg = "将清理 $($selected.Count) 项。高风险 $high 项，需要管理员权限 $adminNeeded 项。`n`n清理前会自动备份，清理后会重启 Explorer 并复扫。继续？"
            $answer = [System.Windows.MessageBox]::Show($msg, '确认清理', 'YesNo', 'Warning')
            if ($answer -ne 'Yes') { return }
            if ($high -gt 0) {
                $answer2 = [System.Windows.MessageBox]::Show('你选了高风险项，包含服务或主程序卸载。确认不是手滑？', '高风险二次确认', 'YesNo', 'Warning')
                if ($answer2 -ne 'Yes') { return }
            }
            $statusText.Text = '清理中：先备份，再动手。'
            $window.Cursor = 'Wait'
            $result = Invoke-Cleanup -Findings $Script:CurrentFindings
            $Script:CurrentFindings = Invoke-FullScan
            Write-ScanReports -Findings $Script:CurrentFindings | Out-Null
            Update-GridView
            Update-Summary
            $statusText.Text = "清理完成。备份：$($result.Backup)"
            [System.Windows.MessageBox]::Show("清理完成。`n备份目录：$($result.Backup)`n报告：$($result.Report)", '完成', 'OK', 'Information') | Out-Null
        } catch {
            [System.Windows.MessageBox]::Show($_.Exception.Message, '清理失败', 'OK', 'Error') | Out-Null
            $statusText.Text = '清理失败。'
        } finally {
            $window.Cursor = $null
        }
    })

    $exportButton.Add_Click({
        try {
            if (!$Script:CurrentFindings -or $Script:CurrentFindings.Count -eq 0) {
                [System.Windows.MessageBox]::Show('还没有扫描结果。', '无法导出', 'OK', 'Information') | Out-Null
                return
            }
            $report = Write-ScanReports -Findings $Script:CurrentFindings
            $statusText.Text = "报告已导出：$($report.Markdown)"
            [System.Windows.MessageBox]::Show("报告已导出：`n$($report.Markdown)", '导出完成', 'OK', 'Information') | Out-Null
        } catch {
            [System.Windows.MessageBox]::Show($_.Exception.Message, '导出失败', 'OK', 'Error') | Out-Null
        }
    })

    $adminButton.Add_Click({
        if ($env:ROGUE_CLEANER_EXE -and (Test-Path -LiteralPath $env:ROGUE_CLEANER_EXE)) {
            Start-Process -FilePath $env:ROGUE_CLEANER_EXE -Verb RunAs
        } else {
            $pwsh = (Get-Process -Id $PID).Path
            Start-Process -FilePath $pwsh -Verb RunAs -ArgumentList @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
        }
        $window.Close()
    })

    $searchBox.Add_TextChanged({ Update-GridView })
    $riskFilter.Add_SelectionChanged({ Update-GridView })
    $typeFilter.Add_SelectionChanged({ Update-GridView })

    if ($Script:ValidateGuiOnly) {
        Write-Host 'GUI_VALID'
        return
    }

    $window.ShowDialog() | Out-Null
}

if ($Restore) {
    if (!$Backup) { throw '请用 -Backup 指定要恢复的备份目录。' }
    Restore-BackupSet -BackupPath $Backup
    return
}

if ($Apply) {
    if (!$Selection) { throw '请用 -Selection 指定选择文件。' }
    $items = Get-Content -LiteralPath $Selection -Raw -Encoding UTF8 | ConvertFrom-Json
    $cleanup = Invoke-Cleanup -Findings @($items)
    $cleanup | ConvertTo-Json -Depth 10
    $failed = @($cleanup.Results | Where-Object { $_.Status -eq 'Failed' })
    if ($failed.Count -gt 0) { exit 20 }
    return
}

if ($Scan -or $NoGui) {
    $items = Invoke-FullScan
    $report = Write-ScanReports -Findings $items
    $items | Select-Object Id, Risk, Vendor, SourceType, Title, RecommendedAction, Location | Format-Table -AutoSize
    Write-Host ""
    Write-Host "报告：$($report.Markdown)"
    return
}

if ($ValidateGui) {
    $Script:ValidateGuiOnly = $true
    Start-Gui
    return
}

Start-Gui
