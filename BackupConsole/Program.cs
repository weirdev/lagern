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
        private static string cwd = Environment.CurrentDirectory;

        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Specify a subcommand to run. Try \"backup help\" for a listing of subcommands.");
            }
            else if (args[0] == "show")
            {
                if (args.Length >= 2)
                {
                    var setting = ReadSetting(args[1]);
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
            else if (args[0] == "set")
            {
                if (args.Length >= 2)
                {
                    if (args.Length >= 3)
                    {
                        WriteSetting(args[1], args[2]);
                    }
                    else
                    {
                        Console.WriteLine("Specify a value for %s. Use \"backup clear <setting>\" to remove a setting.", args[1]);
                    }
                }
                else
                {
                    Console.WriteLine("Specify a setting to set");
                }
            }
            else if (args[0] == "clear")
            {
                ClearSetting(args[1]);
            }
            else if (args[0] == "run")
            {
                string message = null;
                if (args.Length >= 2)
                {
                    message = args[1];
                }
                RunBackup(message);
            }
            else if (args[0] == "restore")
            {
                string filerelpath = args[1];
                // No restoreto path given, restore
                // to cwd / its relative path
                string restorepath = Path.Combine(cwd, filerelpath);
                // TODO: Perhaps replace backup indexes with hashes of the metadata tree file?
                int backupindex = -1; // default to the latest backup
                if (args.Length >= 3)
                {
                    backupindex = Convert.ToInt32(args[2]);
                    if (args.Length >= 4)
                    {
                        if (args[3] == ".")
                        {
                            restorepath = Path.Combine(cwd, Path.GetFileName(filerelpath));
                        }
                        else
                        {
                            restorepath = Path.Combine(args[3], Path.GetFileName(filerelpath));
                        }
                    }
                }
                RestoreFile(filerelpath, restorepath, backupindex);
            }
            else if (args[0] == "list")
            {
                int listcount = -1;
                if (args.Length >= 2)
                {
                    listcount = Convert.ToInt32(args[1]);
                }
                ListBackups(listcount);
            }
            else if (args[0] == "help")
            {
                Console.WriteLine("Possible backup subcommands include:\nset <setting> <value>\nrun\nrestore <relative filepath> [<backup index> [<destination directory path>]]");
            }
            else
            {
                Console.WriteLine("Possible backup subcommands include:\nset <setting> <value>\nrun\nrestore <relative filepath> [<backup index> [<destination directory path>]]");
            }
        }

        private static void RunBackup(string message)
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

        private static void RestoreFile(string filerelpath, string restorepath, int backupindex)
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
            for (int i = show - 1; i >= 0; i--)
            {
                Console.WriteLine("[" + i.ToString() + "]\t" + backups[i].Item1.ToLocalTime().ToString() + "\t" +
                    backups[i].Item2);
            }
        }

        private static string ReadSetting(string key)
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
