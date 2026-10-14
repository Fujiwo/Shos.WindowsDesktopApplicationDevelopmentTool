namespace TemperatureConverter.Tests;

public class TemperatureTests
{
    [Theory]
    [InlineData(0, 32)]
    [InlineData(100, 212)]
    [InlineData(-40, -40)]
    [InlineData(36.6, 97.88)]
    public void CelsiusIsConvertedToFahrenheit(double celsius, double fahrenheit)
        => Assert.Equal(fahrenheit, Temperature.FromCelsius(celsius).Fahrenheit, precision: 10);
}
