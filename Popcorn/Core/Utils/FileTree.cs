using MonoTorrent.Client;
using MudBlazor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Popcorn.Shared.Extensions;
using Popcorn.Shared.Utils;

namespace Popcorn.Core.Utils
{
    public static class FileTree
    {
        public static HashSet<FileTreeNode> BuildFileTree(IList<ITorrentFileInfo> torrentFiles, string rootNodeName = "", char separator = '\\')
        {
            var rootNode = new FileTreeNode(rootNodeName);
            foreach (var torrentFile in torrentFiles.Where(x => !string.IsNullOrEmpty(x.Path.Trim())))
            {
                var currentNode = rootNode;
                var pathItems = torrentFile.Path.Split(separator);
                foreach (var (item, index) in pathItems.WithIndex())
                {
                    var tmp = currentNode.TreeItems.Where(x => x.Text.Equals(item));

                    if (tmp.Count() > 0)
                    {
                        currentNode = tmp.Single();
                    }
                    else if (pathItems.Length == index + 1)
                    {
                        var newNode = new FileTreeNode(item, torrentFile);
                        if (currentNode != rootNode)
                        {
                            newNode.Parent = currentNode;
                        }
                        currentNode.TreeItems.Add(newNode);
                    }
                    else
                    {
                        var newNode = new FileTreeNode(item);
                        if (currentNode != rootNode)
                        {
                            newNode.Parent = currentNode;
                        }
                        currentNode.TreeItems.Add(newNode);
                        currentNode = newNode;
                    }
                }
            }
            return rootNode.TreeItems;
        }

        public class FileTreeNode
        {
            public string Text { get; set; }

            public string Icon { get; set; }

            public string Path { get; set; }

            public string Size { get; set; }

            public ITorrentFileInfo? TorrentFile { get; set; }

            public bool IsExpanded { get; set; } = false;

            public bool IsChecked { get; set; } = true;

            public FileTreeNode Parent { get; set; }

            public bool HasChild => TreeItems != null && TreeItems.Count > 0;

            public HashSet<FileTreeNode> TreeItems { get; set; } = new HashSet<FileTreeNode>();

            public FileTreeNode(string text)
            {
                Text = text;
                Icon = Icons.Material.Filled.Folder;
            }

            public FileTreeNode(string text, ITorrentFileInfo torrentFile)
            {
                Text = text;
                Path = torrentFile.Path;
                Size = StringUtils.FormatBytes(torrentFile.Length);
                TorrentFile = torrentFile;
                Icon = MudUtils.GetIconFromFilename(torrentFile.Path);
            }

            public bool HasPartialChildSelection()
            {
                int iChildrenCheckedCount = (from c in TreeItems where c.IsChecked select c).Count();
                return HasChild && iChildrenCheckedCount > 0 && iChildrenCheckedCount < TreeItems.Count();
            }
        }
    }
}
