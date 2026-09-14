#define WIN32_LEAN_AND_MEAN
#define NETHOST_USE_AS_STATIC
#include <windows.h>
#include <winreg.h>
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

static int invalid_payload(void)
{
    fprintf(stderr, "Packed host: invalid payload.\n");
    return 1;
}

static int host_failed(const char *message)
{
    fprintf(stderr, "%s\n", message);
    return 1;
}

/* Reconstruct the unpatched sentinel without a second contiguous OBPYOVL1 immediate. */
static int overlay_is_unpatched(uint64_t offset)
{
    volatile uint32_t lo = 0x5950424Fu;
    volatile uint32_t hi = 0x314C564Fu;
    uint64_t sentinel = (uint64_t)lo | ((uint64_t)hi << 32);
    return offset == sentinel;
}

static int path_is_dir(const wchar_t *path)
{
    DWORD attr = GetFileAttributesW(path);
    return attr != INVALID_FILE_ATTRIBUTES && (attr & FILE_ATTRIBUTE_DIRECTORY);
}

static int path_is_file(const wchar_t *path)
{
    DWORD attr = GetFileAttributesW(path);
    return attr != INVALID_FILE_ATTRIBUTES && !(attr & FILE_ATTRIBUTE_DIRECTORY);
}

static int env_dir(const wchar_t *name, wchar_t *out, size_t cch)
{
    DWORD n = GetEnvironmentVariableW(name, out, (DWORD)cch);
    return n > 0 && n < cch && path_is_dir(out);
}

static int registry_dotnet_root(wchar_t *out, size_t cch)
{
    HKEY key = NULL;
    DWORD size;
    LONG rc;

    if (RegOpenKeyExW(
            HKEY_LOCAL_MACHINE,
            L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64",
            0,
            KEY_READ | KEY_WOW64_64KEY,
            &key) != ERROR_SUCCESS)
    {
        return 0;
    }

    size = (DWORD)(cch * sizeof(wchar_t));
    rc = RegGetValueW(key, NULL, L"InstallLocation", RRF_RT_REG_SZ, NULL, out, &size);
    RegCloseKey(key);
    return rc == ERROR_SUCCESS && path_is_dir(out);
}

static int default_dotnet_root(wchar_t *out, size_t cch)
{
    wchar_t pf[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"ProgramW6432", pf, MAX_PATH);
    if (n == 0 || n >= MAX_PATH)
        n = GetEnvironmentVariableW(L"ProgramFiles", pf, MAX_PATH);
    if (n == 0 || n >= MAX_PATH)
        return 0;
    if (swprintf_s(out, cch, L"%s\\dotnet", pf) < 0)
        return 0;
    return path_is_dir(out);
}

static int parse_fxr_version(const wchar_t *s, int parts[4])
{
    int idx = 0;
    int val = 0;
    int got = 0;
    const wchar_t *p;

    parts[0] = parts[1] = parts[2] = parts[3] = 0;
    for (p = s; *p; p++)
    {
        if (*p >= L'0' && *p <= L'9')
        {
            val = val * 10 + (*p - L'0');
            got = 1;
        }
        else if (*p == L'.' && idx < 3)
        {
            parts[idx++] = val;
            val = 0;
        }
        else
        {
            return 0;
        }
    }
    if (!got || idx > 3)
        return 0;
    parts[idx] = val;
    return 1;
}

static int version_newer(const int a[4], const int b[4])
{
    int i;
    for (i = 0; i < 4; i++)
    {
        if (a[i] != b[i])
            return a[i] > b[i];
    }
    return 0;
}

