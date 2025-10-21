# Quick Build Script

Write-Host "Building Windows Coverflow..." -ForegroundColor Cyan
dotnet build -c Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild successful!" -ForegroundColor Green
    Write-Host "Executable location: WindowsCoverflow\bin\Release\net8.0-windows\WindowsCoverflow.exe" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
}
