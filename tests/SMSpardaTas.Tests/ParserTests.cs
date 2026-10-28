namespace SMSpardaTas.Tests;

public class ParserTests
{
	private static TasScript Parse(string text) => Parser.Parse(text, "test.tas");

	private static ScriptException ParseError(string text) => Assert.Throws<ScriptException>(() => Parse(text));

	[Fact]
	public void Parses_button_combinations_and_frame_counts()
	{
		TasScript script = Parse("Right+B 60\nright+b+a\n. 10\nnone 2\nwait 5");

		Assert.Equal(
			[
				new InputStatement(1, Buttons.Right | Buttons.B, 60),
				new InputStatement(2, Buttons.Right | Buttons.B | Buttons.A, 1),
				new InputStatement(3, Buttons.None, 10),
				new InputStatement(4, Buttons.None, 2),
				new InputStatement(5, Buttons.None, 5)
			],
			script.Statements);
	}

	[Fact]
	public void Ignores_comments_and_blank_lines()
	{
		TasScript script = Parse("# header\n\n  A 2 ; jump\n; done");

		Assert.Equal([new InputStatement(3, Buttons.A, 2)], script.Statements);
	}

	[Fact]
	public void Parses_nested_repeat_blocks()
	{
		TasScript script = Parse("repeat 2 {\n  Y 1\n  repeat 3 {\n    . 1\n  }\n}");

		RepeatStatement outer = Assert.IsType<RepeatStatement>(Assert.Single(script.Statements));
		Assert.Equal(2, outer.Count);
		Assert.Equal(2, outer.Body.Count);
		RepeatStatement inner = Assert.IsType<RepeatStatement>(outer.Body[1]);
		Assert.Equal(3, inner.Count);
	}

	[Fact]
	public void Parses_wait_until_with_hold_and_timeout()
	{
		TasScript script = Parse("wait_until game_state == 8 and samus_x >= $0100 hold Left+B timeout 0");

		WaitUntilStatement wait = Assert.IsType<WaitUntilStatement>(Assert.Single(script.Statements));
		Assert.Equal("game_state == 8 and samus_x >= $0100", wait.ConditionText);
		Assert.Equal(Buttons.Left | Buttons.B, wait.Hold);
		Assert.Equal(0, wait.Timeout);
		BinaryExpr and = Assert.IsType<BinaryExpr>(wait.Condition);
		Assert.Equal("and", and.Op);
		Assert.Equal(new BinaryExpr(">=", new MemoryExpr(0x7E0AF6, RamType.U16, "samus_x"), new NumberExpr(0x100)), and.Right);
	}

	[Fact]
	public void Wait_until_defaults_to_no_buttons_and_the_default_timeout()
	{
		WaitUntilStatement wait = Assert.IsType<WaitUntilStatement>(Assert.Single(Parse("wait_until room != 0").Statements));

		Assert.Equal(Buttons.None, wait.Hold);
		Assert.Equal(Parser.DefaultTimeout, wait.Timeout);
	}

	[Fact]
	public void Parses_assert_with_message_bitmask_not_and_parentheses()
	{
		TasScript script = Parse("assert not (input & $80 == $80 or room == 1) \"no jump\"");

		AssertStatement assert = Assert.IsType<AssertStatement>(Assert.Single(script.Statements));
		Assert.Equal("no jump", assert.Message);
		NotExpr not = Assert.IsType<NotExpr>(assert.Condition);
		BinaryExpr or = Assert.IsType<BinaryExpr>(not.Operand);
		Assert.Equal(
			new BinaryExpr("==", new BinaryExpr("&", new MemoryExpr(0x7E008B, RamType.U16, "input"), new NumberExpr(0x80)), new NumberExpr(0x80)),
			or.Left);
	}

