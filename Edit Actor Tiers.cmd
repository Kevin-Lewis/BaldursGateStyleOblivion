@echo off
cd /d "%~dp0"
dotnet run --project tools\ActorResearch -- edit
if errorlevel 1 pause
