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
            else if (args[0] == "set")
            {
                WriteSetting(args[1], args[2]);
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
                string restorepath;
                if (args.Length >= 3)
                {
                    if (args[2] == ".")
                    {
                        restorepath = Path.Combine(cwd, Path.GetFileName(filerelpath));
                    }
                    else
                    {
                        restorepath = Path.Combine(args[2], Path.GetFileName(filerelpath));
                    }
                }
                else
                {
                    // No restoreto path given, restore
                    // to cwd / its relative path
                    restorepath = Path.Combine(cwd, filerelpath);
                }
                RestoreFile(filerelpath, restorepath);
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
                Console.WriteLine("Possible backup subcommands include:\nset <setting> <value>\nrun\nrestore <relative filepath> [<destination directory path>]");
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

        private static void RestoreFile(string filerelpath, string restorepath)
        {
            string destination = ReadSetting("dest");
            if (destination == null)
            {
                Console.WriteLine("A backup destination must be specified with \"set dest <path>\"");
                return;
            }
            BackupCore.Core bcore = new BackupCore.Core(cwd, destination);
            bcore.WriteOutFile(filerelpath, restorepath);
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
            var backups = bcore.GetBackups().Reverse().ToArray();
            show = show == -1 ? backups.Length : show;
            show = backups.Length < show ? backups.Length : show;
            for (int i = 0; i < show; i++)
            {
                Console.WriteLine("[" + i.ToString() + "]\t" + backups[i].Item1.DateTime.ToString() + "\t" +
                    backups[i].Item2);
            }
        }

        private static string ReadSetting(string key)
        {
            return ReadSettings()?[key];
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
