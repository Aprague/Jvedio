@echo off
cd /d e:\Project\Jvedio\Jvedio-WPF
set MSBUILDDISABLENODEREVERIFY=1
"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\msbuild.exe" Jvedio.sln /nologo /t:Rebuild /p:Configuration=Debug /p:Platform="Any CPU" /maxcpucount:1 /v:minimal > build_output.txt 2>&1
echo Exit code: %errorlevel% >> build_output.txt
