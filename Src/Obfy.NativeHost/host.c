#define WIN32_LEAN_AND_MEAN
#ifndef NETHOST_USE_AS_STATIC
#define NETHOST_USE_AS_STATIC
#endif
#include <windows.h>
#include <wincon.h>
#include <bcrypt.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <wchar.h>
#include <nethost.h>
#include <coreclr_delegates.h>
#include <hostfxr.h>

#define OBFY_HEADER_SIZE 72u
#define OBFY_FLAG_AES 1u
#define OBFY_PATH_CCH 32768

#pragma pack(push, 1)
typedef struct ObfyOverlayHeader
{
    uint8_t magic[4];
    uint32_t version;
    uint32_t header_size;
    uint32_t flags;
    uint32_t tfm_major;
    uint32_t payload_length;
    uint8_t iv[16];
    uint8_t key[32];
} ObfyOverlayHeader;
#pragma pack(pop)

static_assert(sizeof(ObfyOverlayHeader) == OBFY_HEADER_SIZE, "overlay header must be 72 bytes");

/* ASCII "OBPYOVL1" as little-endian uint64. Must appear once in the image. */
uint64_t ObfyOverlayOffset = 0x314C564F5950424FULL;

typedef int32_t (__stdcall *obfy_packed_run_fn)(uint8_t *payload, int32_t length);

static int stderr_is_captured(void)
{
    HANDLE h = GetStdHandle(STD_ERROR_HANDLE);
    DWORD mode;
    DWORD type;

    if (h == NULL || h == INVALID_HANDLE_VALUE)
        return 0;
    if (GetConsoleMode(h, &mode))
        return 0;
    type = GetFileType(h);
    return type == FILE_TYPE_PIPE || type == FILE_TYPE_DISK;
}

static void emit_error(const char *message)
{
    fprintf(stderr, "%s\n", message);
    fflush(stderr);
    /* GUI apps have no console. Do not MessageBox when stderr is redirected (tests, pipes). */
    if (!stderr_is_captured() && GetConsoleWindow() == NULL)
    {
        wchar_t wmsg[1024];
        if (MultiByteToWideChar(CP_UTF8, 0, message, -1, wmsg, 1024) > 0)
            MessageBoxW(NULL, wmsg, L"Packed host", MB_OK | MB_ICONERROR);
    }
}

static int invalid_payload(void)
{
    emit_error("Packed host: invalid payload.");
    return 1;
}

static int host_failed(const char *message)
{
    emit_error(message);
    return 1;
}

static int host_failed_rc(const char *fmt, int32_t rc)
{
    char buf[512];
    snprintf(buf, sizeof(buf), fmt, (int)rc);
    return host_failed(buf);
}

/* Reconstruct the unpatched sentinel without a second contiguous OBPYOVL1 immediate. */
static int overlay_is_unpatched(uint64_t offset)
{
    volatile uint32_t lo = 0x5950424Fu;
    volatile uint32_t hi = 0x314C564Fu;
    uint64_t sentinel = (uint64_t)lo | ((uint64_t)hi << 32);
    return offset == sentinel;
}

