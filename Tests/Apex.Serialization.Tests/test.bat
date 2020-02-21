
:start

echo Starting...

dotnet test -c Debug
dotnet test -c Release

if ERRORLEVEL 0 goto :start
