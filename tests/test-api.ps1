# Test script for CloudCore PDF-to-Rooms API (RIMJSON v0.3)
# Usage: .\test-api.ps1
# Tests the /api/rooms/extract endpoint and error responses

$baseUrl = "http://localhost:5091"
$apiEndpoint = "$baseUrl/api/rooms/extract"
$testEndpoint = "$baseUrl/api/rooms/test"

$testResults = @()
$passedTests = 0
$failedTests = 0

function Test-Passed {
    param([string]$TestName, [string]$Details = "")
    Write-Host "[PASS] $TestName" -ForegroundColor Green
    if ($Details) {
        Write-Host "  $Details" -ForegroundColor Gray
    }
    $script:passedTests++
    $script:testResults += @{ Test = $TestName; Status = "PASS"; Details = $Details }
}

function Test-Failed {
    param([string]$TestName, [string]$Details = "")
    Write-Host "[FAIL] $TestName" -ForegroundColor Red
    if ($Details) {
        Write-Host "  $Details" -ForegroundColor DarkRed
    }
    $script:failedTests++
    $script:testResults += @{ Test = $TestName; Status = "FAIL"; Details = $Details }
}

function Test-Skipped {
    param([string]$TestName, [string]$Details = "")
    Write-Host "[SKIP] $TestName" -ForegroundColor Yellow
    if ($Details) {
        Write-Host "  $Details" -ForegroundColor Gray
    }
    $script:testResults += @{ Test = $TestName; Status = "SKIP"; Details = $Details }
}

Write-Host "=== CloudCore API Test Script (RIMJSON v0.3) ===" -ForegroundColor Cyan
Write-Host ""

# Test 0: Health/Test Endpoint
Write-Host "Test 0: Health Check" -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri $testEndpoint -Method Get -ErrorAction Stop
    Test-Passed "Health check" "Service is running"
} catch {
    Test-Failed "Health check" "Service not available. Make sure the API is running with: dotnet run --project JSBA.CloudCore.Api"
    Write-Host "  Error: $_" -ForegroundColor DarkRed
    exit 1
}

Write-Host ""

# Test 1: Successful PDF Upload
Write-Host "Test 1: Successful PDF Upload" -ForegroundColor Yellow

# Find a test PDF file
$testPdfPath = Get-ChildItem -Path "01_SingleRoom_Only" -Filter "*.pdf" | Select-Object -First 1
if ($null -eq $testPdfPath) {
    $testPdfPath = Get-ChildItem -Path "." -Recurse -Filter "*.pdf" | Select-Object -First 1
}

if ($null -eq $testPdfPath) {
    Test-Skipped "PDF Upload" "No test PDF files found"
} else {
    Write-Host "  Using test file: $($testPdfPath.Name)" -ForegroundColor Gray
    
    $curlAvailable = Get-Command curl.exe -ErrorAction SilentlyContinue
    
    if ($curlAvailable) {
        try {
            $curlOutput = curl.exe -s -X POST "$apiEndpoint" -F "file=@$($testPdfPath.FullName)" -F "pageIndex=0" -F "unitsHint=feet" 2>&1
            $response = $curlOutput | ConvertFrom-Json
            
            # Validate RIMJSON v0.3 structure
            if ($response.version -eq "0.3" -and $response.source -and $response.rooms) {
                Test-Passed "PDF Upload" "Extracted $($response.rooms.Count) rooms, version $($response.version)"
                Write-Host "  Source: $($response.source.fileName), PageIndex: $($response.source.pageIndex), Units: $($response.source.units)" -ForegroundColor Gray
                if ($response.rooms.Count -gt 0) {
                    $room = $response.rooms[0]
                    Write-Host "  First room: ID=$($room.id), Name=$($room.name), Area=$($room.area), Perimeter=$($room.perimeter)" -ForegroundColor Gray
                }
            } else {
                Test-Failed "PDF Upload" "Response does not match RIMJSON v0.3 format"
            }
        } catch {
            Test-Failed "PDF Upload" "Request failed: $_"
            Write-Host "  Response: $curlOutput" -ForegroundColor DarkRed
        }
    } else {
        Test-Skipped "PDF Upload" "curl.exe not available"
    }
}

Write-Host ""

# Test 2: MISSING_FILE Error
Write-Host "Test 2: MISSING_FILE Error" -ForegroundColor Yellow
try {
    $curlAvailable = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curlAvailable) {
        $curlOutput = curl.exe -s -X POST "$apiEndpoint" -F "pageIndex=0" 2>&1
        $response = $curlOutput | ConvertFrom-Json
        
        # Accept both our custom error format and ASP.NET Core validation format
        $isValidError = $false
        $errorMessage = ""
        
        if ($response.error -and $response.error.code -eq "MISSING_FILE") {
            # Our custom error format
            $isValidError = $true
            $errorMessage = "$($response.error.code) - $($response.error.message)"
        } elseif ($response.errors -and $response.errors.file) {
            # ASP.NET Core model validation format (also valid - framework intercepts before controller)
            $isValidError = $true
            $errorMessage = "ASP.NET Core validation: $($response.errors.file[0])"
        }
        
        if ($isValidError) {
            Test-Passed "MISSING_FILE Error" "Correct error returned (custom or ASP.NET Core validation)"
            Write-Host "  Error: $errorMessage" -ForegroundColor Gray
        } else {
            Test-Failed "MISSING_FILE Error" "Expected MISSING_FILE error code or validation error, got: $($response | ConvertTo-Json -Compress)"
        }
    } else {
        Test-Skipped "MISSING_FILE Error" "curl.exe not available"
    }
} catch {
    Test-Failed "MISSING_FILE Error" "Request failed: $_"
}

