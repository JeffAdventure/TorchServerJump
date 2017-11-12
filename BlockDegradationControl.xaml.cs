#region

using System.Linq;
using System.Windows;
using System.Windows.Controls;

#endregion

namespace ServerJump
{
    /// <summary>
    ///     Interaction logic for ServerJumpControl.xaml
    /// </summary>
    public partial class ServerJumpControl : UserControl
    {
        public ServerJumpControl()
        {
            InitializeComponent();
        }

        public ServerJumpClass Plugin => (ServerJumpClass)DataContext;

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
         /*   var groups = Concealed.SelectedItems.Cast<ConcealGroup>().ToList();
            Concealed.SelectedItems.Clear();
            if (!groups.Any())
                return;

            var p = Plugin;
            Plugin.Torch.InvokeBlocking(delegate
            {
                foreach (var current in groups)
                    p.RevealGroup(current);
            });*/
        }

        private void Reveal_OnClick(object sender, RoutedEventArgs e)
        {
           var p = Plugin;
          //  Plugin.Torch.Invoke(delegate { p.ProcessDegradate(); });
        }

        private void Conceal_OnClick(object sender, RoutedEventArgs e)
        {
          var p = Plugin;
            Plugin.Torch.Invoke(delegate { p.Settings.Save("ServerJump.cfg"); });
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}