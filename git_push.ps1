# NixMaster Git Push Script (Fixed)
Write-Host "--- Git Sync Baslatiliyor ---" -ForegroundColor Cyan

# Buyuk dosyalari zorla cikart
git rm -r --cached NixMaster/Publish/ --ignore-unmatch
git rm --cached *.exe --ignore-unmatch

git add .
$date = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
git commit -m "NixMaster Update: $date"

Write-Host "GitHub'a gonderiliyor..." -ForegroundColor Yellow
# Tarihce cakismalarini cozmek icin force push kullaniyoruz
git push origin master --force

Write-Host "--- Islem Tamamlandi ---" -ForegroundColor Green
