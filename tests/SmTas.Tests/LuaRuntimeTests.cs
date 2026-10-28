using MoonSharp.Interpreter;

namespace SmTas.Tests;

/// <summary>
/// Runs the generated Lua under MoonSharp against a fake BizHawk API, so these tests check
/// what playback actually does frame by frame, not just the text it was generated as.
/// </summary>
public class LuaRuntimeTests
{
	// WRAM is a byte table indexed by offset from $7E:0000. __on_frame(n) runs after each
	// emulated frame so a test can change RAM over time.
	private const string FakeBizHawk = """
		__inputs, __log, __ram, __paused, __loaded, __pending = {}, {}, {}, false, nil, nil
		local function rd(o) return __ram[o] or 0 end
		local function word(o) return rd(o) + rd(o + 1) * 256 end
		local function signed(v, bits) if v >= 2 ^ (bits - 1) then return v - 2 ^ bits end return v end
		memory = {
			read_u8 = function(a, d) assert(d == "WRAM") return rd(a) end,
			read_s8 = function(a, d) assert(d == "WRAM") return signed(rd(a), 8) end,
			read_u16_le = function(a, d) assert(d == "WRAM") return word(a) end,
			read_s16_le = function(a, d) assert(d == "WRAM") return signed(word(a), 16) end,
		}
		joypad = {
			set = function(pad, controller)
				assert(controller == 1)
				local names = {}
				for k, v in pairs(pad) do if v then names[#names + 1] = k end end
				__pending = table.concat(names, "+")
			end
		}
		emu = {
			frameadvance = function()
				__inputs[#__inputs + 1] = __pending or ""
				__pending = nil
				if __on_frame then __on_frame(#__inputs) end
			end
		}
		client = { pause = function() __paused = true end }
		console = { log = function(m) __log[#__log + 1] = m end }
		gui = { text = function() end }
		savestate = {
			load = function(path) __loaded = path end,
			loadslot = function(n) __loaded = "slot" .. n end,
		}

		""";

	private sealed record Run(List<string> Inputs, List<string> Log, bool Paused, string? Loaded);

	private static Run Execute(string tas, string onFrame = "")
	{
		TasScript script = Parser.Parse(tas, "test.tas");
		string lua = LuaGenerator.Generate(script, new GeneratorOptions()).Lua;
		Script engine = new();
		engine.DoString(FakeBizHawk + onFrame + "\n" + lua);
		return new Run(
			engine.Globals.Get("__inputs").Table.Values.Select(v => SortButtons(v.String)).ToList(),
			engine.Globals.Get("__log").Table.Values.Select(v => v.String).ToList(),
			engine.Globals.Get("__paused").Boolean,
			engine.Globals.Get("__loaded").CastToString());
	}

