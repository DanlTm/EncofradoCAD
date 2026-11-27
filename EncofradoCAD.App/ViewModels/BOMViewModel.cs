using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EncofradoCAD.App.ViewModels
{
    public partial class BOMViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<MaterialItem> _listaMaterialesUI;

        public BOMViewModel()
        {
            ListaMaterialesUI = new ObservableCollection<MaterialItem>();
        }

        /// <summary>Refresca la lista con las líneas ya formateadas (Nombre, Cantidad) que produce el motor.</summary>
        public void ActualizarBOM(IEnumerable<(string Nombre, string Cantidad)> filas)
        {
            ListaMaterialesUI.Clear();
            if (filas == null)
            {
                return;
            }

            foreach ((string Nombre, string Cantidad) f in filas)
            {
                ListaMaterialesUI.Add(new MaterialItem { Nombre = f.Nombre, Cantidad = f.Cantidad });
            }
        }
    }

    public class MaterialItem
    {
        public string Nombre { get; set; }
        public string Cantidad { get; set; }
    }
}