	[Fact]
	public void Parses_var_declarations_raw_memory_and_negative_numbers()
	{
		TasScript script = Parse("@var speed $0B2E s16\n@var flag $7F:1234 u8\nassert speed == -1 and flag == 0 and u8[$7E:0AF6] > 3");

		Assert.Equal(new RamSymbol("speed", 0x7E0B2E, RamType.S16, "Declared in script"), script.Symbols["speed"]);
		Assert.Equal(0x7F1234, script.Symbols["FLAG"].Address);
		AssertStatement assert = Assert.IsType<AssertStatement>(Assert.Single(script.Statements));
		BinaryExpr outer = Assert.IsType<BinaryExpr>(assert.Condition);
		BinaryExpr inner = Assert.IsType<BinaryExpr>(outer.Left);
		Assert.Equal(new BinaryExpr("==", new MemoryExpr(0x7E0B2E, RamType.S16, "speed"), new NumberExpr(-1)), inner.Left);
		Assert.Equal(new BinaryExpr(">", new MemoryExpr(0x7E0AF6, RamType.U8, "u8[$7E:0AF6]"), new NumberExpr(3)), outer.Right);
	}

	[Fact]
	public void Parses_savestate_file_and_slot()
	{
		Assert.Equal(new SavestateDirective(1, "states/start.State", null), Parse("@savestate \"states/start.State\"\nA").Savestate);
		Assert.Equal(new SavestateDirective(2, null, 3), Parse("# c\n@savestate slot 3").Savestate);
	}

	[Fact]
	public void Parses_label()
	{
		Assert.Equal([new LabelStatement(1, "Kraid \"fight\"")], Parse("label \"Kraid \\\"fight\\\"\"").Statements);
	}

	[Theory]
	[InlineData("Right+Q 3", 1, 7, "unknown button 'Q'")]
	[InlineData("A+A", 1, 3, "listed twice")]
	[InlineData("A 0", 1, 3, "at least 1")]
	[InlineData("A 3 4", 1, 5, "expected the end of the line")]
	[InlineData("wait_until room = 3", 1, 17, "use '=='")]
	[InlineData("wait_until room", 1, 16, "expected a comparison")]
	[InlineData("wait_until nope == 3", 1, 12, "unknown variable 'nope'")]
	[InlineData("wait_until room == 3 hold Q", 1, 27, "unknown button")]
	[InlineData("wait_until room == 3 sometimes", 1, 22, "expected 'hold', 'timeout'")]
	[InlineData("repeat 2 {\nA", 2, 1, "missing '}'")]
	[InlineData("}", 1, 1, "without a matching repeat")]
	[InlineData("repeat 2\nA", 1, 9, "'{'")]
	[InlineData("A\n@savestate slot 1", 2, 1, "before any input")]
	[InlineData("@savestate slot 1\n@savestate slot 2", 2, 1, "only be used once")]
	[InlineData("@savestate slot 0", 1, 17, "at least 1")]
	[InlineData("@savestate slot 10", 1, 17, "slots are 1-9")]
	[InlineData("repeat 1 {\n@var foo $10\n}", 2, 1, "not allowed inside a repeat")]
	[InlineData("@var room $10", 1, 6, "already defined")]
	[InlineData("@var left $10", 1, 6, "reserved")]
	[InlineData("@var foo $800000", 1, 10, "outside WRAM")]
	[InlineData("@var foo $7F:FFFF u16", 1, 10, "past the end of WRAM")]
	[InlineData("@var foo $10 u32", 1, 14, "unknown type")]
	[InlineData("@bogus", 1, 1, "unknown directive")]
	[InlineData("label \"open", 1, 7, "unterminated string")]
	[InlineData("A 12abc", 1, 3, "invalid number")]
	public void Reports_errors_with_line_and_column(string text, int line, int column, string message)
	{
		ScriptException error = ParseError(text);

		Assert.Equal((line, column), (error.Line, error.Column));
		Assert.Contains(message, error.Detail);
		Assert.StartsWith($"test.tas({line},{column}): error: ", error.Message);
	}
}