static int decrypt_aes256_cbc(
    const uint8_t *key,
    const uint8_t *iv,
    const uint8_t *cipher,
    uint32_t cipher_len,
    uint8_t **plain,
    uint32_t *plain_len)
{
    BCRYPT_ALG_HANDLE alg = NULL;
    BCRYPT_KEY_HANDLE hkey = NULL;
    PUCHAR key_obj = NULL;
    uint8_t *buf = NULL;
    NTSTATUS st;
    DWORD obj_len = 0;
    DWORD cb = 0;
    DWORD out_len = 0;
    UCHAR iv_copy[16];

    st = BCryptOpenAlgorithmProvider(&alg, BCRYPT_AES_ALGORITHM, NULL, 0);
    if (!BCRYPT_SUCCESS(st))
        return 0;

    st = BCryptSetProperty(
        alg,
        BCRYPT_CHAINING_MODE,
        (PUCHAR)BCRYPT_CHAIN_MODE_CBC,
        sizeof(BCRYPT_CHAIN_MODE_CBC),
        0);
    if (!BCRYPT_SUCCESS(st))
        goto fail;

    st = BCryptGetProperty(alg, BCRYPT_OBJECT_LENGTH, (PUCHAR)&obj_len, sizeof(obj_len), &cb, 0);
    if (!BCRYPT_SUCCESS(st) || obj_len == 0)
        goto fail;

    key_obj = (PUCHAR)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, obj_len);
    if (!key_obj)
        goto fail;

    st = BCryptGenerateSymmetricKey(alg, &hkey, key_obj, obj_len, (PUCHAR)key, 32, 0);
    if (!BCRYPT_SUCCESS(st))
        goto fail;

    memcpy(iv_copy, iv, sizeof(iv_copy));
    st = BCryptDecrypt(
        hkey,
        (PUCHAR)cipher,
        cipher_len,
        NULL,
        iv_copy,
        (ULONG)sizeof(iv_copy),
        NULL,
        0,
        &out_len,
        BCRYPT_BLOCK_PADDING);
    if (!BCRYPT_SUCCESS(st) || out_len == 0)
        goto fail;

    buf = (uint8_t *)HeapAlloc(GetProcessHeap(), 0, out_len);
    if (!buf)
        goto fail;

    memcpy(iv_copy, iv, sizeof(iv_copy));
    st = BCryptDecrypt(
        hkey,
        (PUCHAR)cipher,
        cipher_len,
        NULL,
        iv_copy,
        (ULONG)sizeof(iv_copy),
        buf,
        out_len,
        &out_len,
        BCRYPT_BLOCK_PADDING);
    if (!BCRYPT_SUCCESS(st))
        goto fail;

    BCryptDestroyKey(hkey);
    BCryptCloseAlgorithmProvider(alg, 0);
    HeapFree(GetProcessHeap(), 0, key_obj);
    *plain = buf;
    *plain_len = out_len;
    return 1;

fail:
    if (buf)
        HeapFree(GetProcessHeap(), 0, buf);
    if (hkey)
        BCryptDestroyKey(hkey);
    if (alg)
        BCryptCloseAlgorithmProvider(alg, 0);
    if (key_obj)
        HeapFree(GetProcessHeap(), 0, key_obj);
    return 0;
}

static int runtimeconfig_path(wchar_t *buf, size_t buf_cch)
{
    DWORD n;
    wchar_t *dot;
    wchar_t *slash;
    size_t len;
    static const wchar_t suffix[] = L".runtimeconfig.json";

    n = GetModuleFileNameW(NULL, buf, (DWORD)buf_cch);
    if (n == 0 || n >= buf_cch)
        return 0;

    slash = wcsrchr(buf, L'\\');
    dot = wcsrchr(buf, L'.');
    if (dot && (!slash || dot > slash))
        *dot = L'\0';

    len = wcslen(buf);
    if (len + (sizeof(suffix) / sizeof(suffix[0])) > buf_cch)
        return 0;

    if (wcscat_s(buf, buf_cch, suffix) != 0)
        return 0;
    return 1;
}

/* Directory of this EXE, including trailing backslash (AppContext.BaseDirectory form). */
static int exe_directory(wchar_t *buf, size_t buf_cch)
{
    DWORD n;
    wchar_t *slash;

    n = GetModuleFileNameW(NULL, buf, (DWORD)buf_cch);
    if (n == 0 || n >= buf_cch)
        return 0;

    slash = wcsrchr(buf, L'\\');
    if (!slash)
        return 0;
    slash[1] = L'\0';
    return 1;
}

