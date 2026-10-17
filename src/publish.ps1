# 一键同步 / 发布脚本  Optimised Crafting System
#
# 作用：把「开发目录」里的源码与文档同步到「发布仓库」，重新打包 Release 附件，
#       顺手在本地做一次 git 提交。推送（git push）仍需要你在自己的终端里做——
#       因为 GitHub 授权要弹浏览器，我这个执行环境是非交互的。
#
# 用法（在 PowerShell 里）：
#   cd D:\maple_sword\workspace\BetterCraftingLogic
#   .\publish.ps1                                    # 构建 + 部署 + 同步 + 提交
#   .\publish.ps1 -Message "修复：xxx"                # 自定义提交说明
#   .\publish.ps1 -NoBuild -NoDeploy                  # 只同步文件与文档
#   .\publish.ps1 -Bump "1.0.1"                       # 同步后把版本号改成 1.0.1 并重建
#
# 设计说明：
#   * 开发目录（$dev）是唯一真相来源；发布仓库（$pub）永远是它的副本 + 几个仓库专用文件
#     （英文 README.md、LICENSE、.gitignore）。
#   * 中文 README 导出成 README.zh-CN.md —— 仓库根的 README.md 是英文版，**不会被覆盖**。
#   * 不删除 $pub 里多余的文件（只用 /E 覆盖与新增），确需删除请手动 git rm。
#   * 游戏正在运行时跳过部署，避免 DLL 被占用。

param(
    [string]$Message = "sync: 同步开发目录到发布仓库",
    [switch]$NoBuild,
    [switch]$NoDeploy,
    [string]$Bump = ""
)

$ErrorActionPreference = 'Stop'

$dev    = 'D:\maple_sword\workspace\BetterCraftingLogic'
$pub    = 'D:\maple_sword\workspace\publish\OptimisedCraftingSystem'
$zipDir = 'D:\maple_sword\workspace\publish'
$game   = 'D:\Steam\steamapps\common\Casualties Unknown Demo'
$git    = 'C:\Program Files\Git\cmd\git.exe'
$proj   = Join-Path $dev 'OptimisedCraftingSystem.csproj'
$dll    = Join-Path $dev 'bin\Release\netstandard2.1\OptimisedCraftingSystem.dll'
$utf8   = New-Object System.Text.UTF8Encoding($false)

function Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }

