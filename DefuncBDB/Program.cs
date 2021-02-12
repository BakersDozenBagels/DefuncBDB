using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Numerics;
using System.IO;

/* TODO:
 * Optimise chains of +???
 * Add call-cc???
 * */

namespace DefuncBDB
{
    class Program
    {
        static void Main(string[] args)
        {
            bool asciiMode = false, strictMode = false, breakMode = false, fileMode = false, commentMode = false;
            string command;

            if (args.Contains("--help") || args.Contains("-h")) { Console.WriteLine("\nInterprets a Defunc program.\n\n  --help    -h Displays this help message.\n  --ascii   -a Converts ASCII input to integers and outputs as ASCII characters.\n  --strict  -s Makes minor errors halt execution.\n  --break   -b Gives option to break during a long execution cycle.\n  --file    -f Use F\"Filename\" to run a premade file.\n  --comment -c Allows | to be used to toggle between comments and code. (Always on for loaded files.)\n\nPress any key to exit."); Console.ReadKey(); return; }
            if (args.Contains("--ascii") || args.Contains("-a")) asciiMode = true;
            if (args.Contains("--strict") || args.Contains("-s")) strictMode = true;
            if (args.Contains("--break") || args.Contains("-b")) breakMode = true;
            if (args.Contains("--file") || args.Contains("-f")) fileMode = true;
            if (args.Contains("--comment") || args.Contains("-c")) commentMode = true;

            List<OpInfo> Functions = OpInfo.GetDefault(asciiMode);

            if (fileMode) Functions.Add(new OpInfo('F'));
            if (commentMode) Functions.Add(new OpInfo('|'));

            List<string> commands = new List<string>();

            while (true)
            {
                if (commands.Count == 0)
                {
                    commands.AddRange(Console.ReadLine().Split(new char[] { '\n' }));
                    if (commands[0] == "") break;
                }
                command = commands[0];

                #region Read From File

                Regex regex = new Regex("^F\".*\"$");

                if (fileMode && regex.IsMatch(command))
                {
                    string path = command.Substring(2, command.Length - 3);
                    string[] lines = new string[0];
                    try
                    {
                        lines = File.ReadAllLines(path);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error reading file: " + e.ToString());
                    }

                    //Strip comments.
                    string[] strippedLines = new string[lines.Length];
                    foreach (string line in lines)
                    {
                        string strippedLine = "";
                        string tempLine = line;
                        while (tempLine.Length > 0)
                        {
                            if (tempLine[0] == '|')
                            {
                                tempLine = tempLine.Substring(1);
                                while (tempLine[0] != '|' && tempLine.Length > 0) tempLine = tempLine.Substring(1);
                                tempLine = tempLine.Substring(1);
                                continue;
                            }
                            strippedLine += tempLine[0];
                            tempLine = tempLine.Substring(1);
                        }
                        strippedLines[Array.IndexOf(lines, line)] = strippedLine;
                    }

                    commands.AddRange(strippedLines);

                    commands = commands.Skip(1).ToList();
                    continue;
                }
                #endregion

                #region Strip Comments
                string strippedCommand = "";
                string tempCommand = command;
                while (tempCommand.Length > 0)
                {
                    if (tempCommand[0] == '|')
                    {
                        tempCommand = tempCommand.Substring(1);
                        while (tempCommand[0] != '|' && tempCommand.Length > 0) tempCommand = tempCommand.Substring(1);
                        tempCommand = tempCommand.Substring(1);
                        continue;
                    }
                    strippedCommand += tempCommand[0];
                    tempCommand = tempCommand.Substring(1);
                }
                if (commentMode) command = strippedCommand;
                #endregion

                #region New Function Definition

                if (!Functions.Select(x => x.Name).Contains(command[0]))
                {
                    // Scan for the function name, and any local variables.
                    char name = command[0];
                    command = command.Substring(1);
                    Dictionary<char, int> locals = new Dictionary<char, int>();
                    while (!Functions.Select(x => x.Name).Contains(command[0]) && !locals.Select(x => x.Key).Contains(command[0]))
                    {
                        locals.Add(command[0], locals.Count);
                        command = command.Substring(1);
                    }
                    
                    //Create the function itself.
                    char[] innerCommand = command.ToCharArray();
                    Functions.Add(new OpInfo(name, locals.Count, x => {
                        x.Stack.Push(y => y.MoveNext());
                        x.Stack.Push(y => {
                            List<ReturnValue> returned = y.ReturnValues.Pop();
                            ExtendedChar[] modCommand = new ExtendedChar[innerCommand.Length];
                            innerCommand.Select(z => (ExtendedChar)z).ToArray().CopyTo(modCommand, 0);
                            //Substitute in passed values.
                            foreach (KeyValuePair<char, int> kvp in locals)
                                modCommand = modCommand.Select(z => z.Equals((ExtendedChar)kvp.Key) ? new ExtendedChar(returned[kvp.Value].Value) : z).ToArray();
                            List<ExtendedChar> trimmed = new List<ExtendedChar>();
                            //Trim any excess commands.
                            int count = 1;
                            while (count > 0)
                            {
                                if (modCommand[0].Number == null) count += y.Funcs.Where(z => z.Name == modCommand[0].Value).GetFunction().ParamCount;
                                count--;
                                trimmed.AddRange(modCommand.Take(1));
                                modCommand = modCommand.Skip(1).ToArray();
                            }
                            if (strictMode && modCommand.Length > 0) throw new ArgumentCountException();
                            //Add to unanalyzed program.
                            y.Program = trimmed.Concat(y.Program).ToArray();
                            return y;
                        });
                        x.Await(locals.Count);
                        return x;
                    }));

                    commands = commands.Skip(1).ToList();
                    continue;
                }
                #endregion

                #region Execute Function

                //Initialize the program's state.
                State CurrentState = new State();
                CurrentState.Funcs = Functions;
                CurrentState.ReturnValues = new Stack<List<ReturnValue>>();
                CurrentState.Stack = new Stack<Func<State, State>>(new Func<State, State>[] {
                    new Func<State, State>(x =>  x.MoveNext())
                });
                CurrentState.Program = command.ToCharArray().Select(x => (ExtendedChar)x).ToArray();

                ulong cycles = 0ul;
                ulong max = 100_000_000ul;
                while (CurrentState.Stack.Count > 0)
                {
                    try
                    {
                        //Runs the current command.
                        CurrentState = CurrentState.Stack.Pop()(CurrentState);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("There was an error: " + e.ToString());
                        break;
                    }
                    if (breakMode && cycles++ >= max)
                    {
                        Console.Write("\nContinue execution? Y/N : ");
                        string response = Console.ReadLine();
                        if (response.Length == 0 || (response[0] != 'y' && response[0] != 'Y')) break;
                        max *= 10;
                    }
                }
                Console.WriteLine();
                if (CurrentState.Program.Count() > 0 && strictMode) throw new ArgumentCountException();
                commands = commands.Skip(1).ToList();
                #endregion
            }
        }
    }

