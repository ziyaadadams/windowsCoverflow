# Windows Coverflow Build Script
# Run this to build and launch the application

Write-Host "Building Windows Coverflow..." -ForegroundColor Cyan

# Restore dependencies
Write-Host "`nRestoring NuGet packages..." -ForegroundColor Yellow
dotnet restore

# Build the project
Write-Host "`nBuilding project in Release mode..." -ForegroundColor Yellow
dotnet build -c Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild successful!" -ForegroundColor Green
    Write-Host "`nStarting Windows Coverflow..." -ForegroundColor Cyan
    
    # Run the application
    Start-Process -FilePath "dotnet" -ArgumentList "run --project WindowsCoverflow\WindowsCoverflow.csproj -c Release"
    
    Write-Host "`nApplication launched!" -ForegroundColor Green
    Write-Host "Look for the icon in your system tray." -ForegroundColor Yellow
    Write-Host "Press Alt+Tab to activate the coverflow switcher." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check the errors above." -ForegroundColor Red
}
