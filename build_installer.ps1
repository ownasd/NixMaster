# NixMaster Automated Build & Installer Script
# This script publishes the .NET project and builds the Inno Setup executable.

$ProjectName = "NixMaster"
$ProjectPath = ".\NixMaster\NixMaster.csproj"
$PublishPath = ".\NixMaster\Publish"
$IssFilePath = ".\NixMasterInstaller.iss"
$IsccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "   NixMaster Build & Installer System" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan

# 1. Clean previous publish folder
if (Test-Path $PublishPath) {
    Write-Host "[1/3] Cleaning publish directory..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $PublishPath
}

# 2. Dotnet Publish
Write-Host "[2/3] Publishing .NET project (Release)..." -ForegroundColor Yellow
dotnet publish $ProjectPath -c Release -o $PublishPath --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Dotnet publish failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# 3. Run Inno Setup Compiler
Write-Host "[3/3] Compiling Inno Setup installer..." -ForegroundColor Yellow

if (-not (Test-Path $IsccPath)) {
    # Try alternative path for older Inno Setup versions
    $IsccPath = "C:\Program Files (x86)\Inno Setup 5\ISCC.exe"
    if (-not (Test-Path $IsccPath)) {
        Write-Host "ERROR: ISCC.exe (Inno Setup Compiler) not found!" -ForegroundColor Red
        Write-Host "Please install Inno Setup 6 from: https://jrsoftware.org/isdl.php" -ForegroundColor Cyan
        exit 1
    }
}

& $IsccPath $IssFilePath

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Inno Setup compilation failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "==============================================" -ForegroundColor Green
Write-Host "   SUCCESS: Installer generated!" -ForegroundColor Green
Write-Host "   Check 'InstallerOutput' folder." -ForegroundColor Green
Write-Host "==============================================" -ForegroundColor Green
