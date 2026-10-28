namespace SMSpardaTas.Tests;

public class ControllerTests
{
	// The layout from the request: shot Y, dash X, item cancel B, everything else default.
	private const string PlayerLayout = """
		@controller {
		    shot        = Y
		    jump        = A
		    dash        = X
		    item_select = Select
		    item_cancel = B
		    angle_up    = R
		    angle_down  = L
		}

		""";

	private static TasScript Parse(string text) => Parser.Parse(text, "test.tas");

	private static ControllerLayout Layout(string block) => Parse(block).Controller!.Layout;

	[Fact]
	public void Scripts_without_a_block_use_the_default_layout()
	{
		Assert.Null(Parse("A").Controller);
		Assert.Equal(Buttons.X | Buttons.B, ControllerLayout.Default.ToGame(Buttons.X | Buttons.B));
	}

	[Theory]
	[InlineData(Buttons.Y, Buttons.X)]
	[InlineData(Buttons.X, Buttons.B)]
	[InlineData(Buttons.B, Buttons.Y)]
	[InlineData(Buttons.A, Buttons.A)]
	[InlineData(Buttons.Select | Buttons.L | Buttons.R, Buttons.Select | Buttons.L | Buttons.R)]
	[InlineData(Buttons.Left | Buttons.X | Buttons.A | Buttons.Y, Buttons.Left | Buttons.B | Buttons.A | Buttons.X)]
	[InlineData(Buttons.Up | Buttons.Down | Buttons.Left | Buttons.Right | Buttons.Start, Buttons.Up | Buttons.Down | Buttons.Left | Buttons.Right | Buttons.Start)]
	public void Translates_the_players_buttons_to_the_games_default_buttons(Buttons player, Buttons game)
	{
		ControllerLayout layout = Layout(PlayerLayout);

		Assert.Equal(game, layout.ToGame(player));
		Assert.Equal(player, layout.ToPlayer(game));
	}

	[Fact]
	public void Actions_left_out_keep_their_default_button()
	{
		ControllerLayout layout = Layout("@controller {\n  shot = Y\n  item_cancel = X\n}");

		Assert.Equal(Buttons.X, layout.ToGame(Buttons.Y));
		Assert.Equal(Buttons.Y, layout.ToGame(Buttons.X));
		Assert.Equal(Buttons.B, layout.ToGame(Buttons.B));
	}

	[Fact]
	public void Accepts_case_insensitive_names_comments_and_blank_lines()
	{
		ControllerLayout layout = Layout("@controller {   # mine\n\n  SHOT = y   ; shoot with Y\n  Item_Cancel = x\n}");

		Assert.Equal(Buttons.X, layout.ToGame(Buttons.Y));
	}

	[Fact]
	public void Can_follow_the_savestate_and_vars()
	{
		TasScript script = Parse("@savestate slot 1\n@var foo $10\n" + PlayerLayout + "Y 2");

		Assert.NotNull(script.Controller);
		Assert.Equal(3, script.Controller.Line);
	}

	[Theory]
	[InlineData("@controller {\n  fire = Y\n}", 2, 3, "unknown action 'fire'")]
	[InlineData("@controller {\n  shot = Y\n  shot = B\n}", 3, 3, "'shot' is set twice")]
	[InlineData("@controller {\n  shot Y\n}", 2, 8, "expected '=' after 'shot'")]
	[InlineData("@controller {\n  shot = Q\n}", 2, 10, "unknown button 'Q'")]
	[InlineData("@controller {\n  shot = Up\n}", 2, 10, "the d-pad and Start are fixed")]
	[InlineData("@controller {\n  dash = Start\n}", 2, 10, "the d-pad and Start are fixed")]
	[InlineData("@controller {\n  shot = Y\n  dash = Y\n}", 3, 10, "Y is used for both shot and dash")]
	[InlineData("@controller {\n  shot = A\n}", 2, 10, "A is used for both shot and jump (its default)")]
	[InlineData("@controller {\n  shot = Y\n", 3, 1, "missing '}'")]
	[InlineData("@controller\n  shot = Y\n}", 1, 12, "'{' after @controller")]
	[InlineData("A\n@controller {\n}", 2, 1, "before any input")]
	[InlineData("@controller {\n}\n@controller {\n}", 3, 1, "only be used once")]
	[InlineData("repeat 1 {\n@controller {\n}\n}", 2, 1, "not allowed inside a repeat")]
	public void Reports_layout_errors_with_line_and_column(string text, int line, int column, string message)
	{
		ScriptException error = Assert.Throws<ScriptException>(() => Parse(text));

		Assert.Equal((line, column), (error.Line, error.Column));
		Assert.Contains(message, error.Detail);
	}

	[Fact]
	public void Compiles_inputs_and_holds_to_the_games_buttons_but_shows_the_players()
	{
		GeneratorResult result = LuaGenerator.Generate(
			Parse(PlayerLayout + "Left+X 30\nwait_until room == 1 hold Right+B"),
			new GeneratorOptions());

		Assert.Contains("{ \"input\", 10, 0x8200, 30 }, -- Left+X", result.Lua);
		Assert.Contains("[0x8200] = \"Left+X\"", result.Lua);
		Assert.Contains("0x4100, 3600 }", result.Lua);
		Assert.Contains("[0x4100] = \"Right+B\"", result.Lua);
	}

	[Fact]
	public void Checks_the_games_layout_before_the_first_frame()
	{
		GeneratorResult result = LuaGenerator.Generate(Parse(PlayerLayout + "A"), new GeneratorOptions());

		Assert.Equal(2, result.Steps);
		Assert.Contains("{ \"assert\", 1, 1, \"@controller translates inputs", result.Lua);
		Assert.Contains("(rd_u16(0x7E09B2) == 0x40)", result.Lua);
		Assert.Contains("(rd_u16(0x7E09B6) == 0x8000)", result.Lua);
	}

	[Fact]
	public void Scripts_without_a_block_have_no_layout_check()
	{
		Assert.DoesNotContain("@controller", LuaGenerator.Generate(Parse("A"), new GeneratorOptions()).Lua);
	}
}
