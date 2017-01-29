using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupConsole
{
    // Command string format:
    // "subcommand <requiredparam1> <requiredparam2> [-a <>] [-b] [<optionalparam1> <optionalparam2> [<optionalparam3> [-c]]] [-d <>]"
    // Nested ArgumentNodes:
    // <Key=subcommand, [requiredparam1, requiredparam2], {a:"", b:null, d:""}, <Key=null, [optionalparam1, optionalparam2], {}, <Key=null, [optionalparam3], {c:null}, null>>>
    // Valid command line input:
    // subcommand val val -b -a val val val val -c (no d)

    class ArgumentScanner
    {
        Dictionary<string, ArgumentNode> Commands { get; set; } = new Dictionary<string, ArgumentNode>();
        public ArgumentScanner() { }
        public ArgumentScanner(List<string> commandstrings)
        {
            foreach (var cs in commandstrings)
            {
                AddCommand(cs);
            }
        }

        public Tuple<string, Dictionary<string, string>, Dictionary<string, string>> ParseInput(string[] args)
        {
            if (args.Length >= 1)
            {
                if (Commands.ContainsKey(args[0]))
                {
                    ArgumentNode subcommand = Commands[args[0]];
                    var parsed = subcommand.ParseArgs(args);
                    return new Tuple<string, Dictionary<string, string>, Dictionary<string, string>>(args[0], parsed.Item1, parsed.Item2);
                }
            }
            else if (Commands.ContainsKey("")) // allow for default action if no subcommand given
            {
                return null;
            }
            throw new ArgumentException("No matching subcommand found");
        }

        public void AddCommand(string commandstring)
        {
            ArgumentNode an = new ArgumentNode(commandstring);
            Commands.Add(an.Key, an);
        }

        class ArgumentNode
        {
            public string Key { get; set; }
            List<string> ArgNames { get; set; } = new List<string>();
            Dictionary<string, bool> PossibleFlags { get; set; } = new Dictionary<string, bool>();
            ArgumentNode ChildNode { get; set; } = null;

            public ArgumentNode(string commandstring)
            {
                // "subcommand <requiredparam1> <requiredparam2> [-a <>] [-b] [<optionalparam1> <optionalparam2> [<optionalparam3> [-c]]] [-d <>]"
                // <Key=subcommand, [requiredparam1, requiredparam2], {a:"", b:null, d:""}, <Key=null, [optionalparam1, optionalparam2], {}, <Key=null, [optionalparam3], {c:null}, null>>>
                // subcommand val val -b -a val val val val -c (no d)

                int bracketcounter = 0;
                int open = -1;
                for (int i = 0; i < commandstring.Length; i++)
                {
                    if (commandstring[i] == '[')
                    {
                        bracketcounter++;
                        if (bracketcounter == 1)
                        {
                            open = i;
                        }
                    }
                    else if (commandstring[i] == ']')
                    {
                        bracketcounter--;
                        if (bracketcounter == 0)
                        {
                            //outerbracketpairs.Add(new Tuple<int, int>(open, i));
                            string contents = commandstring.Substring(open + 1, i - open - 1);
                            if (contents[0] == '-') // Is flag?
                            {
                                if (contents.Contains("<>")) // Takes value?
                                {
                                    PossibleFlags.Add(contents[1].ToString(), true);
                                }
                                else
                                {
                                    PossibleFlags.Add(contents[1].ToString(), false);
                                }
                            }
                            else // is optional param(s)
                            {
                                ChildNode = new ArgumentNode(contents);
                            }
                            if (i != commandstring.Length - 1)
                            {
                                commandstring = commandstring.Substring(0, open) + commandstring.Substring(i + 2); // Remove the backets we just handled
                            }
                            else
                            {
                                commandstring = commandstring.Substring(0, open - 1);
                            }
                            i = open - 2; // Reset i
                        }
                    }
                }

                string[] kargs = commandstring.Split();
                int x;
                if (kargs[0][0] == '<')
                {
                    Key = null;
                    x = 0;
                }
                else
                {
                    Key = kargs[0];
                    x = 1;
                }
                for (; x < kargs.Length; x++)
                {
                    ArgNames.Add(kargs[x].Substring(1, kargs[x].Length - 2)); // strip out "<>"
                }
            }

            public Tuple<Dictionary<string, string>, Dictionary<string, string>> ParseArgs(string[] args, int startpos=0, Dictionary<string, string> argvals=null, Dictionary<string, string> flags=null)
            {
                if (argvals == null)
                {
                    argvals = new Dictionary<string, string>();
                }
                if (flags == null)
                {
                    flags = new Dictionary<string, string>();
                }
                if (Key != null)
                {
                    if (Key != args[startpos])
                    {
                        throw new ArgumentException("Args didnt match");
                    }
                    startpos += 1;
                }
                int argnameindex = 0;
                while (argnameindex < ArgNames.Count)
                {
                    if (args[startpos][0] == '-') // Is flag?
                    {
                        if (PossibleFlags.ContainsKey(args[startpos][1].ToString()))
                        {
                            if (PossibleFlags[args[startpos][1].ToString()]) // flag takes a value?
                            {
                                flags.Add(args[startpos][1].ToString(), args[startpos + 1]);
                                startpos++;
                            }
                            else
                            {
                                flags.Add(args[startpos][1].ToString(), null);
                            }
                        }
                        else
                        {
                            throw new ArgumentException("Invalid flag");
                        }
                    }
                    else // positional argument
                    {
                        argvals.Add(ArgNames[argnameindex], args[startpos]);
                        argnameindex++;
                    }
                    startpos++;
                }
                while (startpos < args.Length && args[startpos][0] == '-') // handle dangling flags
                {
                    if (PossibleFlags.ContainsKey(args[startpos][1].ToString()))
                    {
                        if (PossibleFlags[args[startpos][1].ToString()]) // flag takes a value?
                        {
                            flags.Add(args[startpos][1].ToString(), args[startpos + 1]);
                            startpos++;
                        }
                        else
                        {
                            flags.Add(args[startpos][1].ToString(), null);
                        }
                    }
                    else
                    {
                        throw new ArgumentException("Invalid flag");
                    }
                    startpos++;
                }
                if (startpos < args.Length && ChildNode != null)
                {
                    ChildNode.ParseArgs(args, startpos, argvals, flags);
                }
                return new Tuple<Dictionary<string, string>, Dictionary<string, string>>(argvals, flags);
            }
        }
    }
}
