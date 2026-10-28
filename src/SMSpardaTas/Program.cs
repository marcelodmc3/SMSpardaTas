namespace SMSpardaTas;

internal static class Program
{
	private const string Usage = """
		SMSpardaTas - compiles Super Metroid TAS scripts into BizHawk Lua playback scripts

		Usage:
		  SMSpardaTas compile <script.tas> [-o <out.lua>] [--no-hud]
		  SMSpardaTas check <script.tas>
		  SMSpardaTas symbols

		compile   Writes a Lua script (default: <script>.bizhawk.lua next to the script).
		check     Parses the script and prints a summary without writing anything.
		symbols   Lists the built-in RAM variables usable in conditions.
		""";

	private static int Main(string[] args)
	{
		if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
		{
			Console.WriteLine(Usage);
			return args.Length == 0 ? 1 : 0;
		}
		try
		{
			return args[0] switch
			{
				"compile" => Compile(args[1..]),
				"check" => Check(args[1..]),
				"symbols" => Symbols(),
				_ => Fail($"unknown command '{args[0]}'")
			};
		}
		catch (ScriptException e)
		{
			Console.Error.WriteLine(e.Message);
			return 1;
		}
		catch (IOException e)
		{
			Console.Error.WriteLine($"error: {e.Message}");
			return 1;
		}
	}

	private static int Compile(string[] args)
	{
		string? input = null;
		string? output = null;
		bool hud = true;
		for (int i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "-o" or "--output" when i + 1 < args.Length:
					output = args[++i];
					break;
				case "--no-hud":
					hud = false;
					break;
				default:
					if (args[i].StartsWith('-') || input != null)
					{
						return Fail($"unexpected argument '{args[i]}'");
					}
					input = args[i];
					break;
			}
		}
		if (input == null)
		{
			return Fail("missing the script to compile");
		}

		TasScript script = Parser.Parse(File.ReadAllText(input), input);
		string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(input))!;
		GeneratorResult result = LuaGenerator.Generate(script, new GeneratorOptions(hud, baseDirectory));
		output ??= Path.ChangeExtension(input, ".bizhawk.lua");
		File.WriteAllText(output, result.Lua);

		PrintWarnings(result);
		Console.WriteLine($"Wrote {output}");
		PrintSummary(result);
		return 0;
	}

	private static int Check(string[] args)
	{
		if (args.Length != 1)
		{
			return Fail("check takes exactly one script");
		}
		TasScript script = Parser.Parse(File.ReadAllText(args[0]), args[0]);
		GeneratorResult result = LuaGenerator.Generate(script, new GeneratorOptions());
		PrintWarnings(result);
		Console.WriteLine($"{args[0]}: OK");
		PrintSummary(result);
		return 0;
	}

	private static int Symbols()
	{
		foreach (RamSymbol symbol in RamMap.BuiltIn)
		{
			string type = symbol.Type.ToString().ToLowerInvariant();
			Console.WriteLine($"{symbol.Name,-18} {RamMap.FormatAddress(symbol.Address)}  {type,-4} {symbol.Description}");
		}
		return 0;
	}

	private static void PrintSummary(GeneratorResult result)
	{
		string waits = result.Waits == 0 ? "" : $" plus {result.Waits} wait_until step(s)";
		TimeSpan time = TimeSpan.FromSeconds(result.FixedFrames / 60.0988);
		Console.WriteLine($"  {result.Steps} steps, {result.FixedFrames} fixed frames (~{time:mm\\:ss\\.ff} at NTSC 60.1 fps){waits}");
	}

	private static void PrintWarnings(GeneratorResult result)
	{
		foreach (string warning in result.Warnings)
		{
			Console.Error.WriteLine($"warning: {warning}");
		}
	}

	private static int Fail(string message)
	{
		Console.Error.WriteLine($"error: {message}");
		Console.Error.WriteLine("Run 'SMSpardaTas --help' for usage.");
		return 1;
	}
}
