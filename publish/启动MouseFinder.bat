@echo off
chcp 65001 >/dev/null
echo 正在启动 MouseFinder...
dotnet "%~dp0MouseFinder.dll"
pause
