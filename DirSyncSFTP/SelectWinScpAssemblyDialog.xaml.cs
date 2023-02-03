using System;
using System.Collections.Generic;
using System.IO;
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
using System.Windows.Shapes;
using GlitchedPolygons.ExtensionMethods;
using Microsoft.Win32;

namespace DirSyncSFTP
{
    /// <summary>
    /// Interaction logic for SelectWinScpExeDialog.xaml
    /// </summary>
    public partial class SelectWinScpAssemblyDialog : Window
    {
        public string AssemblyFilePath { get; private set; } = string.Empty;
        
        public SelectWinScpAssemblyDialog()
        {
            InitializeComponent();
        }

        private void ButtonPickWinScpAssemblyFile_OnClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new()
            {
                Multiselect = false,
                DefaultExt = ".dll",
                Title = "Select WinSCPnet.dll",
                Filter = "Dynamically linked library|*.dll",
            };

            if (openFileDialog.ShowDialog() == true)
            {
                AssemblyFilePath =  TextBoxWinScpExeFilePath.Text = openFileDialog.FileName;
                ButtonConfirm.IsEnabled = AssemblyFilePath.NotNullNotEmpty() && File.Exists(AssemblyFilePath);
            }
        }

        private void ButtonCancel_OnClick(object sender, RoutedEventArgs e)
        {
            AssemblyFilePath = string.Empty;
            DialogResult = false;
            Close();
        }

        private void ButtonConfirm_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}