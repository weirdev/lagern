using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace BackupConsole
{
    public class Program
    {
        // Current directory (where user launches from)
        public static string cwd = Environment.CurrentDirectory;
        private static readonly ArgumentScanner main_scanner = MainArgScannerFactory();

        public static void Main(string[] args)
        {
            // TODO: Get rid of this large try,catch block and catch errors closer to where they occur
            // ... in Core, during parsing, etc.
            try
            {
                var parsed = main_scanner.ParseInput(args);
                if (parsed.Item1 == "show")
                {
                    // "show [<setting>]"
                    if (parsed.Item2.ContainsKey("setting"))
                    {
                        var setting = ReadSetting(parsed.Item2["setting"]);
                        if (setting != null)
                        {
                            Console.WriteLine(setting);
                        }
                    }
                    else
                    {
                        var settings = ReadSettings();
                        if (settings != null)
                        {
                            foreach (var setval in settings)
                            {
                                Console.WriteLine(setval.Key + ": " + setval.Value);
                            }
                        }
                    }
                }
                else if (parsed.Item1 == "set")
                {
                    // "set <setting> <value>"
                    WriteSetting(parsed.Item2["setting"], parsed.Item2["value"]);
                }
                else if (parsed.Item1 == "clear")
                {
                    // "clear <setting>"
                    ClearSetting(parsed.Item2["setting"]);
                }
                else if (parsed.Item1 == "run")
                {
                    // "run [<message>] [-s]"
                    string message = null;
                    if (parsed.Item2.ContainsKey("message"))
                    {
                        message = parsed.Item2["message"];
                    }
                    bool diffbackup = !parsed.Item3.ContainsKey("s"); // force scan
                    RunBackup(message, diffbackup);
                }
                else if (parsed.Item1 == "delete")
                {
                    // "delete <backuphash>"
                    string backuphash = parsed.Item2["backuphash"];
                    DeleteBackup(backuphash);
                }
                else if (parsed.Item1 == "restore")
                {
                    // "restore <filerelpath> [-b <>] [-r <>]"
                    string filerelpath = parsed.Item2["filerelpath"];
                    // If no restoreto path given, restore
                    // to cwd / its relative path
                    string restorepath = Path.Combine(cwd, filerelpath);
                    string backuphash = null;
                    if (parsed.Item3.ContainsKey("b"))
                    {
                        backuphash = parsed.Item3["b"];
                    }
                    if (parsed.Item3.ContainsKey("r"))
                    {
                        if (parsed.Item3["r"] == ".")
                        {
                            restorepath = Path.Combine(cwd, Path.GetFileName(filerelpath));
                        }
                        else
                        {
                            restorepath = Path.Combine(parsed.Item3["r"], Path.GetFileName(filerelpath));
                        }
                    }
                    RestoreFile(filerelpath, restorepath, backuphash);
                }
                else if (parsed.Item1 == "list")
                {
                    // "list [<listcount>] [-s]"
                    int listcount = -1;
                    if (parsed.Item2.ContainsKey("listcount"))
                    {
                        listcount = Convert.ToInt32(parsed.Item2["listcount"]);
                    }
                    bool calculatesizes = parsed.Item3.ContainsKey("s");
                    try
                    {
                        BackupCore.Core bcore = GetCore();
                        ListBackups(bcore, parsed.Item3.ContainsKey("s"), listcount);
                    }
                    catch
                    {
                        Console.WriteLine("Failed to initialize Backup program");
                    }
                }
                else if (parsed.Item1 == "browse")
                {
                    // "browse [<backuphash>]"
                    string backuphash = null;
                    if (parsed.Item2.ContainsKey("backuphash"))
                    {
                        backuphash = parsed.Item2["backuphash"];
                    }
                    try
                    {
                        var browser = new BackupBrowser(backuphash);
                        browser.CommandLoop();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }
                else if (parsed.Item1 == "help")
                {
                    // "help"
                    ShowCommands();
                }
            }
            catch
            {
                ShowCommands();
            }
        }

        private static ArgumentScanner MainArgScannerFactory()
        {
            ArgumentScanner scanner = new ArgumentScanner();
            scanner.AddCommand("show [<setting>]");
            scanner.AddCommand("set <setting> <value>");
            scanner.AddCommand("clear <setting>");
            scanner.AddCommand("run [-s] [<message>]");
            scanner.AddCommand("delete <backuphash>");
            scanner.AddCommand("restore <filerelpath> [-b <>] [-r <>]");
            scanner.AddCommand("list [<listcount>] [-s]");
            scanner.AddCommand("browse [<backuphash>]");
            scanner.AddCommand("help");
            return scanner;
        }

        private static void ShowCommands()
        {
            foreach (var command in main_scanner.CommandStrings)
            {
                Console.WriteLine(command);
            }
        }

        private static void RunBackup(string message=null, bool diffbackup=true)
        {
            try
            {
                var bcore = GetCore();
                var trackclasses = GetTrackClasses();
                bcore.RunBackupSync(message, diffbackup, trackclasses);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private static void DeleteBackup(string backuphash)
        {
            try
            { 
                var bcore = GetCore();
                bcore.RemoveBackup(backuphash);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public static void RestoreFile(string filerelpath, string restorepath, string backuphash)
        {
            try
            {
                var bcore = GetCore();
                bcore.RestoreFileOrDirectory(filerelpath, restorepath, backuphash);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        internal static void ListBackups(BackupCore.Core bcore, bool calculatesizes, int show = -1)
        {
            var backups = bcore.GetBackups().ToArray();
            show = show == -1 ? backups.Length : show;
            show = backups.Length < show ? backups.Length : show;
            TablePrinter table = new TablePrinter();
            if (calculatesizes)
            {
                table.AddHeaderRow(new string[] { "Hash", "Saved", "RestoreSize", "BackupSize", "Message" });
                for (int i = backups.Length - 1; i >= backups.Length - show; i--)
                {
                    var sizes = bcore.GetBackupSizes(backups[i].Item1);
                    string message = backups[i].Item3;
                    int mlength = 40;
                    if (mlength > message.Length)
                    {
                        mlength = message.Length;
                    }
                    table.AddBodyRow(new string[] {backups[i].Item1.Substring(0, 7),
                        backups[i].Item2.ToLocalTime().ToString(), Utilities.BytesFormatter(sizes.Item1),
                        Utilities.BytesFormatter(sizes.Item2), message.Substring(0, mlength) });
                }
            }
            else
            {
                table.AddHeaderRow(new string[] { "Hash", "Saved", "Message" });
                for (int i = backups.Length - 1; i >= backups.Length - show; i--)
                {
                    string message = backups[i].Item3;
                    int mlength = 40;
                    if (mlength > message.Length)
                    {
                        mlength = message.Length;
                    }
                    table.AddBodyRow(new string[] { backups[i].Item1.Substring(0, 7),
                        backups[i].Item2.ToLocalTime().ToString(), message.Substring(0, mlength) });
                }
            }
            Console.WriteLine(table);
        }

        private static List<Tuple<int, string>> GetTrackClasses()
        {
            try
            {
                List<Tuple<int, string>> trackclasses = new List<Tuple<int, string>>();
                using (FileStream fs = new FileStream(Path.Combine(GetBUSourceDir(), ".backuptrack"), FileMode.Open))
                {
                    using (StreamReader reader = new StreamReader(fs))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string[] ctp = line.Split(' ');
                            trackclasses.Add(new Tuple<int, string>(Convert.ToInt32(ctp[0]), ctp[1]));
                        }
                    }
                }
                return trackclasses;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static BackupCore.Core GetCore()
        {
            string destination = ReadSetting("dest");
            if (destination == null)
            {
                destination = GetBUDestinationDir();
                if (destination != null) // We are in a backup destination
                {
                    try
                    {
                        return new BackupCore.Core(null, destination, ContinueOrExitPrompt);
                    }
                    catch
                    {
                        throw;
                    }
                }
                else
                {
                    Console.WriteLine("A backup destination must be specified with \"set dest <path>\"");
                    Console.WriteLine("or this command must be run from an existing backup destination.");
                    throw new Exception(); // TODO: more specific exceptions
                }
            }
            else
            {
                try
                {
                    return new BackupCore.Core(cwd, destination, ContinueOrExitPrompt);
                }
                catch
                {
                    throw;
                }
            }
        }

        public static void ContinueOrExitPrompt(string error)
        {
            Console.WriteLine(error);
            Console.Write("Continue (y/n)? ");
            while (true)
            {
                ConsoleKeyInfo k = Console.ReadKey();
                if (k.KeyChar.ToString().ToLower() == "y")
                {
                    Console.WriteLine();
                    return;
                }
                else if (k.KeyChar.ToString().ToLower() == "n")
                {
                    Console.WriteLine();
                    throw new Exception("User chose not to continue");
                }
            }
        }

        public static string ReadSetting(string key)
        {
            var settings = ReadSettings();
            if (settings != null)
            {
                if (settings.ContainsKey(key))
                {
                    return settings[key];
                }
            }
            return null;
        }
        
        private static void WriteSetting(string key, string value)
        {
            var settings = ReadSettings();
            if (settings == null)
            {
                settings = new Dictionary<string, string>();
            }
            settings[key] = value;
            WriteSettings(settings);
        }

        private static void ClearSetting(string key)
        {
            var settings = ReadSettings();
            if (settings != null)
            {
                if (settings.ContainsKey(key))
                {
                    settings.Remove(key);
                    WriteSettings(settings);
                }
            }
        }

        private static string GetBUSourceDir()
        {
            string dir = cwd;
            do
            {
                if (File.Exists(Path.Combine(dir, ".backup")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            } while (dir != null);
            return null;
        }

        private static string GetBUDestinationDir()
        {
            string dir = cwd;
            do
            {
                if (Directory.Exists(Path.Combine(dir, "backup")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            } while (dir != null);
            return null;
        }

        private static Dictionary<string, string> ReadSettings()
        {
            try
            {
                Dictionary<string, string> settings = new Dictionary<string, string>();
                string src = GetBUSourceDir();
                if (src != null)
                {
                    using (FileStream fs = new FileStream(Path.Combine(src, ".backup"), FileMode.Open))
                    {
                        using (StreamReader reader = new StreamReader(fs))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                string[] kv = line.Split(' ');
                                settings[kv[0]] = kv[1];
                            }
                        }
                    }
                    return settings;
                }
                return null;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        private static void WriteSettings(Dictionary<string, string> settings)
        {
            using (FileStream fs = new FileStream(Path.Combine(GetBUSourceDir(), ".backup"), FileMode.Create))
            {
                using (StreamWriter writer = new StreamWriter(fs))
                {
                    foreach (var kv in settings)
                    {
                        writer.WriteLine(kv.Key + " " + kv.Value);
                    }
                }
            }
        }
    }
}