static int load_and_run(uint8_t *plain, int32_t plain_len)
{
    char_t hostfxr_path[OBFY_PATH_CCH];
    size_t path_size = OBFY_PATH_CCH;
    HMODULE hostfxr;
    hostfxr_initialize_for_runtime_config_fn init_fn;
    hostfxr_get_runtime_delegate_fn get_delegate_fn;
    hostfxr_close_fn close_fn;
    hostfxr_set_runtime_property_value_fn set_prop_fn;
    struct hostfxr_initialize_parameters params;
    wchar_t host_path[OBFY_PATH_CCH];
    wchar_t app_base[OBFY_PATH_CCH];
    wchar_t cfg[OBFY_PATH_CCH];
    hostfxr_handle ctx = NULL;
    load_assembly_bytes_fn load_bytes = NULL;
    get_function_pointer_fn get_fn = NULL;
    HMODULE self;
    HRSRC res;
    HGLOBAL glob;
    DWORD res_size;
    const void *res_ptr;
    obfy_packed_run_fn run = NULL;
    int32_t rc;
    int32_t result;

    if (get_hostfxr_path(hostfxr_path, &path_size, NULL) != 0)
        return host_failed("Packed host failed: runtime not found.");

    hostfxr = LoadLibraryW(hostfxr_path);
    if (!hostfxr)
        return host_failed("Packed host failed: runtime not found.");

    init_fn = (hostfxr_initialize_for_runtime_config_fn)GetProcAddress(
        hostfxr, "hostfxr_initialize_for_runtime_config");
    get_delegate_fn = (hostfxr_get_runtime_delegate_fn)GetProcAddress(
        hostfxr, "hostfxr_get_runtime_delegate");
    close_fn = (hostfxr_close_fn)GetProcAddress(hostfxr, "hostfxr_close");
    set_prop_fn = (hostfxr_set_runtime_property_value_fn)GetProcAddress(
        hostfxr, "hostfxr_set_runtime_property_value");
    if (!init_fn || !get_delegate_fn || !close_fn || !set_prop_fn)
        return host_failed("Packed host failed: runtime not found.");

    if (!runtimeconfig_path(cfg, OBFY_PATH_CCH))
        return host_failed("Packed host failed: could not resolve sibling .runtimeconfig.json.");
    if (GetFileAttributesW(cfg) == INVALID_FILE_ATTRIBUTES)
        return host_failed("Packed host failed: sibling .runtimeconfig.json is missing. Copy it next to the EXE.");
    if (!GetModuleFileNameW(NULL, host_path, OBFY_PATH_CCH))
        return host_failed("Packed host failed: runtime not found.");
    if (!exe_directory(app_base, OBFY_PATH_CCH))
        return host_failed("Packed host failed: runtime not found.");

    params.size = sizeof(params);
    params.host_path = host_path;
    params.dotnet_root = NULL;

    rc = init_fn(cfg, &params, &ctx);
    if (rc < 0 || ctx == NULL)
    {
        if (ctx != NULL)
            close_fn(ctx);
        return host_failed_rc(
            "Packed host failed: could not initialize runtime (hostfxr rc=%d). Install Microsoft.NETCore.App.",
            rc);
    }

    /* Component hosting does not set the app base to the EXE directory. */
    if (set_prop_fn(ctx, L"APP_CONTEXT_BASE_DIRECTORY", app_base) != 0)
    {
        close_fn(ctx);
        return host_failed("Packed host failed: could not set APP_CONTEXT_BASE_DIRECTORY.");
    }

    if (get_delegate_fn(ctx, hdt_load_assembly_bytes, (void **)&load_bytes) != 0 ||
        get_delegate_fn(ctx, hdt_get_function_pointer, (void **)&get_fn) != 0 ||
        !load_bytes || !get_fn)
    {
        close_fn(ctx);
        return host_failed("Packed host failed: could not obtain hostfxr delegates.");
    }

    self = GetModuleHandleW(NULL);
    res = FindResourceW(self, L"BOOTSTRAP", RT_RCDATA);
    if (!res)
    {
        close_fn(ctx);
        return host_failed("Packed host failed: bootstrap missing.");
    }

    glob = LoadResource(self, res);
    res_size = SizeofResource(self, res);
    res_ptr = LockResource(glob);
    if (!glob || !res_ptr || res_size == 0)
    {
        close_fn(ctx);
        return host_failed("Packed host failed: bootstrap missing.");
    }

    if (load_bytes(res_ptr, (size_t)res_size, NULL, 0, NULL, NULL) != 0)
    {
        close_fn(ctx);
        return host_failed("Packed host failed: bootstrap missing.");
    }

    if (get_fn(
            L"Obfy.Runtime.PackedBootstrap, Obfy.PackedBootstrap",
            L"ObfyPackedRun",
            UNMANAGEDCALLERSONLY_METHOD,
            NULL,
            NULL,
            (void **)&run) != 0 ||
        !run)
    {
        close_fn(ctx);
        return host_failed("Packed host failed: bootstrap missing.");
    }

    result = run(plain, plain_len);
    close_fn(ctx);
    return (int)result;
}