    /// <summary>
    /// A class representing the state of a Defunc interpreter.
    /// </summary>
    public class State
    {
        /// <summary>
        /// The stack of functions to call.
        /// </summary>
        public Stack<Func<State, State>> Stack { get; set; }
        /// <summary>
        /// The stack of values to return to previous functions.
        /// </summary>
        public Stack<List<ReturnValue>> ReturnValues { get; set; }
        /// <summary>
        /// The unanalyzed program.
        /// </summary>
        public ExtendedChar[] Program { get; set; }
        /// <summary>
        /// Any previously defined functions.
        /// </summary>
        public List<OpInfo> Funcs { get; set; }

        /// <summary>
        /// Pushes a command to analyze more of the program.
        /// </summary>
        /// <returns></returns>
        public State MoveNext()
        {
            Stack.Push(y => {
                if (y.Program[0].Number != null)
                {
                    BigInteger i = y.Program[0].Number.Value;
                    y.Stack.Push(x =>
                    {
                        x.ReturnValues.Push(new List<ReturnValue>(new ReturnValue[] { i }));
                        return x;
                    });
                }
                else y.Stack.Push(y.Funcs.Where(x => x.Name == y.Program[0].Value).GetFunction().Call);
                y.Program = y.Program.Skip(1).ToArray();
                return y;
            });
            return this;
        }

