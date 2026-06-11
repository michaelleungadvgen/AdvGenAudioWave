using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AdvGenAudioWave;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) { }
    private void Browse_Click(object sender, RoutedEventArgs e) { }
    private void Settings_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) { }
    private void Frames_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) { }
    private void PickColor_Click(object sender, RoutedEventArgs e) { }
    private void ExportApng_Click(object sender, RoutedEventArgs e) { }
    private void ExportMov_Click(object sender, RoutedEventArgs e) { }
}