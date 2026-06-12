using System.Windows;
using FFMpegCore;

namespace AdvGenAudioWave;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        GlobalFFOptions.Configure(opt =>
            opt.BinaryFolder = AppContext.BaseDirectory);
    }
}