        /// <summary>
        /// Pushes a command to skip running a branch of the program.
        /// </summary>
        /// <returns></returns>
        public State SkipNext()
        {
            Stack.Push(x => {
                int count = 1;
                while (count > 0)
                {
                    if (x.Program[0].Number == null) count += x.Funcs.Where(y => y.Name == x.Program[0].Value).GetFunction().ParamCount;
                    count--;
                    x.Program = x.Program.Skip(1).ToArray();
                }
                return x;
            });
            return this;
        }

        /// <summary>
        /// Pushes a command to aggragate returned variables from multiple following branches.
        /// </summary>
        /// <param name="Time">How many variables to collect.</param>
        /// <returns></returns>
        public State Await(int Time)
        {
            ReturnValues.Push(new List<ReturnValue>());
            if (Time == 0) return this;
            Stack.Push(x => {
                var v = x.ReturnValues.Pop();
                x.ReturnValues.Push(x.ReturnValues.Pop().Concat(v).ToList());
                if (Time == 1) return x;
                return x.Await2(Time - 1);
            });
            return MoveNext();
        }

        /// <summary>
        /// Used internally by Await().
        /// </summary>
        /// <param name="Time"></param>
        /// <returns></returns>
        private State Await2(int Time)
        {
            if (Time == 0) return this;
            Stack.Push(x => {
                var v = x.ReturnValues.Pop();
                x.ReturnValues.Push(x.ReturnValues.Pop().Concat(v).ToList());
                if (Time == 1) return x;
                return x.Await2(Time - 1);
            });
            return MoveNext();
        }
    }

    /// <summary>
    /// A class that holds all of the information about one Defunc function.
    /// </summary>
    public class OpInfo
    {
        /// <summary>
        /// The symbol to call the function.
        /// </summary>
        public char Name { get; }
        /// <summary>
        /// How many parameters the function takes.
        /// </summary>
        public int ParamCount { get; set; }
        /// <summary>
        /// What the function does.
        /// </summary>
        public Func<State, State> Call { get; set; }

        /// <summary>
        /// Generates the default Defunc functions.
        /// </summary>
        /// <param name="Ascii">Whether to use ASCII mode or integer mode.</param>
        /// <returns></returns>
        public static List<OpInfo> GetDefault(bool Ascii)
        {
            return new List<OpInfo>(new OpInfo[] {
                new OpInfo('0', 0, new Func<State, State>(x => {
                    x.ReturnValues.Push(new List<ReturnValue>(new ReturnValue[]{ 0 }));
                    return x;
                })),
                new OpInfo('+', 1, new Func<State, State>(x => {
                    x.Stack.Push(y => {
                        y.ReturnValues.Push(new ReturnValue[]{ y.ReturnValues.Pop().First() + 1 }.ToList());
                        return y;
                    });
                    x.Await(1);
                    return x;
                })),
                new OpInfo('?', 4, new Func<State, State>(x => {
                    x.Stack.Push(y => {
                        List<ReturnValue> comp = y.ReturnValues.Pop();
                        if (comp[0].Value > comp[1].Value)
                        {
                            y.SkipNext();
                            y.MoveNext();
                        }
                        else
                        {
                            y.MoveNext();
                            y.SkipNext();
                        }
                        return y;
                    });
                    x.Await(2);
                    return x;
                })),
                new OpInfo('.', 1, new Func<State, State>(x => {
                    x.Stack.Push(y => {
                        Console.Write((Ascii ? ((char)y.ReturnValues.Peek().First()).ToString() : (y.ReturnValues.Peek().First().Value.ToString()) + "\n"));
                        return y;
                    });
                    x.Await(1);
                    return x;
                })),
                new OpInfo(',', 0, new Func<State, State>(x => {
                    Console.Write(": ");
                    string i = Console.ReadLine();
                    x.ReturnValues.Push(new List<ReturnValue>(new ReturnValue[]{ Ascii ? i.ToCharArray()[0] : BigInteger.Parse(i) }));
                    return x;
                }))
            });
        }