	// MoonSharp's table.sort doesn't order strings like stock Lua, so sort here.
	private static string SortButtons(string pressed)
	{
		return string.Join("+", pressed.Split('+', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
	}

	[Fact]
	public void Plays_each_input_for_its_frame_count()
	{
		Run run = Execute("Right+B 3\n. 2\nA");

		Assert.Equal(["B+Right", "B+Right", "B+Right", "", "", "A"], run.Inputs);
		Assert.Contains("finished after 6 frames", run.Log.Last());
	}

	[Fact]
	public void Pauses_when_playback_finishes()
	{
		Assert.True(Execute("A").Paused);
	}

	[Fact]
	public void Uses_bizhawk_button_names()
	{
		Assert.Equal(["Start+Up+Y", "L+R", "Down+Left+Select", "A+B+X"], Execute("Up+Start+Y\nL+R\nDown+Left+Select\nA+B+X").Inputs);
	}

	[Fact]
	public void Loads_the_savestate_slot_before_the_first_frame()
	{
		Run run = Execute("@savestate slot 4\nA", "__on_frame = function() assert(__loaded == 'slot4') end");

		Assert.Equal(["A"], run.Inputs);
		Assert.Equal("slot4", run.Loaded);
	}

	[Fact]
	public void Expands_repeat_blocks()
	{
		Run run = Execute("repeat 2 {\n  Y\n  repeat 2 {\n    .\n  }\n}\nB");

		Assert.Equal(["Y", "", "", "Y", "", "", "B"], run.Inputs);
	}

	[Fact]
	public void Wait_until_holds_buttons_until_the_condition_is_true()
	{
		// game_state ($0998) becomes 8 after the 4th frame.
		Run run = Execute(
			"wait_until game_state == 8 hold Left\nA",
			"__on_frame = function(n) if n == 4 then __ram[0x0998] = 8 end end");

		Assert.Equal(["Left", "Left", "Left", "Left", "A"], run.Inputs);
	}

	[Fact]
	public void Wait_until_that_is_already_true_takes_no_frames()
	{
		Run run = Execute("wait_until room == 0\nA");

		Assert.Equal(["A"], run.Inputs);
	}

	[Fact]
	public void Wait_until_timeout_stops_playback_and_reports_values()
	{
		Run run = Execute("wait_until samus_x > 100 hold Right timeout 3\nA");

		Assert.Equal(["Right", "Right", "Right"], run.Inputs);
		Assert.True(run.Paused);
		string failure = Assert.Single(run.Log);
		Assert.Contains("line 1", failure);
		Assert.Contains("timed out after 3 frames", failure);
		Assert.Contains("samus_x=0 (0x0000)", failure);
	}

	[Fact]
	public void Failed_assert_stops_before_later_inputs()
	{
		string onFrame = "__on_frame = function(n) if n == 1 then __ram[0x079B] = 0xF8; __ram[0x079C] = 0x91 end end";
		Run run = Execute("A\nassert room == $91F8\nB\nassert room == 0 \"wrong room\"\nX", onFrame);

		Assert.Equal(["A", "B"], run.Inputs);
		Assert.True(run.Paused);
		Assert.Contains("line 4: wrong room: room == 0; room=37368 (0x91F8)", Assert.Single(run.Log));
	}

	[Fact]
	public void Evaluates_bitmasks_signed_values_and_boolean_logic()
	{
		Run run = Execute(
			"@var speed $0B2E s16\n" +
			"assert input & $80 == $80 and input & $40 == 0\n" +
			"assert speed == -1 and u16[$0B2E] == $FFFF\n" +
			"assert not (speed > 0) or room == 99\n" +
			"A",
			"__ram[0x8B] = 0x80; __ram[0x0B2E] = 0xFF; __ram[0x0B2F] = 0xFF\n");

		Assert.Equal(["A"], run.Inputs);
		Assert.DoesNotContain(run.Log, l => l.Contains("assert failed"));
	}

	[Fact]
	public void Labels_are_logged_with_the_frame_they_start_on()
	{
		Run run = Execute(". 5\nlabel \"Parlor\"\nA");

		Assert.Equal("SmTas: frame 5: Parlor", run.Log[0]);
	}

	[Fact]
	public void Sample_script_compiles()
	{
		string sample = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "landing_site.tas"));

		GeneratorResult result = LuaGenerator.Generate(Parser.Parse(sample, "landing_site.tas"), new GeneratorOptions());

		Assert.Empty(result.Warnings);
	}

	[Fact]
	public void Runs_a_script_through_a_door_transition()
	{
		const string script = """
			@savestate slot 1
			assert room == $91F8 "start from the Landing Site"
			assert game_state == 8
			label "Landing Site"
			Left+B 30
			Left+B+A 15
			Left+B 40
			repeat 3 {
			    Left+B+A 4
			    Left+B   8
			}
			wait_until game_state == $0B hold Left+B timeout 900
			wait_until game_state == 8 timeout 300
			label "Parlor"
			assert room != $91F8 "should have left the Landing Site"
			. 10
			""";
		// Start in the Landing Site in gameplay; enter a door transition on frame 120 and finish it 40 frames later.
		string onFrame = """
			__ram[0x079B], __ram[0x079C], __ram[0x0998] = 0xF8, 0x91, 8
			__on_frame = function(n)
				if n == 120 then __ram[0x0998] = 0x0B end
				if n == 160 then __ram[0x0998], __ram[0x079B], __ram[0x079C] = 8, 0xFD, 0x92 end
			end
			""";

		Run run = Execute(script, onFrame);

		Assert.Equal(170, run.Inputs.Count);
		Assert.Contains("finished after 170 frames", run.Log.Last());
	}
}
