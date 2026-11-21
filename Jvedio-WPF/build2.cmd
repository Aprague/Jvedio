@echo off
cd /d e:\Project\Jvedio\Jvedio-WPF
set MSBUILD_EXE="C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
%MSBUILD_EXE% ^
  /nologo ^
  /m:1 ^
  /v:minimal ^
  /t:Rebuild ^
  /p:Configuration=Debug ^
  /p:Platform="Any CPU" ^
  Jvedio.sln ^
  > build_output.txt 2>&1
echo Exit code: %errorlevel% >> build_output.txt
type build_output.txt
