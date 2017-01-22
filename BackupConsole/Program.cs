using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.IO;

namespace BackupConsole
{
    public class Program
    {
        // When this becomes a regular command line program
        // (like git) we will just use the actual working directory
        private static string cwd = "C:\\Users\\Wesley\\Desktop\\test\\src";

        public static void Main(string[] args)
        {
            while (true)
            {
                Console.Write("backup > ");
                string[] input = Console.ReadLine().Split(' ');
                if (input[0] == "set")
                {
                    WriteSetting(input[1], input[2]);
                }
                else if (input[0] == "run")
                {
                    RunBackup();
                }
                else if (input[0] == "exit")
                {
                    Environment.Exit(0);
                }
            }
        }

        private static void RunBackup()
        {
            string destination = ReadSetting("dest");
            if (destination == null)
            {
                Console.WriteLine("A backup destination must be specified with \"set dest <path>\"");
                return;
            }
            BackupCore.Core bcore = new BackupCore.Core(cwd, destination);
            bcore.RunBackupSync();
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
