# Setup PDFium Native Library
# Downloads and installs the PDFium native library for Windows x64

param(
    [string]$Platform = "win-x64"
)

$ErrorActionPreference = "Stop"

Write-Host "=== PDFium Native Library Setup ===" -ForegroundColor Cyan
Write-Host ""

# Determine download URL based on platform
$baseUrl = "https://github.com/bblanchon/pdfium-binaries/releases/latest/download"
$fileName = "pdfium-$Platform.tgz"
$downloadUrl = "$baseUrl/$fileName"

Write-Host "Platform: $Platform" -ForegroundColor Yellow
Write-Host "Download URL: $downloadUrl" -ForegroundColor Gray
Write-Host ""

# Download
Write-Host "Step 1: Downloading PDFium..." -ForegroundColor Yellow
try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $fileName -UseBasicParsing
    Write-Host "[OK] Downloaded $fileName" -ForegroundColor Green
} catch {
    Write-Host "[FAIL] Failed to download: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Extract
Write-Host "Step 2: Extracting archive..." -ForegroundColor Yellow
try {
    # Check if tar is available (Windows 10+ has built-in tar)
    $tarAvailable = Get-Command tar -ErrorAction SilentlyContinue
    
    if ($tarAvailable) {
        tar -xzf $fileName
        Write-Host "[OK] Extracted using tar" -ForegroundColor Green
    } else {
        Write-Host "[FAIL] tar command not found. Please extract $fileName manually." -ForegroundColor Red
        Write-Host "  Extract to current directory and run this script again with -SkipDownload" -ForegroundColor Yellow
        exit 1
    }
} catch {
    Write-Host "[FAIL] Failed to extract: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Copy DLL to Extractor project
Write-Host "Step 3: Copying pdfium.dll to Extractor project..." -ForegroundColor Yellow

$dllSource = ""
$dllDest = "JSBA.CloudCore.Extractor\pdfium.dll"

# Determine source path based on platform
if ($Platform -like "win-*") {
    $dllSource = "bin\pdfium.dll"
} elseif ($Platform -like "linux-*") {
    $dllSource = "lib\libpdfium.so"
    $dllDest = "JSBA.CloudCore.Extractor\libpdfium.so"
} elseif ($Platform -like "mac-*") {
    $dllSource = "lib\libpdfium.dylib"
    $dllDest = "JSBA.CloudCore.Extractor\libpdfium.dylib"
}

if (Test-Path $dllSource) {
    Copy-Item $dllSource $dllDest -Force
    Write-Host "[OK] Copied to $dllDest" -ForegroundColor Green
} else {
    Write-Host "[FAIL] Source file not found: $dllSource" -ForegroundColor Red
    Write-Host "  Available files:" -ForegroundColor Yellow
    Get-ChildItem -Recurse -Filter "*.dll" | ForEach-Object { Write-Host "    $($_.FullName)" -ForegroundColor Gray }
    Get-ChildItem -Recurse -Filter "*.so" | ForEach-Object { Write-Host "    $($_.FullName)" -ForegroundColor Gray }
    Get-ChildItem -Recurse -Filter "*.dylib" | ForEach-Object { Write-Host "    $($_.FullName)" -ForegroundColor Gray }
    exit 1
}

Write-Host ""

# Cleanup
Write-Host "Step 4: Cleaning up..." -ForegroundColor Yellow
try {
    Remove-Item $fileName -Force -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force bin -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force include -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force lib -ErrorAction SilentlyContinue
    Remove-Item LICENSE -Force -ErrorAction SilentlyContinue
    Remove-Item VERSION -Force -ErrorAction SilentlyContinue
    Write-Host "[OK] Cleanup complete" -ForegroundColor Green
} catch {
    Write-Host "[WARN] Some cleanup failed (this is usually OK): $_" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== PDFium Setup Complete! ===" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Build the project: dotnet build" -ForegroundColor Gray
Write-Host "  2. Run the API: dotnet run --project JSBA.CloudCore.Api" -ForegroundColor Gray
Write-Host ""
Write-Host "The native library is now available at:" -ForegroundColor Cyan
Write-Host "  $dllDest" -ForegroundColor Gray
Write-Host ""

