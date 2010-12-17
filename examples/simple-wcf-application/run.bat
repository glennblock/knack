@echo off
xcopy ..\..\bin\Debug\*.dll .
csc RunWcfApp.cs /r:owin.dll  /r:Owin.Common.dll  /r:Owin.Handlers.Wcf.dll
RunWcfApp.exe
