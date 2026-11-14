// LLVM IR Virtual Machine & Workbench - C# Console Edition
// Target: .NET 6 or later
// Usage:
//   LLVMVM.exe input.ll output.txt
//   dotnet run -- input.ll output.txt
//
// Commands in interactive mode:
//   run, step, break <line>, entry <function|auto>, regs, memory,
//   state, reset, clear, help, quit

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.BackgroundColor = ConsoleColor.White;
        Console.ForegroundColor = ConsoleColor.Black;
        Console.Clear();
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length < 1)
        {
            Console.WriteLine("LLVM IR Virtual Machine v1.4");
            Console.WriteLine("Usage: LLVMVM <input.ll> [output.txt]");
            Console.WriteLine("Example: LLVMVM factorial.ll result.txt");
            return 1;
        }

        string inputPath = args[0];
        string outputPath = args.Length >= 2 ? args[1] : Path.ChangeExtension(inputPath, ".out.txt");

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input file not found: {inputPath}");
            return 2;
        }

        try
        {
            string source = File.ReadAllText(inputPath);
            using var output = new StreamWriter(outputPath, false);
            var vm = new LLVMVirtualMachine(output);
            vm.LoadSource(source, Path.GetFileName(inputPath));

            Console.WriteLine($"LLVM IR Virtual Machine v1.4");
            Console.WriteLine($"Input : {Path.GetFullPath(inputPath)}");
            Console.WriteLine($"Output: {Path.GetFullPath(outputPath)}");
            Console.WriteLine();
            Console.WriteLine("Type 'help' for commands.");

            if (Console.IsInputRedirected)
            {
                vm.Initialize("auto");
                vm.Run();
                vm.WriteFinalState();
            }
            else
            {
                InteractiveLoop(vm);
            }

            output.Flush();
            Console.WriteLine();
            Console.WriteLine($"Output written to: {Path.GetFullPath(outputPath)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Fatal error: " + ex.Message);
            return 3;
        }
    }

    private static void InteractiveLoop(LLVMVirtualMachine vm)
    {
        while (true)
        {
            Console.Write("LLVMVM> ");
            string? line = Console.ReadLine();
            if (line == null) break;

            string[] parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            string command = parts[0].ToLowerInvariant();
            string argument = parts.Length > 1 ? parts[1].Trim() : "";

            switch (command)
            {
                case "run":
                    if (!vm.IsInitialized || vm.IsHalted) vm.Initialize(vm.EntryName);
                    vm.Run();
                    vm.WriteFinalState();
                    break;
                case "step":
                    if (!vm.IsInitialized || vm.IsHalted) vm.Initialize(vm.EntryName);
                    vm.Step();
                    vm.WriteState();
                    break;
                case "break":
                case "bp":
                    if (int.TryParse(argument, out int bpLine) && bpLine > 0)
                    {
                        vm.ToggleBreakpoint(bpLine);
                    }
                    else Console.WriteLine("Usage: break <source-line>");
                    break;
                case "entry":
                    if (argument.Length == 0)
                        Console.WriteLine("Entry: " + vm.EntryName);
                    else
                    {
                        vm.EntryName = argument;
                        Console.WriteLine("Entry function set to: " + argument);
                    }
                    break;
                case "regs":
                    vm.WriteRegisters();
                    break;
                case "memory":
                case "mem":
                    vm.WriteMemory();
                    break;
                case "state":
                    vm.WriteState();
                    break;
                case "reset":
                    vm.ResetExecution();
                    break;
                case "clear":
                    Console.Clear();
                    break;
                case "help":
                    PrintHelp();
                    break;
                case "quit":
                case "exit":
                    return;
                default:
                    Console.WriteLine("Unknown command. Type 'help'.");
                    break;
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  run              Execute until halt or breakpoint");
        Console.WriteLine("  step             Execute one LLVM instruction");
        Console.WriteLine("  break <line>     Toggle a breakpoint at a source line");
        Console.WriteLine("  entry <name>     Select entry function (use auto for automatic)");
        Console.WriteLine("  regs             Display current SSA registers");
        Console.WriteLine("  memory           Display allocated memory");
        Console.WriteLine("  state            Display VM state");
        Console.WriteLine("  reset            Reset execution state");
        Console.WriteLine("  clear            Clear the screen");
        Console.WriteLine("  help             Display this help");
        Console.WriteLine("  quit             Exit");
    }
}

internal sealed class Instruction
{
    public string Text { get; init; } = "";
    public int LineNumber { get; init; }
}

internal sealed class BasicBlock
{
    public string Name { get; init; } = "";
    public List<Instruction> Instructions { get; } = new();
}

internal sealed class FunctionInfo
{
    public string Name { get; init; } = "";
    public List<string> Arguments { get; } = new();
    public Dictionary<string, BasicBlock> Blocks { get; } = new(StringComparer.Ordinal);
    public string EntryBlock { get; set; } = "";
}

internal sealed class StackFrame
{
    public string FunctionName { get; init; } = "";
    public string BlockName { get; set; } = "";
    public int InstructionPointer { get; set; }
    public Dictionary<string, object> Registers { get; } = new(StringComparer.Ordinal);
    public string? ReturnDestination { get; init; }
    public string? PreviousBlock { get; set; }
}

internal sealed class LLVMVirtualMachine
{
    private readonly TextWriter output;
    private readonly Dictionary<string, FunctionInfo> functions = new(StringComparer.Ordinal);
    private readonly List<StackFrame> callStack = new();
    private readonly Dictionary<string, object> memory = new(StringComparer.Ordinal);
    private readonly HashSet<int> breakpoints = new();

    private int memoryCounter = 0x1000;
    private int cycles;
    private bool halted;
    private bool initialized;
    private string documentName = "Untitled.ll";

    public string EntryName { get; set; } = "auto";
    public bool IsInitialized => initialized;
    public bool IsHalted => halted;

    public LLVMVirtualMachine(TextWriter writer)
    {
        output = writer;
    }

    private void Log(string message, string type = "info")
    {
        string prefix = type switch
        {
            "error" => "[ERROR] ",
            "stdout" => "[STDOUT] ",
            "system" => "[VM] ",
            "warn" => "[WARN] ",
            _ => ""
        };

        string formatted = prefix + message;
        Console.WriteLine(formatted);
        output.WriteLine(formatted);
        output.Flush();
    }

    public void LoadSource(string source, string name)
    {
        documentName = name;
        Parse(source);
        Log($"Loaded LLVM assembly file: {name}", "system");
        Log($"Functions found: {string.Join(", ", functions.Keys.Select(x => "@" + x))}");
    }

    private void Parse(string source)
    {
        functions.Clear();
        FunctionInfo? currentFunction = null;
        BasicBlock? currentBlock = null;
        string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i];
            string stripped = raw.Split(';')[0].Trim();
            if (stripped.Length == 0) continue;

            Match fnMatch = Regex.Match(stripped, @"^define\b.*?@([\w\d_.$-]+)\s*\(([^)]*)\)");
            if (fnMatch.Success)
            {
                currentFunction = new FunctionInfo { Name = fnMatch.Groups[1].Value };
                string argsRaw = fnMatch.Groups[2].Value.Trim();
                if (argsRaw.Length > 0)
                {
                    foreach (string arg in argsRaw.Split(','))
                    {
                        Match argMatch = Regex.Match(arg.Trim(), @"(%[\w\d_.$-]+)\s*$");
                        if (argMatch.Success) currentFunction.Arguments.Add(argMatch.Groups[1].Value);
                    }
                }
                functions[currentFunction.Name] = currentFunction;
                currentBlock = null;
                continue;
            }

            if (stripped.StartsWith("}", StringComparison.Ordinal))
            {
                currentFunction = null;
                currentBlock = null;
                continue;
            }

            Match labelMatch = Regex.Match(stripped, @"^([\w\d_.$-]+):$");
            if (labelMatch.Success && currentFunction != null)
            {
                currentBlock = new BasicBlock { Name = labelMatch.Groups[1].Value };
                currentFunction.Blocks[currentBlock.Name] = currentBlock;
                if (currentFunction.EntryBlock.Length == 0)
                    currentFunction.EntryBlock = currentBlock.Name;
                continue;
            }

            if (currentFunction != null)
            {
                if (currentBlock == null)
                {
                    currentBlock = new BasicBlock { Name = "entry" };
                    currentFunction.Blocks["entry"] = currentBlock;
                    currentFunction.EntryBlock = "entry";
                }
                currentBlock.Instructions.Add(new Instruction { Text = stripped, LineNumber = i + 1 });
            }
        }
    }

    private string? ResolveEntry(string requested)
    {
        if (requested != "auto" && functions.ContainsKey(requested)) return requested;
        if (functions.ContainsKey("main")) return "main";
        if (functions.ContainsKey("_main")) return "_main";
        return functions.Keys.FirstOrDefault();
    }

    public bool Initialize(string requestedEntry)
    {
        callStack.Clear();
        memory.Clear();
        memoryCounter = 0x1000;
        cycles = 0;
        halted = false;
        initialized = false;

        string? entry = ResolveEntry(requestedEntry);
        if (entry == null)
        {
            Log("No executable function found in document.", "error");
            return false;
        }

        FunctionInfo fn = functions[entry];
        if (fn.EntryBlock.Length == 0 ||
            !fn.Blocks.TryGetValue(fn.EntryBlock, out BasicBlock? block) ||
            block.Instructions.Count == 0)
        {
            Log($"Function @{entry} has no instructions to execute.", "error");
            return false;
        }

        callStack.Add(new StackFrame { FunctionName = entry, BlockName = fn.EntryBlock });
        EntryName = requestedEntry;
        initialized = true;
        Log($"Initialized. Entry point: @{entry}()", "system");
        return true;
    }

    public void ResetExecution()
    {
        callStack.Clear();
        memory.Clear();
        memoryCounter = 0x1000;
        cycles = 0;
        halted = false;
        initialized = false;
        Log("Virtual Machine reset.", "system");
    }

    public void ToggleBreakpoint(int line)
    {
        if (!breakpoints.Add(line))
        {
            breakpoints.Remove(line);
            Console.WriteLine($"Breakpoint removed from line {line}.");
            output.WriteLine($"Breakpoint removed from line {line}.");
        }
        else
        {
            Console.WriteLine($"Breakpoint set at line {line}.");
            output.WriteLine($"Breakpoint set at line {line}.");
        }
    }

    private Instruction? CurrentInstruction()
    {
        if (halted || callStack.Count == 0) return null;
        StackFrame frame = callStack[^1];
        if (!functions.TryGetValue(frame.FunctionName, out FunctionInfo? fn)) return null;
        if (!fn.Blocks.TryGetValue(frame.BlockName, out BasicBlock? block)) return null;
        if (frame.InstructionPointer < 0 || frame.InstructionPointer >= block.Instructions.Count) return null;
        return block.Instructions[frame.InstructionPointer];
    }

    public void Step()
    {
        if (!initialized)
        {
            Log("VM is not initialized. Use run or step to initialize it.", "warn");
            return;
        }
        if (halted || callStack.Count == 0) return;

        StackFrame frame = callStack[^1];
        if (!functions.TryGetValue(frame.FunctionName, out FunctionInfo? fn))
        {
            Log($"Missing function @{frame.FunctionName}.", "error");
            halted = true;
            return;
        }

        if (!fn.Blocks.TryGetValue(frame.BlockName, out BasicBlock? block) ||
            frame.InstructionPointer >= block.Instructions.Count)
        {
            Log($"Reached end of block '{frame.BlockName}' without return or branch.", "error");
            halted = true;
            return;
        }

        Instruction instruction = block.Instructions[frame.InstructionPointer];
        cycles++;
        try
        {
            ExecuteInstruction(instruction.Text, frame);
        }
        catch (Exception ex)
        {
            Log($"Exception at line {instruction.LineNumber}: {ex.Message}", "error");
            halted = true;
        }
    }

    public void Run()
    {
        if (!initialized)
        {
            if (!Initialize(EntryName)) return;
        }

        while (!halted && callStack.Count > 0)
        {
            Instruction? next = CurrentInstruction();
            if (next != null && breakpoints.Contains(next.LineNumber))
            {
                Log($"Hit breakpoint at line {next.LineNumber}.", "system");
                break;
            }

            Step();
        }

        if (halted) Log($"Execution finished after {cycles} cycles.", "system");
    }

    private object ResolveOperand(string operand, StackFrame frame)
    {
        string op = operand.Trim();
        op = Regex.Replace(op, @"^(?:i\d+|ptr|[\w\d*]+\s+)", "").Trim();

        if (op.StartsWith('%'))
            return frame.Registers.TryGetValue(op, out object? value) ? value : 0;

        if (op.StartsWith("@"))
            return op;

        if (long.TryParse(op, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
            return unchecked((int)number);

        return 0;
    }

    private static int AsInt(object value)
    {
        if (value is int i) return i;
        if (value is long l) return unchecked((int)l);
        if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out int parsed)) return parsed;
        return 0;
    }

    private void ExecuteInstruction(string instruction, StackFrame frame)
    {
        Match match;

        // ALLOCA
        match = Regex.Match(instruction, @"^(%\w[\w\d_.$-]*)\s*=\s*alloca\s+([\w\d]+)");
        if (match.Success)
        {
            string destination = match.Groups[1].Value;
            string address = "0x" + (memoryCounter++).ToString("X", CultureInfo.InvariantCulture);
            memory[address] = 0;
            frame.Registers[destination] = address;
            frame.InstructionPointer++;
            return;
        }

        // STORE
        match = Regex.Match(instruction, @"^store\s+[\w\d*]+\s+([^,]+),\s*(?:ptr|[\w\d*]+)\s+(%\w[\w\d_.$-]*)");
        if (match.Success)
        {
            object value = ResolveOperand(match.Groups[1].Value, frame);
            string pointerName = match.Groups[2].Value;
            if (!frame.Registers.TryGetValue(pointerName, out object? ptr) ||
                ptr is not string address || !memory.ContainsKey(address))
                throw new InvalidOperationException($"Invalid pointer store to {pointerName}");
            memory[address] = value;
            frame.InstructionPointer++;
            return;
        }

        // LOAD
        match = Regex.Match(instruction, @"^(%\w[\w\d_.$-]*)\s*=\s*load\s+[\w\d*]+,\s*(?:ptr|[\w\d*]+)\s+(%\w[\w\d_.$-]*)");
        if (match.Success)
        {
            string destination = match.Groups[1].Value;
            string pointerName = match.Groups[2].Value;
            if (!frame.Registers.TryGetValue(pointerName, out object? ptr) ||
                ptr is not string address || !memory.TryGetValue(address, out object? value))
                throw new InvalidOperationException($"Segmentation fault: invalid pointer load {pointerName}");
            frame.Registers[destination] = value;
            frame.InstructionPointer++;
            return;
        }

        // INTEGER ARITHMETIC AND BITWISE OPERATIONS
        match = Regex.Match(instruction,
            @"^(%\w[\w\d_.$-]*)\s*=\s*(add|sub|mul|sdiv|udiv|srem|urem|and|or|xor|shl|ashr)\s+[\w\d*]+\s+([^,]+),\s*(.+)$");
        if (match.Success)
        {
            string destination = match.Groups[1].Value;
            string op = match.Groups[2].Value;
            int a = AsInt(ResolveOperand(match.Groups[3].Value, frame));
            int b = AsInt(ResolveOperand(match.Groups[4].Value, frame));
            int result = op switch
            {
                "add" => unchecked(a + b),
                "sub" => unchecked(a - b),
                "mul" => unchecked(a * b),
                "sdiv" or "udiv" => b == 0 ? 0 : a / b,
                "srem" or "urem" => b == 0 ? 0 : a % b,
                "and" => a & b,
                "or" => a | b,
                "xor" => a ^ b,
                "shl" => a << (b & 31),
                "ashr" => a >> (b & 31),
                _ => 0
            };
            frame.Registers[destination] = result;
            frame.InstructionPointer++;
            return;
        }

        // ICMP
        match = Regex.Match(instruction,
            @"^(%\w[\w\d_.$-]*)\s*=\s*icmp\s+(\w+)\s+[\w\d*]+\s+([^,]+),\s*(.+)$");
        if (match.Success)
        {
            string destination = match.Groups[1].Value;
            string condition = match.Groups[2].Value;
            int a = AsInt(ResolveOperand(match.Groups[3].Value, frame));
            int b = AsInt(ResolveOperand(match.Groups[4].Value, frame));
            bool result = condition switch
            {
                "eq" => a == b,
                "ne" => a != b,
                "sgt" or "ugt" => a > b,
                "sge" or "uge" => a >= b,
                "slt" or "ult" => a < b,
                "sle" or "ule" => a <= b,
                _ => false
            };
            frame.Registers[destination] = result ? 1 : 0;
            frame.InstructionPointer++;
            return;
        }

        // CONDITIONAL BRANCH
        match = Regex.Match(instruction,
            @"^br\s+i1\s+(%\w[\w\d_.$-]*),\s*label\s+%(?:label\.)?([\w\d_.$-]+),\s*label\s+%(?:label\.)?([\w\d_.$-]+)");
        if (match.Success)
        {
            int condition = frame.Registers.TryGetValue(match.Groups[1].Value, out object? value) ? AsInt(value) : 0;
            frame.PreviousBlock = frame.BlockName;
            frame.BlockName = condition != 0 ? match.Groups[2].Value : match.Groups[3].Value;
            frame.InstructionPointer = 0;
            return;
        }

        // UNCONDITIONAL BRANCH
        match = Regex.Match(instruction, @"^br\s+label\s+%(?:label\.)?([\w\d_.$-]+)");
        if (match.Success)
        {
            frame.PreviousBlock = frame.BlockName;
            frame.BlockName = match.Groups[1].Value;
            frame.InstructionPointer = 0;
            return;
        }

        // FUNCTION CALL
        match = Regex.Match(instruction,
            @"^(?:(%\w[\w\d_.$-]*)\s*=\s*)?call\s+[\w\d*]+\s+@([\w\d_.$-]+)\s*\(([^)]*)\)");
        if (match.Success)
        {
            string? returnDestination = match.Groups[1].Success ? match.Groups[1].Value : null;
            string functionName = match.Groups[2].Value;
            string argsText = match.Groups[3].Value.Trim();

            List<object> arguments = argsText.Length == 0
                ? new List<object>()
                : argsText.Split(',').Select(x => ResolveOperand(x, frame)).ToList();

            if (functionName == "print" || functionName == "println")
            {
                string printed = string.Join(", ", arguments.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)));
                Log(printed, "stdout");
                frame.InstructionPointer++;
                return;
            }

            if (!functions.TryGetValue(functionName, out FunctionInfo? target))
                throw new InvalidOperationException($"Undefined function @{functionName}");

            var newFrame = new StackFrame
            {
                FunctionName = functionName,
                BlockName = target.EntryBlock,
                ReturnDestination = returnDestination
            };

            for (int i = 0; i < target.Arguments.Count; i++)
                newFrame.Registers[target.Arguments[i]] = i < arguments.Count ? arguments[i] : 0;

            frame.InstructionPointer++;
            callStack.Add(newFrame);
            return;
        }

        // RETURN
        match = Regex.Match(instruction, @"^ret\s+(?:void|[\w\d*]+\s+(.+))$");
        if (match.Success)
        {
            object? returnValue = match.Groups[1].Success
                ? ResolveOperand(match.Groups[1].Value, frame)
                : null;

            callStack.RemoveAt(callStack.Count - 1);
            if (callStack.Count > 0)
            {
                StackFrame caller = callStack[^1];
                if (!string.IsNullOrEmpty(frame.ReturnDestination) && returnValue != null)
                    caller.Registers[frame.ReturnDestination] = returnValue;
            }
            else
            {
                Log($"Process exited with code: {(returnValue == null ? 0 : AsInt(returnValue))}", "system");
                halted = true;
            }
            return;
        }

        // Ignore unsupported instructions, matching the original workbench behavior.
        frame.InstructionPointer++;
    }

    public void WriteRegisters()
    {
        Console.WriteLine("Registers (SSA):");
        output.WriteLine("Registers (SSA):");
        if (callStack.Count == 0)
        {
            WriteBoth("  No active frame");
            return;
        }

        StackFrame frame = callStack[^1];
        WriteBoth($"  Frame: @{frame.FunctionName}");
        foreach (var item in frame.Registers)
            WriteBoth($"  {item.Key,-18} = {item.Value}");
    }

    public void WriteMemory()
    {
        WriteBoth("Memory allocations:");
        if (memory.Count == 0)
        {
            WriteBoth("  No memory allocated");
            return;
        }
        foreach (var item in memory)
            WriteBoth($"  {item.Key,-12} = {item.Value}");
    }

    public void WriteState()
    {
        Instruction? current = CurrentInstruction();
        WriteBoth($"Cycles: {cycles}");
        WriteBoth($"Stack depth: {callStack.Count}");
        WriteBoth($"Instruction: {(current == null ? (halted ? "Halted" : "Idle") : $"line {current.LineNumber}: {current.Text}")}");
        WriteRegisters();
        WriteMemory();
    }

    public void WriteFinalState()
    {
        WriteBoth("----- FINAL VM STATE -----");
        WriteState();
        WriteBoth("--------------------------");
    }

    private void WriteBoth(string line)
    {
        Console.WriteLine(line);
        output.WriteLine(line);
        output.Flush();
    }
}
