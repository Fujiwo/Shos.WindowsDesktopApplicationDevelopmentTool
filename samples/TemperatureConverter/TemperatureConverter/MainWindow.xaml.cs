using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace TemperatureConverter;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    void OnCelsiusChanged(object sender, TextChangedEventArgs e)
        => FahrenheitText.Text = double.TryParse(CelsiusBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var celsius)
            ? Temperature.FromCelsius(celsius).Fahrenheit.ToString("0.##", CultureInfo.CurrentCulture)
            : "-";
}