# ——— 0) 可选：改版本号 ———
if ($Bump) {
    Step "把版本号改成 $Bump"
    $t = [System.IO.File]::ReadAllText($proj)
    $t = [regex]::Replace($t, '<Version>[^<]*</Version>', "<Version>$Bump</Version>")
    $t = [regex]::Replace($t, '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$Bump.0</AssemblyVersion>")
    $t = [regex]::Replace($t, '<FileVersion>[^<]*</FileVersion>', "<FileVersion>$Bump.0</FileVersion>")
    [System.IO.File]::WriteAllText($proj, $t, $utf8)
    $verBak = "$dll.bak"
    Write-Host "版本号已写入 csproj；记得同步改 Plugin.cs 里的版本字符串（若有）与文档里的版本号"
}

# ——— 1) 构建 ———
if (-not $NoBuild) {
    Step "构建 Release"
    & dotnet build $proj -c Release --nologo -v:m
    if ($LASTEXITCODE -ne 0) { throw "构建失败，已中止" }
}

if (-not (Test-Path $dll)) { throw "找不到产物：$dll" }
$info = Get-Item $dll
$sha  = (Get-FileHash $dll -Algorithm SHA256).Hash
Write-Host ("产物：{0:N0} B / {1:yyyy-MM-dd HH:mm:ss} / SHA256 {2}" -f $info.Length, $info.LastWriteTime, $sha)

# ——— 2) 部署到游戏（游戏没在运行才做） ———
if (-not $NoDeploy) {
    Step "部署到 BepInEx\plugins"
    if (Get-Process -Name 'Casualties*' -ErrorAction SilentlyContinue) {
        Write-Host "游戏正在运行，跳过部署（关掉游戏后重跑本脚本）" -ForegroundColor Yellow
    } else {
        # 用 try/catch 而不是中止：某些环境（如受限沙箱）不允许写游戏目录，
        # 但那不该挡住后面的同步与打包
        try {
            Copy-Item $dll (Join-Path $game 'BepInEx\plugins\OptimisedCraftingSystem.dll') -Force
            Write-Host "已写入 plugins\OptimisedCraftingSystem.dll"
        } catch {
            Write-Host ("部署失败（跳过，不影响同步）：" + $_.Exception.Message) -ForegroundColor Yellow
        }
    }
}

# ——— 3) 导出源码与文档到仓库 ———
Step "同步到发布仓库"
# 源码：排除 bin/obj/docs 与仓库根专用文件
& robocopy $dev (Join-Path $pub 'src') /E /XD bin obj docs /XF README.md NOTICE.md licence.txt *.log /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy 同步源码失败（exit $LASTEXITCODE）" }
# 中文文档：排除真机日志抓取与临时脚本
& robocopy (Join-Path $dev 'docs') (Join-Path $pub 'docs') /E /XD logs /XF *.txt /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy 同步文档失败（exit $LASTEXITCODE）" }
# 三个仓库根文件：中文 README 改名成 README.zh-CN.md，不碰英文 README.md
Copy-Item (Join-Path $dev 'README.md')   (Join-Path $pub 'README.zh-CN.md') -Force
Copy-Item (Join-Path $dev 'NOTICE.md')   (Join-Path $pub 'NOTICE.md')       -Force
Copy-Item (Join-Path $dev 'licence.txt') (Join-Path $pub 'LICENSE')         -Force

# ——— 4) 重新打包 Release 附件 ———
Step "打包 Release 附件"
$ver = ([regex]::Match([System.IO.File]::ReadAllText($proj), '<Version>([^<]*)</Version>')).Groups[1].Value
if (-not $ver) { $ver = '1.0.0' }
$zip = Join-Path $zipDir "OptimisedCraftingSystem-$ver.zip"
$stage = Join-Path $env:TEMP "ocs-release-$PID"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $dll (Join-Path $stage 'OptimisedCraftingSystem.dll')
$install = @"
Optimised Crafting System $ver - BepInEx 5 (Mono) plugin
Casualties: Unknown Demo (Unity 2022.3.62f3)

Install
  1. Extract OptimisedCraftingSystem.dll into <game>\BepInEx\plugins\
  2. Delete plugins\CraftingLogic.dll (the original BetterInventoryLogic) if present.
     If plugins\BetterCraftingLogic.dll exists (old name), rename it to
     BetterCraftingLogic.dll.disabled or move it out of plugins\ - otherwise both
     copies load and every patch is applied twice.
  3. Start the game; the log should show: Loading [Optimised Crafting System $ver]

Licensed CC BY-SA 4.0. Derivative of BetterInventoryLogic by BalaurBohemianBroken (CC BY 4.0).
"@
[System.IO.File]::WriteAllText((Join-Path $stage 'INSTALL.txt'), $install, $utf8)
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force
Write-Host ("附件：{0}（{1:N0} B）" -f $zip, (Get-Item $zip).Length)

# ——— 5) 本地提交 ———
Step "git 提交"
& $git -C $pub add -A
$pending = (& $git -C $pub status --porcelain)
if (-not $pending) {
    Write-Host "没有文件变化，跳过提交"
} else {
    & $git -C $pub commit -q -m $Message
    & $git -C $pub log --oneline -1
    Write-Host ("本次改动 {0} 个文件：" -f (@($pending).Count))
    @($pending) | Select-Object -First 12 | ForEach-Object { "    " + $_ }
}

Write-Host @"

下一步（需要你在自己的 PowerShell 里做，GitHub 授权要弹浏览器）：
    cd $pub
    git push                     # 推送提交
    git tag -a v$ver -m "v$ver"; git push origin v$ver     # 新版本才需要
然后到 https://github.com/maple-sword/OptimisedCraftingSystem/releases/new
把 $zip 拖进去当附件。

提醒：英文 README.md 是手写的，功能有变化时记得一起更新（本脚本不会覆盖它）。
"@ -ForegroundColor Green
