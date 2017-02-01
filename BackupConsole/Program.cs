using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Xml;
using System.IO;

namespace BackupConsole
{
    public class Program
    {
        // Current directory (where user launch from)
        public static string cwd = Environment.CurrentDirectory;
        private static readonly ArgumentScanner main_scanner = MainArgScannerFactory();

        public static void Main(string[] args)
        {
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
                        foreach (var setval in ReadSettings())
                        {
                            Console.WriteLine(setval.Key + ": " + setval.Value);
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
                    // "run [<message>]"
                    string message = null;
                    if (parsed.Item2.ContainsKey("message"))
                    {
                        message = parsed.Item2["message"];
                    }
                    RunBackup(message);
                }
                else if (parsed.Item1 == "restore")
                {
                    // "restore <filerelpath> [-i <>] [-r <>]"
                    string filerelpath = parsed.Item2["filerelpath"];
                    // If no restoreto path given, restore
                    // to cwd / its relative path
                    string restorepath = Path.Combine(cwd, filerelpath);
                    // TODO: Perhaps replace backup indexes with hashes of the metadata tree file?
                    int backupindex = -1; // default to the latest backup
                    if (parsed.Item3.ContainsKey("i"))
                    {
                        backupindex = Convert.ToInt32(parsed.Item3["i"]);
                    }
                    if (parsed.Item3.ContainsKey("r"))
                    {
                        if (parsed.Item3["r"] == "\\")
                        {
                            restorepath = Path.Combine(cwd, Path.GetFileName(filerelpath));
                        }
                        else
                        {
                            restorepath = Path.Combine(parsed.Item3["r"], Path.GetFileName(filerelpath));
                        }
                    }
                    RestoreFile(filerelpath, restorepath, backupindex);
                }
                else if (parsed.Item1 == "list")
                {
                    // "list [<listcount>]"
                    int listcount = -1;
                    if (parsed.Item2.ContainsKey("listcount"))
                    {
                        listcount = Convert.ToInt32(parsed.Item2["listcount"]);
                    }
                    ListBackups(listcount);
                }
                else if (parsed.Item1 == "browse")
                {
                    // "browse [-i <>]"
                    int backupindex = -1;
                    if (parsed.Item3.ContainsKey("i"))
                    {
                        backupindex = Convert.ToInt32(parsed.Item3["i"]);
                    }
                    try
                    {
                        var browser = new BackupBrowser(backupindex);
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
            scanner.AddCommand("run [<message>]");
            scanner.AddCommand("restore <filerelpath> [-i <>] [-r <>]");
            scanner.AddCommand("list [<listcount>]");
            scanner.AddCommand("browse [-i <>]");
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

        private static void RunBackup(string message=null)
        {
            string destination = ReadSetting("dest");
            if (destination == null)
            {
                Console.WriteLine("A backup destination must be specified with \"set dest <path>\"");
                return;
            }
            BackupCore.Core bcore = new BackupCore.Core(cwd, destination);
            bcore.RunBackupSync(message);
        }

        public static void RestoreFile(string filerelpath, string restorepath, int backupindex)
        {
            string destination = ReadSetting("dest");
            if (destination == null)
            {
                Console.WriteLine("A backup destination must be specified with \"set dest <path>\"");
                return;
            }
            BackupCore.Core bcore = new BackupCore.Core(cwd, destination);
            bcore.WriteOutFile(filerelpath, restorepath, backupindex);
        }

        private static void ListBackups(int show=-1)
        {
            string destination = ReadSetting("dest");
            if (destination == null)
            {
                Console.WriteLine("A backup destination must be specified with \"set dest <path>\"");
                return;
            }
            BackupCore.Core bcore = new BackupCore.Core(cwd, destination);
            var backups = bcore.GetBackups().ToArray();
            show = show == -1 ? backups.Length : show;
            show = backups.Length < show ? backups.Length : show;
            for (int i = backups.Length - 1; i >= backups.Length - show; i--)
            {
                Console.WriteLine("[" + i.ToString() + "]\t" + backups[i].Item1.ToLocalTime().ToString() + "\t" +
                    backups[i].Item2);
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

        private static Dictionary<string, string> ReadSettings()
        {
            try
            {
                Dictionary<string, string> settings = new Dictionary<string, string>();
                using (FileStream fs = new FileStream(Path.Combine(cwd, ".backup"), FileMode.Open))
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
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        private static void WriteSettings(Dictionary<string, string> settings)
        {
            using (FileStream fs = new FileStream(Path.Combine(cwd, ".backup"), FileMode.Create))
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
