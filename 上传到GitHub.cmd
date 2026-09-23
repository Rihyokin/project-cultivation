@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ============================================================
echo   备份 project-cultivation 到 GitHub
echo   仓库: https://github.com/Rihyokin/project-cultivation
echo ============================================================
echo.

REM ---------- 1) 检查 git ----------
where git >nul 2>nul
if errorlevel 1 (
    echo [X] 没有找到 git。这台机器上没装（PATH 里那条 C:\Program Files\Git\cmd 是卸载后的残留）。
    echo.
    echo     请先装 Git for Windows:  https://git-scm.com/download/win
    echo     装完关掉这个窗口、重新双击本文件即可。
    echo.
    pause
    exit /b 1
)
for /f "delims=" %%v in ('git --version') do echo [OK] %%v
echo.

REM ---------- 2) 身份（没配过就只在本仓库里配一份，不动全局） ----------
git config user.name  >nul 2>nul || git config user.name  "Rihyokin"
git config user.email >nul 2>nul || git config user.email "Rihyokin@users.noreply.github.com"

REM ---------- 3) 初始化 ----------
if not exist ".git" (
    echo [1/5] git init ...
    git init
    git branch -M main
) else (
    echo [1/5] 已经是 git 仓库，跳过 init
)

REM ---------- 4) 暂存 + 提交 ----------
echo [2/5] 暂存改动（.gitignore 已排除 Library/Temp/screenshots/somenews 等）...
git add -A

echo [3/5] 提交 ...
for /f "tokens=1-4 delims=/ " %%a in ("%date%") do set D=%%a-%%b-%%c
git commit -m "备份 %D% %time:~0,5%"
if errorlevel 1 echo      （没有新改动，或者提交被跳过）

REM ---------- 5) 远端 ----------
echo [4/5] 设置远端 origin ...
git remote remove origin >nul 2>nul
git remote add origin https://github.com/Rihyokin/project-cultivation.git

echo [5/5] 推送 ...
echo.
echo    首次推送要 2 GB 左右（Assets 里含 1.7 GB 的三方资源包），
echo    慢是正常的。中途会要求你登录 GitHub —— 用浏览器授权，或贴一个 Personal Access Token。
echo.
git push -u origin main
if errorlevel 1 (
    echo.
    echo [X] 推送失败。常见原因：
    echo     1. 仓库还没在 GitHub 上创建 ^(要先建一个空的 project-cultivation^)
    echo     2. 认证没过 —— 用 token 而不是密码： https://github.com/settings/tokens
    echo     3. 单个文件超过 100 MB —— 看下面的提示
    echo.
    echo     如果想跳过那 1.7 GB 三方资源包（推得快很多），
    echo     把 .gitignore 里 "cultivation/Assets/resources/" 那行取消注释再试。
    pause
    exit /b 1
)

echo.
echo [OK] 推送完成: https://github.com/Rihyokin/project-cultivation
echo.
pause
