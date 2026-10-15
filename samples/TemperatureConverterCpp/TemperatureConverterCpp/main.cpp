// 摂氏と華氏の対応を表示するだけの Win32 のアプリ。ブリッジの run_app で、起動と終了を確かめるためのサンプル。
#include <windows.h>
#include <format>
#include <string>
#include "Temperature.h"

namespace
{
    std::wstring ConversionTable()
    {
        std::wstring table;
        for (double celsius : { -40.0, 0.0, 36.6, 100.0 })
            table += std::format(L"{:g} °C = {:g} °F\n", celsius, temperature::ToFahrenheit(celsius));
        return table;
    }

    LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
    {
        switch (message) {
        case WM_PAINT: {
            PAINTSTRUCT paint;
            HDC dc = BeginPaint(window, &paint);
            RECT area;
            GetClientRect(window, &area);
            DrawTextW(dc, ConversionTable().c_str(), -1, &area, DT_LEFT);
            EndPaint(window, &paint);
            return 0;
        }
        case WM_DESTROY:
            // run_app の「窓を閉じる要求」(WM_CLOSE)で、ここに来てアプリが終わる。
            PostQuitMessage(0);
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }
}

int WINAPI wWinMain(_In_ HINSTANCE instance, _In_opt_ HINSTANCE, _In_ PWSTR, _In_ int showCommand)
{
    WNDCLASSW windowClass{};
    windowClass.lpfnWndProc = WindowProcedure;
    windowClass.hInstance = instance;
    windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    windowClass.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
    windowClass.lpszClassName = L"TemperatureConverterCpp";
    RegisterClassW(&windowClass);

    HWND window = CreateWindowW(windowClass.lpszClassName, L"温度の換算 (C++)", WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT, 320, 200, nullptr, nullptr, instance, nullptr);
    if (window == nullptr)
        return 1;
    ShowWindow(window, showCommand);

    MSG message;
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    return static_cast<int>(message.wParam);
}
