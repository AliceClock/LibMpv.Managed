"""Validate the imports for the minimal Windows x64 package."""
import re
import sys
from pathlib import Path

SYSTEM_DLLS = set("""
advapi32 avrt bcrypt comctl32 comdlg32 crypt32 d3d9 d3d11 dcomp dwmapi dwrite
dxgi gdi32 gdiplus hid imm32 iphlpapi kernel32 ksuser mf mfplat mfuuid
msimg32 msvcrt ncrypt ntdll ole32 oleaut32 opengl32 pathcch powrprof propsys psapi
rtworkq secur32 setupapi shcore shell32 shlwapi user32 userenv uxtheme
version winhttp winmm winspool ws2_32 wtsapi32
""".split())

pe = Path(sys.argv[1]).read_text(encoding="utf-8")
imports = {name.lower() for name in re.findall(r"DLL Name:\s*(\S+)", pe)}
if not imports:
    raise SystemExit("The PE import table was not found.")
unexpected = {
    name for name in imports
    if not name.startswith(("api-ms-win-", "ext-ms-win-"))
    and name.removesuffix(".dll") not in SYSTEM_DLLS
}
if unexpected:
    raise SystemExit(f"Unsupported non-system imports: {', '.join(sorted(unexpected))}")
for symbol in ("mpv_client_api_version", "mpv_create", "mpv_command_async", "mpv_wait_event"):
    if not re.search(rf"(?m)\]\s+{symbol}\s*$", pe):
        raise SystemExit(f"Missing standard libmpv export: {symbol}")