int wmain(int argc, wchar_t **argv)
{
    wchar_t exe_path[OBFY_PATH_CCH];
    HANDLE file;
    LARGE_INTEGER li;
    uint64_t size;
    LARGE_INTEGER off;
    ObfyOverlayHeader hdr;
    DWORD read = 0;
    uint8_t *cipher = NULL;
    uint8_t *plain = NULL;
    uint32_t plain_len = 0;
    int rc;

    /* Args are read in PackedBootstrap via GetCommandLineArgs, not wmain. */
    (void)argc;
    (void)argv;

    if (!GetModuleFileNameW(NULL, exe_path, OBFY_PATH_CCH))
        return invalid_payload();

    file = CreateFileW(
        exe_path,
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);
    if (file == INVALID_HANDLE_VALUE)
        return invalid_payload();

    if (!GetFileSizeEx(file, &li) || li.QuadPart < 0)
    {
        CloseHandle(file);
        return invalid_payload();
    }
    size = (uint64_t)li.QuadPart;

    if (overlay_is_unpatched(ObfyOverlayOffset) ||
        ObfyOverlayOffset > size ||
        size - ObfyOverlayOffset < OBFY_HEADER_SIZE)
    {
        CloseHandle(file);
        return invalid_payload();
    }

    off.QuadPart = (LONGLONG)ObfyOverlayOffset;
    if (!SetFilePointerEx(file, off, NULL, FILE_BEGIN))
    {
        CloseHandle(file);
        return invalid_payload();
    }

    if (!ReadFile(file, &hdr, sizeof(hdr), &read, NULL) || read != sizeof(hdr))
    {
        CloseHandle(file);
        return invalid_payload();
    }

    if (hdr.magic[0] != (uint8_t)'O' ||
        hdr.magic[1] != (uint8_t)'B' ||
        hdr.magic[2] != (uint8_t)'P' ||
        hdr.magic[3] != (uint8_t)'1' ||
        hdr.version != 1u ||
        hdr.header_size != OBFY_HEADER_SIZE ||
        hdr.flags != OBFY_FLAG_AES ||
        hdr.payload_length == 0u ||
        hdr.payload_length > (uint32_t)INT32_MAX ||
        size - ObfyOverlayOffset - OBFY_HEADER_SIZE < hdr.payload_length)
    {
        CloseHandle(file);
        return invalid_payload();
    }

    cipher = (uint8_t *)HeapAlloc(GetProcessHeap(), 0, hdr.payload_length);
    if (!cipher)
    {
        CloseHandle(file);
        return invalid_payload();
    }

    if (!ReadFile(file, cipher, hdr.payload_length, &read, NULL) || read != hdr.payload_length)
    {
        HeapFree(GetProcessHeap(), 0, cipher);
        CloseHandle(file);
        return invalid_payload();
    }
    CloseHandle(file);

    if (!decrypt_aes256_cbc(hdr.key, hdr.iv, cipher, hdr.payload_length, &plain, &plain_len) ||
        plain == NULL ||
        plain_len == 0u ||
        plain_len > (uint32_t)INT32_MAX)
    {
        HeapFree(GetProcessHeap(), 0, cipher);
        if (plain)
            HeapFree(GetProcessHeap(), 0, plain);
        return invalid_payload();
    }
    HeapFree(GetProcessHeap(), 0, cipher);

    rc = load_and_run(plain, (int32_t)plain_len);
    HeapFree(GetProcessHeap(), 0, plain);
    return rc;
}
