namespace SmTas.Tests;

public class LuaGeneratorTests
{
	private static GeneratorResult Generate(string tas, string? baseDirectory = null)
	{
		return LuaGenerator.Generate(Parser.Parse(tas, "test.tas"), new GeneratorOptions(SavestateBaseDirectory: baseDirectory));
	}

	[Fact]
	public void Counts_steps_fixed_frames_and_waits()
	{
		GeneratorResult result = Generate("@savestate slot 1\nA 10\nrepeat 3 {\n  B 2\n  wait_until room == 1\n}");

		Assert.Equal(7, result.Steps);
		Assert.Equal(16, result.FixedFrames);
		Assert.Equal(3, result.Waits);
		Assert.Empty(result.Warnings);
	}

	[Fact]
	public void Warns_when_there_is_no_savestate()
	{
		Assert.Contains("no @savestate", Assert.Single(Generate("A").Warnings));
	}

	[Fact]
	public void Reuses_one_condition_for_repeated_waits()
	{
		string lua = Generate("repeat 50 {\n  wait_until room == 1\n}").Lua;

		Assert.Contains("[1] = {", lua);
		Assert.DoesNotContain("[2] = {", lua);
	}

	[Fact]
	public void Resolves_relative_savestate_paths_against_the_script_folder()
	{
		string folder = Path.Combine(Path.GetTempPath(), "smtas");
		string lua = Generate("@savestate \"states/a.State\"", baseDirectory: folder).Lua;

		string expected = LuaGenerator.LuaString(Path.Combine(folder, "states", "a.State"));
		Assert.Contains($"savestate.load({expected})", lua);
	}

	[Fact]
	public void Loads_savestate_slots_by_number()
	{
		Assert.Contains("local function load_start() savestate.loadslot(1) end", Generate("@savestate slot 1").Lua);
	}

	[Fact]
	public void Rejects_scripts_that_expand_too_far()
	{
		Assert.Throws<ScriptException>(() => Generate("repeat 1000 {\n  repeat 1000 {\n    A\n  }\n}"));
	}

	[Fact]
	public void Escapes_lua_strings()
	{
		Assert.Equal("\"a\\\\b\\\"c\\nd\"", LuaGenerator.LuaString("a\\b\"c\nd"));
	}

	[Theory]
	[InlineData(0, "0")]
	[InlineData(9, "9")]
	[InlineData(10, "0xA")]
	[InlineData(0x7E0AF6, "0x7E0AF6")]
	[InlineData(-1, "(-1)")]
	public void Formats_lua_numbers(long value, string expected)
	{
		Assert.Equal(expected, LuaGenerator.LuaNumber(value));
	}
}
