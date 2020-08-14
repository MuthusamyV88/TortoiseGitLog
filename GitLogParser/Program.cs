using Nest;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GitLogParser
{
    class Entry
    {
        public string Id { get; set; }
        public string JiraId { get; set; }
        public string Author { get; set; }
        public DateTime Date { get; set; }
        public string Message { get; set; }
        public List<Change> Changes { get; set; }
    }

    enum ChangeType
    {
        Modified = 0,
        Deleted = -1,
        Added = 1
    }

    class Change
    {
        public ChangeType Type { get; set; }
        public string File { get; set; }
    }

    enum ParserEnum
    {
        Revision,
        Author,
        Date,
        Message,
        Modified,
        Deleted,
        Added,
        Unknown,
        Blank
    }

    class Program
    {
        static void Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                var client = new ElasticClient();
                if (client.Ping().IsValid)
                {
                    var indexResp = client.Indices.Create("git-commit");
                    if (!indexResp.IsValid && indexResp.ServerError.Status != 400)
                    {
                        System.Windows.MessageBox.Show("Unable to create / find necessary Elastic search index");
                        return;
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show("Elastic search is not running or not hosted in default port (9200)");
                    return;
                }

                if (!File.Exists(args[0]))
                {
                    System.Windows.MessageBox.Show($"File \"{args[0]}\" is not found or not accessible");
                }

                var choice = System.Windows.MessageBox.Show($"Have you removed line breaks for message node and SAVED the file?", "Sanitation required", System.Windows.MessageBoxButton.YesNo);
                if (choice == System.Windows.MessageBoxResult.No) return;

                int entryCount = 0, lineNo = 1;
                var entry = new Entry() { Changes = new List<Change>() };
                Regex jiraId = new Regex(@"\[*(?:SI)-\d+", RegexOptions.IgnoreCase);
                try
                {
                    foreach (var line in File.ReadLines(args[0]))
                    {

                        if (line.IndexOf("Revision", StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            // Skip the merge entry as it contains unnecessary commit details
                            if (!string.IsNullOrWhiteSpace(entry.Id) && entry.Message.IndexOf("Merge", StringComparison.OrdinalIgnoreCase) != 0)
                            {
                                client.Index<Entry>(entry, i => i.Index("git-commit"));
                                entryCount++;
                            }
                            entry = new Entry() { Changes = new List<Change>() };
                        }
                        var node = GetNode(line);
                        switch (node.Key)
                        {
                            case ParserEnum.Revision:
                                entry.Id = node.Value;
                                break;
                            case ParserEnum.Author:
                                entry.Author = node.Value;
                                break;
                            case ParserEnum.Date:
                                entry.Date = DateTime.Parse(node.Value);
                                break;
                            case ParserEnum.Message:
                                entry.Message = node.Value;
                                entry.JiraId = jiraId.Match(node.Value).Value.ToUpper();
                                break;
                            case ParserEnum.Modified:
                            case ParserEnum.Added:
                            case ParserEnum.Deleted:
                                entry.Changes.Add(new Change() { File = node.Value, Type = (ChangeType)Enum.Parse(typeof(ChangeType), node.Key.ToString()) });
                                break;
                            default: break;
                        }
                        lineNo++;
                    }

                    // Save the last entry object which will be missed after above iteration
                    if (!string.IsNullOrWhiteSpace(entry.Id) && entry.Message.IndexOf("Merge branch", StringComparison.OrdinalIgnoreCase) != 0)
                    {
                        client.Index<Entry>(entry, i => i.Index("git-commit"));
                        entryCount++;
                    }

                    System.Windows.MessageBox.Show(entryCount > 0 ? $"Imported {entryCount} commit information successfully!" : "No identifiable commit information present. Please use valid log file!");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Unable to process log.\r\nProblem in line# {lineNo}\r\n\r\nException :\r\n{ex.Message}\r\n{ex.InnerException?.Message}");
                }
            }
            else
            {
                System.Windows.MessageBox.Show("A fully qualified file name is expected as a command line argument!");
            }
        }

        private static KeyValuePair<ParserEnum, string> GetNode(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.IndexOf(':') < 0)
                return new KeyValuePair<ParserEnum, string>(ParserEnum.Blank, null);
            var entry = line.Split(':');
            var entryType = Enum.TryParse(entry[0].Trim(), out ParserEnum eType) ? eType : ParserEnum.Unknown;
            return new KeyValuePair<ParserEnum, string>(entryType, string.Join(":", entry.Skip(1)).Trim());
        }
    }
}
