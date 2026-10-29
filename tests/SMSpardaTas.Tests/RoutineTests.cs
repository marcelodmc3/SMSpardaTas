namespace SMSpardaTas.Tests;

public class RoutineTests
{
	// The example from the request: run left for 60 frames alternating angle up and angle down.
	private const string ArmpumpLeft = """
		@routine armpump_left {
		    repeat 30 {
		        Left+B+R
		        Left+B+L
		    }
		}

		""";

	private static TasScript Parse(string text) => Parser.Parse(text, "test.tas");

	private static GeneratorResult Generate(string text) => LuaGenerator.Generate(Parse(text), new GeneratorOptions());

	[Fact]
	public void Declares_routines_in_order_and_calls_them()
	{
		TasScript script = Parse(ArmpumpLeft + "@routine downback {\n  Down 2\n}\ncall armpump_left\ncall DOWNBACK");

		Assert.Equal(["armpump_left", "downback"], script.Routines.Select(r => r.Name));
		Assert.Equal(1, script.Routines[0].Line);
		Assert.Same(script.Routines[0], Assert.IsType<CallStatement>(script.Statements[0]).Routine);
		Assert.Same(script.Routines[1], Assert.IsType<CallStatement>(script.Statements[1]).Routine);
	}

	[Fact]
	public void Routines_can_follow_the_controller_block_savestate_and_vars()
	{
		TasScript script = Parse("@savestate slot 1\n@controller {\n  shot = Y\n  item_cancel = X\n}\n@var foo $10\n" + ArmpumpLeft + "call armpump_left");

		Assert.Single(script.Routines);
		Assert.NotNull(script.Controller);
	}

	[Fact]
	public void Routines_can_call_earlier_routines_and_be_called_from_repeat_blocks()
	{
		TasScript script = Parse("@routine first {\n  A\n}\n@routine second {\n  call first\n  B\n}\nrepeat 2 {\n  call second\n}");

		Assert.IsType<CallStatement>(script.Routines[1].Body[0]);
		Assert.IsType<CallStatement>(Assert.IsType<RepeatStatement>(Assert.Single(script.Statements)).Body[0]);
	}

	[Fact]
	public void A_routine_body_does_not_start_playback()
	{
		// Statements inside a routine aren't playback, so later declarations are still allowed.
		TasScript script = Parse("@routine first {\n  A\n}\n@routine second {\n  B\n}\ncall first");

		Assert.Equal(2, script.Routines.Count);
	}

	[Theory]
	[InlineData("A\n@routine late {\n  B\n}", 2, 1, "before playback starts")]
	[InlineData("@routine first {\n  A\n}\n@controller {\n  shot = Y\n}", 4, 1, "@controller must come before any @routine")]
	[InlineData("@routine first {\n  A\n}\n@routine FIRST {\n  B\n}", 4, 10, "already declared on line 1")]
	[InlineData("@routine wait {\n  A\n}", 1, 10, "reserved")]
	[InlineData("@routine left {\n  A\n}", 1, 10, "reserved")]
	[InlineData("@routine first\n  A\n}", 1, 15, "'{' after the routine name")]
	[InlineData("@routine first {\n  A\n", 3, 1, "missing '}' to close a @routine block")]
	[InlineData("@routine first {\n  @var foo $10\n}", 2, 3, "not allowed inside a @routine block")]
	[InlineData("@routine first {\n  repeat 2 {\n    @savestate slot 1\n  }\n}", 3, 5, "not allowed inside a repeat block")]
	[InlineData("@routine first {\n  call first\n}", 2, 8, "can't call itself")]
	[InlineData("call nowhere", 1, 6, "unknown routine 'nowhere'")]
	[InlineData("@routine first {\n  call second\n}\n@routine second {\n  B\n}", 2, 8, "unknown routine 'second'")]
	[InlineData("call", 1, 5, "expected a routine name")]
	[InlineData("@routine first {\n  A\n}\ncall first 3", 4, 12, "expected the end of the line")]
	public void Reports_routine_errors_with_line_and_column(string text, int line, int column, string message)
	{
		ScriptException error = Assert.Throws<ScriptException>(() => Parse(text));

		Assert.Equal((line, column), (error.Line, error.Column));
		Assert.Contains(message, error.Detail);
	}

	[Fact]
	public void Expands_calls_and_counts_their_frames()
	{
		GeneratorResult result = Generate("@savestate slot 1\n" + ArmpumpLeft + "call armpump_left\nA 5\ncall armpump_left");

		Assert.Equal(125, result.FixedFrames);
		Assert.Equal(121, result.Steps);
		Assert.Empty(result.Warnings);
	}

	[Fact]
	public void Steps_from_a_routine_say_where_it_was_called_from()
	{
		string lua = Generate("@routine inner {\n  A 2\n}\n@routine outer {\n  call inner\n}\nB\ncall outer").Lua;

		Assert.Contains("{ \"input\", 7, 0x8000, 1 }, -- B", lua);
		Assert.Contains("{ \"input\", \"2 in inner, called from line 5 in outer, called from line 8\", 0x80, 2 }, -- A", lua);
	}

	[Fact]
	public void Warns_about_routines_that_are_never_called()
	{
		GeneratorResult result = Generate("@savestate slot 1\n" + ArmpumpLeft + "A");

		Assert.Equal("routine 'armpump_left' (line 2) is never called", Assert.Single(result.Warnings));
	}

	[Fact]
	public void A_routine_called_only_by_another_routine_counts_as_called_when_that_one_is()
	{
		GeneratorResult result = Generate("@savestate slot 1\n@routine first {\n  A\n}\n@routine second {\n  call first\n}\ncall second");

		Assert.Empty(result.Warnings);
	}

	[Fact]
	public void Routines_are_written_in_the_players_layout()
	{
		string lua = Generate("@controller {\n  shot = Y\n  dash = X\n  item_cancel = B\n}\n@routine run {\n  Left+X 3\n}\ncall run").Lua;

		Assert.Contains("0x8200, 3 }, -- Left+X", lua);
	}
}
