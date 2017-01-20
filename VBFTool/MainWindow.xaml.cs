using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using VBFTool.VirtuosBigFile;

namespace VBFTool
{
    public partial class MainWindow : Window
    {
        private bool ConvertPreviews = false; // Should normally not be necessary
        private int PreviewBlockCount = 100;
        private string SaveDir = "./Extracted/";

        private VirtuosBigFileReader _vbfReader;

        public MainWindow()
        {
            Loaded += (s, e) =>
            {
                if (!Directory.Exists(SaveDir))
                    Directory.CreateDirectory(SaveDir);
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
            else if (path.EndsWith(".webm"))
            {
                string videoPath;
                if (ConvertPreviews)
                {
                    videoPath = Path.Combine(SaveDir, Path.GetFileNameWithoutExtension(path) + ".preview.wmv");
                    if (!File.Exists(videoPath))
                        ConvertPreview(path, videoPath);
                }
                else
                {
                    videoPath = Path.Combine(SaveDir, Path.GetFileNameWithoutExtension(path) + ".preview.webm");
                    if (!File.Exists(videoPath))
                    {
                        var stream = File.OpenWrite(videoPath);
                        _vbfReader.GetFileContents(path, stream, PreviewBlockCount);
                        stream.Close();
                    }
                }

                previewMediaElement.LoadedBehavior = MediaState.Manual;
                previewMediaElement.Source = new Uri(videoPath, UriKind.RelativeOrAbsolute);
                previewMediaElement.Play();
            }
        }

        private void filterButton_Click(object sender, RoutedEventArgs e) => PopulateTreeView();

        private void openButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog {Filter = @"VBF files (*.vbf)|*.vbf|All files (*.*)|*.*"};
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _vbfReader = new VirtuosBigFileReader(dialog.FileName);
                _vbfReader.Open();

                PopulateTreeView();
                filterButton.IsEnabled = true;
            }
        }

        private void exportButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedNode = (TreeNode) filesTreeView.SelectedItem;
            var outputPath = Path.Combine(SaveDir, Path.GetFileName(selectedNode.FullPath));

            var steam = File.OpenWrite(outputPath);
            _vbfReader.GetFileContents(selectedNode.FullPath, steam);
            steam.Close();
        }

        private void ConvertPreview(string path, string convPath)
        {
            var process = new Process
            {
                StartInfo =
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    FileName = "ffmpeg.exe",
                    Arguments = $@"-i pipe:0 -c:v wmv2 -vf scale=320:-1 -c:a wmav2 {convPath}"
                }
            };

            process.Start();

            var stream = process.StandardInput.BaseStream;
            _vbfReader.GetFileContents(path, stream, PreviewBlockCount);
            process.WaitForExit();
        }
    }
}