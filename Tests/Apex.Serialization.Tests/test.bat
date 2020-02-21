
:start

echo Starting...

dotnet test -c Debug
if ERRORLEVEL 1 goto :end
dotnet test -c Release
if ERRORLEVEL 1 goto :end
goto :start

:end
