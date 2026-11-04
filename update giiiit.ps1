# update-asicfarm.ps1
Write-Host "ƒобавл€ю все изменени€..." -ForegroundColor Cyan
git add .

Write-Host "—оздаю коммит..." -ForegroundColor Cyan
git commit -m "Auto-update: $(Get-Date -Format 'yyyy-MM-dd HH:mm')"

Write-Host "ќтправл€ю на GitHub..." -ForegroundColor Cyan
git push origin master