        #region Constructors
        /// <summary>
        /// Creates a new OpInfo that does nothing when called.
        /// </summary>
        /// <param name="Name"></param>
        public OpInfo(char Name)
        {
            this.Name = Name;
            ParamCount = 0;
            Call = new Func<State, State>(x => x);
        }

        /// <summary>
        /// Creates a new OpInfo with the specified function.
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="ParamCount">How many parameters to wait for.</param>
        /// <param name="Call">The function to be called.</param>
        public OpInfo(char Name, int ParamCount, Func<State, State> Call)
        {
            this.Name = Name;
            this.ParamCount = ParamCount;
            this.Call = Call;
        }
        #endregion
    }

    /// <summary>
                              /// A class used to hold characters of a Defunc program, as well as other control codes.
                              /// </summary>
    public struct ExtendedChar
    {
        /// <summary>
        /// The numeric value this function should return.
        /// </summary>
        public BigInteger? Number { get; set; }
        /// <summary>
        /// The character of the program.
        /// </summary>
        public char Value { get; set; }

        public static implicit operator ExtendedChar(char c) => new ExtendedChar(c);
        public static explicit operator char(ExtendedChar x) => x.Value;

        public override bool Equals(object o)
        {
            if (o.GetType() != GetType()) return false;
            ExtendedChar other = (ExtendedChar)o;
            return (Number != null && other.Number != null) ? Number == other.Number : Value == other.Value;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        #region Constructors
        private ExtendedChar(char Value)
        {
            this.Value = Value;
            Number = null;
        }

        /// <summary>
        /// Creates an ExtendedChar which holds a number.
        /// </summary>
        /// <param name="Number"></param>
        public ExtendedChar(BigInteger Number)
        {
            Value = '0';
            this.Number = Number;
        }
        #endregion
    }

    /// <summary>
    /// A value returned by a Defunc function.
    /// </summary>
    public class ReturnValue
    {
        /// <summary>
        /// The main value of this ReturnValue.
        /// </summary>
        public BigInteger Value { get; set; }

        public static implicit operator ReturnValue(BigInteger b) => new ReturnValue(b);
        public static implicit operator ReturnValue(int b) => new ReturnValue(b);
        public static explicit operator BigInteger(ReturnValue r) => r.Value;
        public static explicit operator char(ReturnValue r) => (char)r.Value;

        public static ReturnValue operator +(ReturnValue a, ReturnValue b) => a.Value + b.Value;

        private ReturnValue(BigInteger Value)
        {
            this.Value = Value;
        }
    }

    /// <summary>
    /// An exception used when a Defunc program has too few or too many arguments.
    /// </summary>
    public class ArgumentCountException : Exception
    {
        
    }

    public static class Extensions
    {
        /// <summary>
        /// Gets the first OpInfo from the input, or a default one if none is there.
        /// </summary>
        /// <param name="infos">Input to search</param>
        /// <returns></returns>
        public static OpInfo GetFunction(this IEnumerable<OpInfo> infos)
        {
            if (infos.ToArray().Length > 0) return infos.First();
            else return new OpInfo(' ', 0, new Func<State, State>(x => throw new ArgumentException("That function was not defined.")));
        }
    }
}
