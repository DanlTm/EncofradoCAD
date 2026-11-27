using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace EncofradoCAD.App.Infrastructure
{
    /// <summary>
    /// Reemplazo de System.Windows.MessageBox (que no existe en Avalonia): muestra un diálogo modal
    /// simple con un mensaje y un botón Aceptar. Asíncrono, como todo diálogo en Avalonia.
    /// </summary>
    public static class MessageService
    {
        public static async Task ShowAsync(Window? owner, string title, string message)
        {
            var ok = new Button
            {
                Content = "Aceptar",
                Width = 90,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA0, 0x85)),
                Foreground = Brushes.White,
            };

            var dialog = new Window
            {
                Title = title,
                Width = 380,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(18),
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                            FontSize = 13,
                        },
                        ok,
                    },
                },
            };

            ok.Click += (_, _) => dialog.Close();

            if (owner != null)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }
        }
    }
}
