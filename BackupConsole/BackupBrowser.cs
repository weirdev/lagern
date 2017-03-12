﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace BackupConsole
{
    class BackupBrowser
    {
        // Current directory (where user launches from)
        public static string cwd = Environment.CurrentDirectory;

        private static readonly ArgumentScanner browse_scanner = BrowseArgScannerFactory();

        BackupCore.Core BCore;
        string BackupHash;
        BackupCore.MetadataTree backuptree;
        BackupCore.MetadataNode currentnode;

        public BackupBrowser(string backuphash)
        {
            BackupHash = backuphash;
            string destination = Program.ReadSetting("dest");
            if (destination == null)
            {
                if (Directory.Exists("backup")) // We are in a backup destination
                {
                    destination = cwd;
                }
                else
                {
                    Console.WriteLine("A backup destination must be specified with \"set dest <path>\"");
                    Console.WriteLine("or this command must be run from an existing backup destination.");
                    return;
                }
                BCore = new BackupCore.Core(Program.cwd, destination);
            }
            backuptree = BCore.GetMetadataTree(BackupHash);
            currentnode = backuptree.Root;
        }

        public void CommandLoop()
        {
            while (true)
            {
                Console.Write(String.Format("backup {0}:{1}> ", BackupHash, currentnode.Path));
                string command = Console.ReadLine();
                string[] args = command.Split();
                try
                {
                    var parsed = browse_scanner.ParseInput(args);
                    if (parsed.Item1 == "cd")
                    {
                        // "cd <directory>"
                        ChangeDirectory(parsed.Item2["directory"]);
                    }
                    else if (parsed.Item1 == "ls")
                    {
                        // "ls"
                        ListDirectory();
                    }
                    else if (parsed.Item1 == "exit")
                    {
                        // "exit"
                        return;
                    }
                    else if (parsed.Item1 == "restore")
                    {
                        // "restore <filerelpath> [-r <>]"
                        string filerelpath = Path.Combine(currentnode.Path, parsed.Item2["filerelpath"]);
                        // If no restoreto path given, restore
                        // to cwd / its relative path
                        string restorepath = Path.Combine(Program.cwd, filerelpath);
                        if (parsed.Item3.ContainsKey("r"))
                        {
                            if (parsed.Item3["r"] == ".")
                            {
                                restorepath = Path.Combine(Program.cwd, Path.GetFileName(filerelpath));
                            }
                            else
                            {
                                restorepath = Path.Combine(parsed.Item3["r"], Path.GetFileName(filerelpath));
                            }
                        }
                        Program.RestoreFile(filerelpath, restorepath, BackupHash);
                    }
                    else if (parsed.Item1 == "help")
                    {
                        ShowCommands();
                    }
                }
                catch (Exception)
                {
                    ShowCommands();
                }
            }
        }

        private static ArgumentScanner BrowseArgScannerFactory()
        {
            ArgumentScanner scanner = new ArgumentScanner();
            scanner.AddCommand("cd <directory>");
            scanner.AddCommand("ls");
            scanner.AddCommand("restore <filerelpath> [-r <>]");
            scanner.AddCommand("exit");
            scanner.AddCommand("help");
            return scanner;
        }

        private static void ShowCommands()
        {
            foreach (var command in browse_scanner.CommandStrings)
            {
                Console.WriteLine(command);
            }
        }

        private void ListDirectory()
        {
            BackupCore.MetadataNode dir = currentnode;

            List<BackupCore.MetadataNode> childdirectories = new List<BackupCore.MetadataNode>(from cdir in dir.Directories where cdir.Key!=".." select cdir.Value);
            childdirectories.Sort(delegate (BackupCore.MetadataNode x, BackupCore.MetadataNode y)
            {
               return x.DirMetadata.FileName.CompareTo(y.DirMetadata.FileName);
            });
            foreach (var childdir in childdirectories)
            {
                Console.WriteLine(childdir.DirMetadata.FileName + Path.DirectorySeparatorChar.ToString());
            }

            List<BackupCore.FileMetadata> childfiles = new List<BackupCore.FileMetadata>(dir.Files.Values);
            childfiles.Sort(delegate (BackupCore.FileMetadata x, BackupCore.FileMetadata y)
            {
                return x.FileName.CompareTo(y.FileName);
            });
            foreach (var childfile in childfiles)
            {
                Console.WriteLine(childfile.FileName);
            }
        }

        private void ChangeDirectory(string directory)
        {
            BackupCore.MetadataNode dir;
            try
            {
                if (directory.StartsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    dir = backuptree.GetDirectory(directory);
                }
                else
                {
                    dir = backuptree.GetDirectory(Path.Combine(currentnode.Path, directory));
                }
            }
            catch (Exception)
            {
                dir = null;
            }
            if (dir == null)
            {
                Console.WriteLine("Cannot find directory specified");
                return;
            }
            currentnode = dir;
        }
    }
}
