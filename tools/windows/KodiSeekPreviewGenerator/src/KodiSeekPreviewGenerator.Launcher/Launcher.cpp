#include <windows.h>

#include <string>
#include <vector>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
  std::wstring modulePath(32768, L'\0');
  const DWORD moduleLength =
      GetModuleFileNameW(nullptr, modulePath.data(), static_cast<DWORD>(modulePath.size()));
  if (moduleLength == 0 || moduleLength >= modulePath.size())
  {
    MessageBoxW(nullptr,
                L"Não foi possível determinar a localização da aplicação.",
                L"Kodi Seek Preview Generator",
                MB_OK | MB_ICONERROR);
    return 1;
  }

  modulePath.resize(moduleLength);
  const std::wstring::size_type separator = modulePath.find_last_of(L"\\/");
  const std::wstring rootDirectory = modulePath.substr(0, separator);
  const std::wstring dataDirectory = rootDirectory + L"\\Data";
  const std::wstring applicationPath =
      dataDirectory + L"\\KodiSeekPreviewGenerator.App.exe";

  if (GetFileAttributesW(applicationPath.c_str()) == INVALID_FILE_ATTRIBUTES)
  {
    MessageBoxW(nullptr,
                L"Não foi encontrada a pasta Data da aplicação. "
                L"Volte a extrair o ZIP completo.",
                L"Kodi Seek Preview Generator",
                MB_OK | MB_ICONERROR);
    return 1;
  }

  std::wstring commandLine = L"\"" + applicationPath + L"\"";
  std::vector<wchar_t> mutableCommandLine(commandLine.begin(), commandLine.end());
  mutableCommandLine.push_back(L'\0');

  STARTUPINFOW startupInfo{};
  startupInfo.cb = sizeof(startupInfo);
  PROCESS_INFORMATION processInformation{};
  if (!CreateProcessW(applicationPath.c_str(),
                      mutableCommandLine.data(),
                      nullptr,
                      nullptr,
                      FALSE,
                      0,
                      nullptr,
                      dataDirectory.c_str(),
                      &startupInfo,
                      &processInformation))
  {
    const std::wstring message =
        L"Não foi possível abrir a aplicação. Código do Windows: " +
        std::to_wstring(GetLastError());
    MessageBoxW(nullptr,
                message.c_str(),
                L"Kodi Seek Preview Generator",
                MB_OK | MB_ICONERROR);
    return 1;
  }

  CloseHandle(processInformation.hThread);
  CloseHandle(processInformation.hProcess);
  return 0;
}
