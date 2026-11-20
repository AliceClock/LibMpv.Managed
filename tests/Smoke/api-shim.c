/* A DLL with an older Client API version, used to verify load-time rejection. */
__declspec(dllexport) unsigned long __cdecl mpv_client_api_version(void)
{
    return (2UL << 16) | 5UL;
}