Write-Host ""

# Test 3: INVALID_PDF Error (non-PDF file)
Write-Host "Test 3: INVALID_PDF Error (non-PDF file)" -ForegroundColor Yellow
try {
    $curlAvailable = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curlAvailable) {
        # Create a temporary text file
        $tempFile = [System.IO.Path]::GetTempFileName()
        "This is not a PDF file" | Out-File -FilePath $tempFile -Encoding ASCII
        
        try {
            $curlOutput = curl.exe -s -X POST "$apiEndpoint" -F "file=@$tempFile" -F "pageIndex=0" 2>&1
            $response = $curlOutput | ConvertFrom-Json
            
            if ($response.error -and $response.error.code -eq "INVALID_PDF") {
                Test-Passed "INVALID_PDF Error" "Correct error code returned for non-PDF file"
                Write-Host "  Error: $($response.error.code) - $($response.error.message)" -ForegroundColor Gray
            } else {
                Test-Failed "INVALID_PDF Error" "Expected INVALID_PDF error code, got: $($response | ConvertTo-Json -Compress)"
            }
        } finally {
            Remove-Item -Path $tempFile -ErrorAction SilentlyContinue
        }
    } else {
        Test-Skipped "INVALID_PDF Error" "curl.exe not available"
    }
} catch {
    Test-Failed "INVALID_PDF Error" "Request failed: $_"
}

Write-Host ""

# Test 4: INVALID_PDF Error (empty file)
Write-Host "Test 4: INVALID_PDF Error (empty file)" -ForegroundColor Yellow
try {
    $curlAvailable = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curlAvailable) {
        # Create a temporary empty file
        $tempFile = [System.IO.Path]::GetTempFileName()
        "" | Out-File -FilePath $tempFile -Encoding ASCII
        
        try {
            $curlOutput = curl.exe -s -X POST "$apiEndpoint" -F "file=@$tempFile" -F "pageIndex=0" 2>&1
            $response = $curlOutput | ConvertFrom-Json
            
            # Empty file should return either MISSING_FILE or INVALID_PDF
            if ($response.error -and ($response.error.code -eq "MISSING_FILE" -or $response.error.code -eq "INVALID_PDF")) {
                Test-Passed "INVALID_PDF Error (empty)" "Correct error code returned for empty file"
                Write-Host "  Error: $($response.error.code) - $($response.error.message)" -ForegroundColor Gray
            } else {
                Test-Failed "INVALID_PDF Error (empty)" "Expected MISSING_FILE or INVALID_PDF, got: $($response | ConvertTo-Json -Compress)"
            }
        } finally {
            Remove-Item -Path $tempFile -ErrorAction SilentlyContinue
        }
    } else {
        Test-Skipped "INVALID_PDF Error (empty)" "curl.exe not available"
    }
} catch {
    Test-Failed "INVALID_PDF Error (empty)" "Request failed: $_"
}

Write-Host ""

# Test 5: Request with pageIndex and unitsHint parameters
Write-Host "Test 5: Request with Optional Parameters" -ForegroundColor Yellow
if ($null -ne $testPdfPath -and $curlAvailable) {
    try {
        $curlOutput = curl.exe -s -X POST "$apiEndpoint" `
            -F "file=@$($testPdfPath.FullName)" `
            -F "pageIndex=0" `
            -F "unitsHint=meters" `
            -F "projectId=test-project-123" 2>&1
        $response = $curlOutput | ConvertFrom-Json
        
        if ($response.version -eq "0.3" -and $response.source.units -eq "meters") {
            Test-Passed "Optional Parameters" "Parameters accepted and reflected in response"
            Write-Host "  Units: $($response.source.units), PageIndex: $($response.source.pageIndex)" -ForegroundColor Gray
        } else {
            Test-Failed "Optional Parameters" "Parameters not properly handled"
        }
    } catch {
        Test-Failed "Optional Parameters" "Request failed: $_"
    }
} else {
    Test-Skipped "Optional Parameters" "Test PDF or curl not available"
}

Write-Host ""

# Summary
Write-Host "=== Test Summary ===" -ForegroundColor Cyan
Write-Host "Passed: $passedTests" -ForegroundColor Green
Write-Host "Failed: $failedTests" -ForegroundColor $(if ($failedTests -eq 0) { "Green" } else { "Red" })
Write-Host "Skipped: $($testResults.Count - $passedTests - $failedTests)" -ForegroundColor Yellow
Write-Host ""

if ($failedTests -eq 0) {
    Write-Host "=== All tests passed! ===" -ForegroundColor Green
    exit 0
} else {
    Write-Host "=== Some tests failed ===" -ForegroundColor Red
    exit 1
}

