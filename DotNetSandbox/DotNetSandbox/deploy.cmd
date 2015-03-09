@echo off

setlocal

set source=%~dp0
set target=%1

if "%target%"=="" (
    echo Usage: deploy.cmd [target_path]
    goto exit
)

xcopy /e /y %source%bin %target%\bin\
xcopy /y %source%Global.asax %target%\
xcopy /y %source%Global.asax.cs %target%\
xcopy /y %source%Web.config %target%\

if not exist %target%\ReferencedAssemblies (
    mkdir %target%\ReferencedAssemblies
)

:exit