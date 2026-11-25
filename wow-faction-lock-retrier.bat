@echo off
rem Default: retries every 30 seconds. To change it, edit -IntervalSec (e.g. -IntervalSec 60)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0wow-faction-lock-retrier.ps1" -IntervalSec 30
