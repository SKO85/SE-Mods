ECHO "POST SCRIPT STARTED"
set ProjectName=%1
set ProjectDir=%2
set ModFolder=%appdata%\SpaceEngineers\Mods\%ProjectName%
set ScriptFolder=%ModFolder%\Data\Scripts\%ProjectName%

rmdir "%ModFolder%" /s /q
mkdir "%ModFolder%"
xcopy "%ProjectDir%" "%ModFolder%" /S /Y

rmdir "%ScriptFolder%\bin" /s /q
rmdir "%ScriptFolder%\obj" /s /q
rmdir "%ScriptFolder%\Properties" /s /q
rmdir "%ScriptFolder%\packages" /s /q
rmdir "%ScriptFolder%\.vs\" /s /q

del /s /q "%ScriptFolder%\*.config"
del /s /q "%ScriptFolder%\*.svn"
del /s /q "%ScriptFolder%\*.sln"
del /s /q "%ScriptFolder%\*.csproj"
del /s /q "%ScriptFolder%\*.user"
del /s /q "%ScriptFolder%\..\..\..\Textures\Models\*.xcf"
