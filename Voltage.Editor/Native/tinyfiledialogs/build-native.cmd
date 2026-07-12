@echo off
REM Builds tinyfiledialogs.dll into runtimes\win-x64\native\.
REM Run from a Visual Studio "Developer Command Prompt" (so `cl` is on PATH), or have `clang` on PATH.
setlocal
cd /d "%~dp0"
if not exist runtimes\win-x64\native mkdir runtimes\win-x64\native

where cl >nul 2>nul
if %errorlevel%==0 (
	echo Building with MSVC cl...
	cl /nologo /O2 /LD tinyfiledialogs.c ^
		/link /OUT:runtimes\win-x64\native\tinyfiledialogs.dll ^
		comdlg32.lib ole32.lib shell32.lib user32.lib
	del /q tinyfiledialogs.obj tinyfiledialogs.lib tinyfiledialogs.exp 2>nul
	echo Done: runtimes\win-x64\native\tinyfiledialogs.dll
	goto :eof
)

where clang >nul 2>nul
if %errorlevel%==0 (
	echo Building with clang...
	clang -shared -O2 -o runtimes\win-x64\native\tinyfiledialogs.dll tinyfiledialogs.c ^
		-lcomdlg32 -lole32 -lshell32 -luser32
	echo Done: runtimes\win-x64\native\tinyfiledialogs.dll
	goto :eof
)

echo ERROR: neither cl nor clang found on PATH. Open a Developer Command Prompt or install clang. 1>&2
exit /b 1
