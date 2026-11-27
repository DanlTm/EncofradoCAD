using System;
using Avalonia;

namespace EncofradoCAD.App
{
    internal sealed class Program
    {
        // Punto de entrada. Inicializa Avalonia antes de crear cualquier control.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
