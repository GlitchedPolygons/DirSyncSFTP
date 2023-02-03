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
    public partial class SelectWinScpExeDialog : Window
    {
        public string ExeFilePath { get; private set; } = string.Empty;
        
        public SelectWinScpExeDialog()
        {
            InitializeComponent();
        }

        private void ButtonPickWinScpExeFile_OnClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new()
            {
                Multiselect = false,
                DefaultExt = ".exe",
                Title = "Select WinSCP.exe",
                Filter = "Windows Executable|*.exe",
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ExeFilePath =  TextBoxWinScpExeFilePath.Text = openFileDialog.FileName;
                ButtonConfirm.IsEnabled = ExeFilePath.NotNullNotEmpty() && File.Exists(ExeFilePath);
            }
        }

        private void ButtonCancel_OnClick(object sender, RoutedEventArgs e)
        {
            ExeFilePath = string.Empty;
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