using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace EncofradoCAD.App.Infrastructure
{
    /// <summary>Diálogo modal simple para pedir un número (p. ej. la escala real del plano en metros).</summary>
    public static class InputService
    {
        public static async Task<double?> AskNumberAsync(Window? owner, string title, string prompt, string? sugerido = null)
        {
            var caja = new TextBox
            {
                Text = sugerido ?? string.Empty,
                Width = 220,
                PlaceholderText = "Ej. 6.98",
            };

            var error = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0x4B, 0x4A)),
                FontSize = 12,
                IsVisible = false,
            };

            var aceptar = new Button
            {
                Content = "Aceptar",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA0, 0x85)),
                Foreground = Brushes.White,
            };
            var cancelar = new Button
            {
                Content = "Cancelar",
                Width = 100,
                Height = 32,
            };

            var botones = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancelar, aceptar },
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
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = prompt,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                            FontSize = 13,
                        },
                        caja,
                        error,
                        botones,
                    },
                },
            };

            double? resultado = null;

            void Confirmar()
            {
                string txt = caja.Text ?? string.Empty;
                if ((double.TryParse(txt, NumberStyles.Any, CultureInfo.CurrentCulture, out double v)
                     || double.TryParse(txt, NumberStyles.Any, CultureInfo.InvariantCulture, out v))
                    && v > 0)
                {
                    resultado = v;
                    dialog.Close();
                }
                else
                {
                    error.Text = "Escribe un número mayor que 0.";
                    error.IsVisible = true;
                }
            }

            aceptar.Click += (_, _) => Confirmar();
            cancelar.Click += (_, _) => { resultado = null; dialog.Close(); };

            if (owner != null)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }

            return resultado;
        }
    }
}
