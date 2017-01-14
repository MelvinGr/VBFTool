using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using VBFTool.VirtuosBigFile;

namespace VBFTool
{
    public partial class MainWindow : Window
    {
        private readonly string _saveDir = "./Extracted/";
        private VirtuosBigFileReader _vbfReader;

        public MainWindow()
        {
            Loaded += (s, e) =>
            {
                if (!Directory.Exists(_saveDir))
                    Directory.CreateDirectory(_saveDir);
            };

            InitializeComponent();
        }

        public static BitmapImage GetBitmapImage(Stream stream)
        {
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = stream;
            bitmapImage.EndInit();
            return bitmapImage;
        }

        private static void PopulateTreeView(ICollection<TreeNode> nodeList, string path, TreeNode parent = null)
        {
            string folder;
            var p = path.IndexOf('/');
            if (p == -1)
            {
                folder = path;
                path = string.Empty;
            }
            else
            {
                folder = path.Substring(0, p);
                path = path.Substring(p + 1, path.Length - (p + 1));
            }

            var node = nodeList.FirstOrDefault(item => item.Text == folder);
            if (node == null)
            {
                node = new TreeNode(folder, parent);
                nodeList.Add(node);
            }

            if (path != string.Empty)
                PopulateTreeView(node.Nodes, path, node);
        }

        private void PopulateTreeView()
        {
            // https://stackoverflow.com/questions/30299671/matching-strings-with-wildcard
            var filter = filterTextBox.Text == string.Empty ? "*" : filterTextBox.Text;
            var regex = "^" + Regex.Escape(filter).Replace("\\?", ".").Replace("\\*", ".*") + "$";

            filesTreeView.ItemsSource = new ObservableCollection<TreeNode>();
            foreach (var line in _vbfReader.FileList.Where(l => Regex.IsMatch(l, regex)))
                PopulateTreeView((ObservableCollection<TreeNode>) filesTreeView.ItemsSource, line);
        }

        private void filesTreeView_selectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedNode = (TreeNode) filesTreeView.SelectedItem;
            if (selectedNode == null)
                return;

            exportButton.IsEnabled = selectedNode.Nodes.Count == 0;
            var path = selectedNode.FullPath;
            if (path.EndsWith(".png"))
            {
                var stream = new MemoryStream();
                _vbfReader.GetFileContents(path, stream);
                imageBox.Source = GetBitmapImage(stream);
            }
        }

        private void filterButton_Click(object sender, RoutedEventArgs e) => PopulateTreeView();

        private void openButton_Click(object sender, RoutedEventArgs e)
        {
            //@"D:\Games\Final Fantasy X X-2 HD Remaster\data\FFX2_Data.vbf"
            var dialog = new OpenFileDialog {Filter = @"VBF files (*.vbf)|*.vbf|All files (*.*)|*.*"};
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _vbfReader = new VirtuosBigFileReader(dialog.FileName);
                _vbfReader.Load();

                PopulateTreeView();
                filterButton.IsEnabled = true;
            }
        }

        private void exportButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedNode = (TreeNode) filesTreeView.SelectedItem;
            var outputPath = Path.Combine(_saveDir, Path.GetFileName(selectedNode.FullPath));

            var steam = File.OpenWrite(outputPath);
            _vbfReader.GetFileContents(selectedNode.FullPath, steam);
            steam.Close();
        }
    }
}