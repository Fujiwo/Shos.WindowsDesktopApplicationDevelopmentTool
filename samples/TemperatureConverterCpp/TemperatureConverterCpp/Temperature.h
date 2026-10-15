#pragma once

// 温度の換算。画面から分けて、単体テストで確かめる。
namespace temperature
{
    constexpr double ToFahrenheit(double celsius)
    {
        return celsius * 9 / 5 + 32;
    }
}
