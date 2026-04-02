@ECHO OFF
ECHO "POST SCRIPT STARTED"
set ProjectName=%1
set ProjectDir=%2
set ModFolder=%appdata%\SpaceEngineers\Mods\%ProjectName%
set ScriptFolder=%ModFolder%\Data\Scripts\%ProjectName%

rmdir "%ModFolder%" /s /q 2>nul
mkdir "%ModFolder%"
xcopy "%ProjectDir%" "%ModFolder%" /S /Y

rmdir "%ScriptFolder%\bin" /s /q 2>nul
rmdir "%ScriptFolder%\obj" /s /q 2>nul
rmdir "%ScriptFolder%\Properties" /s /q 2>nul
rmdir "%ScriptFolder%\packages" /s /q 2>nul
rmdir "%ScriptFolder%\.vs\" /s /q 2>nul
rmdir "%ModFolder%\Release Notes" /s /q 2>nul

del /s /q "%ScriptFolder%\*.config" 2>nul
del /s /q "%ScriptFolder%\*.svn" 2>nul
del /s /q "%ScriptFolder%\*.sln" 2>nul
del /s /q "%ScriptFolder%\*.csproj" 2>nul
del /s /q "%ScriptFolder%\*.user" 2>nul
del /s /q "%ScriptFolder%\..\..\..\Textures\Models\*.xcf" 2>nul

set ModFolderTesting=%ModFolder%-Testing
rmdir "%ModFolderTesting%" /s /q 2>nul
mkdir "%ModFolderTesting%"
xcopy "%ModFolder%" "%ModFolderTesting%" /S /Y
xcopy "%ProjectDir%\..\_Scripts\BuildAndRepairSystem\Testing\" "%ModFolderTesting%" /S /Y

set ModFolderOriginal=%ModFolder%-Original
rmdir "%ModFolderOriginal%" /s /q 2>nul
mkdir "%ModFolderOriginal%"
xcopy "%ModFolder%" "%ModFolderOriginal%" /S /Y
xcopy "%ProjectDir%\..\_Scripts\BuildAndRepairSystem\Original\" "%ModFolderOriginal%" /S /Y

exit /b 0