static int pick_latest_hostfxr(const wchar_t *dotnet_root, wchar_t *out, size_t cch)
{
    wchar_t fxr[OBFY_PATH_CCH];
    wchar_t pattern[OBFY_PATH_CCH];
    wchar_t best_name[64];
    int best_parts[4];
    WIN32_FIND_DATAW fd;
    HANDLE find;
    int found = 0;

    if (swprintf_s(fxr, OBFY_PATH_CCH, L"%s\\host\\fxr", dotnet_root) < 0)
        return 0;
    if (swprintf_s(pattern, OBFY_PATH_CCH, L"%s\\*", fxr) < 0)
        return 0;

    find = FindFirstFileW(pattern, &fd);
    if (find == INVALID_HANDLE_VALUE)
        return 0;

    best_name[0] = L'\0';
    best_parts[0] = best_parts[1] = best_parts[2] = best_parts[3] = 0;
    do
    {
        wchar_t candidate[OBFY_PATH_CCH];
        int parts[4];

        if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY))
            continue;
        if (fd.cFileName[0] == L'.')
            continue;
        if (!parse_fxr_version(fd.cFileName, parts))
            continue;
        if (swprintf_s(candidate, OBFY_PATH_CCH, L"%s\\%s\\hostfxr.dll", fxr, fd.cFileName) < 0)
            continue;
        if (!path_is_file(candidate))
            continue;
        if (!found || version_newer(parts, best_parts))
        {
            if (wcscpy_s(best_name, 64, fd.cFileName) != 0)
                continue;
            best_parts[0] = parts[0];
            best_parts[1] = parts[1];
            best_parts[2] = parts[2];
            best_parts[3] = parts[3];
            found = 1;
        }
    } while (FindNextFileW(find, &fd));
    FindClose(find);

    if (!found)
        return 0;
    return swprintf_s(out, cch, L"%s\\%s\\hostfxr.dll", fxr, best_name) >= 0;
}

/*
 * nethost.lib / libnethost.lib from current host packs require MSVC 14.42+
 * (__std_find_end_2). Implement get_hostfxr_path so the stub has no nethost.dll
 * dependency and still locates hostfxr the same way nethost does.
 */
int NETHOST_CALLTYPE get_hostfxr_path(
    char_t *buffer,
    size_t *buffer_size,
    const struct get_hostfxr_parameters *parameters)
{
    wchar_t root[OBFY_PATH_CCH];
    wchar_t path[OBFY_PATH_CCH];
    size_t needed;
    int have_root = 0;

    if (!buffer_size)
        return -1;

    root[0] = L'\0';
    if (parameters && parameters->dotnet_root && parameters->dotnet_root[0])
    {
        if (wcsncpy_s(root, OBFY_PATH_CCH, parameters->dotnet_root, _TRUNCATE) == 0)
            have_root = path_is_dir(root);
    }

    if (!have_root)
        have_root = env_dir(L"DOTNET_ROOT(x64)", root, OBFY_PATH_CCH);
    if (!have_root)
        have_root = env_dir(L"DOTNET_ROOT", root, OBFY_PATH_CCH);
    if (!have_root)
        have_root = registry_dotnet_root(root, OBFY_PATH_CCH);
    if (!have_root)
        have_root = default_dotnet_root(root, OBFY_PATH_CCH);

    if (!have_root || !pick_latest_hostfxr(root, path, OBFY_PATH_CCH))
        return -1;

    needed = wcslen(path) + 1;
    if (!buffer || *buffer_size < needed)
    {
        *buffer_size = needed;
        return (int)0x80008098;
    }

    if (wcscpy_s(buffer, *buffer_size, path) != 0)
        return -1;
    *buffer_size = needed;
    return 0;
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

static int load_and_run(uint8_t *plain, int32_t plain_len)
{
    char_t hostfxr_path[OBFY_PATH_CCH];
    size_t path_size = OBFY_PATH_CCH;
    HMODULE hostfxr;
    hostfxr_initialize_for_runtime_config_fn init_fn;
    hostfxr_get_runtime_delegate_fn get_delegate_fn;
    hostfxr_close_fn close_fn;
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
    if (!init_fn || !get_delegate_fn || !close_fn)
        return host_failed("Packed host failed: runtime not found.");

    if (!runtimeconfig_path(cfg, OBFY_PATH_CCH))
        return host_failed("Packed host failed: runtime not found.");

    rc = init_fn(cfg, NULL, &ctx);
    if (rc < 0 || ctx == NULL)
        return host_failed("Packed host failed: runtime not found.");

    if (get_delegate_fn(ctx, hdt_load_assembly_bytes, (void **)&load_bytes) != 0 ||
        get_delegate_fn(ctx, hdt_get_function_pointer, (void **)&get_fn) != 0 ||
        !load_bytes || !get_fn)
    {
        close_fn(ctx);
        return host_failed("Packed host failed: runtime not found.");
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
