@echo off

:: Max number of inputs to read (includes password)
SET MAX_INPUTS=2
:: The directory bz2 files will go.
SET BZ2DIR=
:: The directory bsp files will go.
SET BSPDIR=
:: 'http://' required! Separate with ','
SET URIS=http://localhost:8080/mapupload/,http://localhost:8080/upload/
SET PASSWORD=

bspuploader.exe -maxinputs %MAX_INPUTS% -bz2dir "%BZ2DIR%" -bspdir "%BSPDIR%" -uris "%URIS%" -password "%PASSWORD%"
PAUSE