namespace TemperatureConverter;

/// <summary>温度。摂氏で持ち、華氏でも答える。</summary>
public readonly record struct Temperature(double Celsius)
{
    public static Temperature FromCelsius(double celsius) => new(celsius);

    public double Fahrenheit => Celsius * 9 / 5 + 32;
}
