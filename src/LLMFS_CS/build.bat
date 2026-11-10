@echo off
rem LLMFS folder watcher - build
rem Makes bin\llmfs\llmfs.exe (single file, self-contained) with llmfs-config.json beside it.

cd /d "%~dp0"

dotnet publish llmfs.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o bin/llmfs