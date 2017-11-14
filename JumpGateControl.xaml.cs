using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ServerJump
{
    /// <summary>
    /// Логика взаимодействия для JumpGateControl.xaml
    /// </summary>
    public partial class JumpGateControl : UserControl
    {
        private ServerJumpClass Plugin { get; }
        public JumpGateControl()
        {
            InitializeComponent();
        }

        public JumpGateControl(ServerJumpClass plugin) : this()
        {
            Plugin = plugin;
            DataContext = plugin.Config;
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            Plugin.Save();
        }
    }
}
