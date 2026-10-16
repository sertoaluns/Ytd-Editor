@echo off
chcp 65001 >nul
title YTD Editor - GTA5 / FiveM (Windows Forms)
cd /d "%~dp0"

if not exist "templates" mkdir "templates"

REM Eger YTD_Editor.exe mevcutsa dogrudan baslat
if exist "YTD_Editor.exe" (
    start "" "YTD_Editor.exe"
    exit /b 0
)

REM YTD_Editor.exe silinmisse C# kaynak kodlarindan otomatik derle ve baslat
echo YTD_Editor.exe derleniyor...
if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" (
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" YTDEditor.csproj /p:Configuration=Release /p:Platform=x64 /v:minimal
) else (
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:YTD_Editor.exe /platform:x64 /optimize+ /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll Program.cs Core\*.cs UI\*.cs
)

if exist "YTD_Editor.exe" (
    start "" "YTD_Editor.exe"
    exit /b 0
) else (
    echo [HATA] YTD_Editor.exe derlenemedi!
    pause
    exit /b 1
)
