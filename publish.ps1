# Publish Script - Creates standalone executable

Write-Host "Publishing Windows Coverflow..." -ForegroundColor Cyan

# Publish for Windows x64 (framework-dependent)
Write-Host "`nCreating framework-dependent build..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained false -o publish\framework-dependent

# Publish for Windows x64 (self-contained)
Write-Host "`nCreating self-contained build..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained true -o publish\self-contained

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nPublish successful!" -ForegroundColor Green
    Write-Host "`nOutputs:" -ForegroundColor Yellow
    Write-Host "  Framework-dependent: publish\framework-dependent\WindowsCoverflow.exe" -ForegroundColor Cyan
    Write-Host "    (Requires .NET 8.0 Runtime)" -ForegroundColor Gray
    Write-Host "  Self-contained:      publish\self-contained\WindowsCoverflow.exe" -ForegroundColor Cyan
    Write-Host "    (Includes .NET runtime - larger file)" -ForegroundColor Gray
} else {
    Write-Host "`nPublish failed!" -ForegroundColor Red
}
