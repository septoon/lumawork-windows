#include <windows.h>
#include <shellapi.h>
#include <string>
#include <iterator>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    wchar_t module[32768]{};
    const auto length = GetModuleFileNameW(nullptr, module, static_cast<DWORD>(std::size(module)));
    if (length == 0 || length >= std::size(module)) return 1;
    const std::wstring path(module, length);
    const auto directory = path.substr(0, path.find_last_of(L"\\/")) + L"\\app";
    const auto executable = directory + L"\\Инженер Lite.exe";
    if (GetFileAttributesW(executable.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        MessageBoxW(nullptr, L"Распакуйте весь архив. Рядом с этим EXE должна находиться папка app.", L"Инженер Lite", MB_OK | MB_ICONERROR);
        return 1;
    }
    SHELLEXECUTEINFOW launch{};
    launch.cbSize = sizeof(launch);
    launch.fMask = SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI;
    launch.lpVerb = L"open";
    launch.lpFile = executable.c_str();
    launch.lpDirectory = directory.c_str();
    launch.nShow = SW_SHOWNORMAL;
    if (!ShellExecuteExW(&launch))
    {
        MessageBoxW(nullptr, L"Не удалось открыть приложение. Распакуйте архив полностью и повторите запуск.", L"Инженер Lite", MB_OK | MB_ICONERROR);
        return 1;
    }
    return 0;
}
