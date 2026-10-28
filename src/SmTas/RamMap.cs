namespace SmTas;

public enum RamType
{
	U8,
	U16,
	S8,
	S16
}

/// <summary>A named WRAM variable. <see cref="Address"/> is the full 24-bit SNES address ($7E:xxxx or $7F:xxxx).</summary>
public sealed record RamSymbol(string Name, int Address, RamType Type, string Description);

/// <summary>Super Metroid variables taken from "RAM map.txt", usable by name in script conditions.</summary>
public static class RamMap
{
	public const int WramStart = 0x7E0000;
	public const int WramEnd = 0x7FFFFF;

	public static readonly IReadOnlyList<RamSymbol> BuiltIn =
	[
		new("input", 0x7E008B, RamType.U16, "Controller 1 input ($8B)"),
		new("new_input", 0x7E008F, RamType.U16, "Newly pressed controller 1 input ($8F)"),
		new("game_state", 0x7E0998, RamType.U16, "Game state (8 = main gameplay, Bh = door transition, Fh = paused)"),
		new("room", 0x7E079B, RamType.U16, "Room pointer"),
		new("area", 0x7E079F, RamType.U16, "Area index"),
		new("frame_counter", 0x7E05B6, RamType.U16, "Frame counter, excludes lag frames"),
		new("nmi_counter", 0x7E05B8, RamType.U16, "NMI counter, includes lag frames"),
		new("lag_counter", 0x7E05BA, RamType.U16, "Lag counter"),
		new("rng", 0x7E05E5, RamType.U16, "Random number"),
		new("igt_frames", 0x7E09DA, RamType.U16, "Game time, frames"),
		new("igt_seconds", 0x7E09DC, RamType.U16, "Game time, seconds"),
		new("igt_minutes", 0x7E09DE, RamType.U16, "Game time, minutes"),
		new("igt_hours", 0x7E09E0, RamType.U16, "Game time, hours"),
		new("equipped_items", 0x7E09A2, RamType.U16, "Equipped items"),
		new("collected_items", 0x7E09A4, RamType.U16, "Collected items"),
		new("equipped_beams", 0x7E09A6, RamType.U16, "Equipped beams"),
		new("collected_beams", 0x7E09A8, RamType.U16, "Collected beams"),
		new("health", 0x7E09C2, RamType.U16, "Samus health"),
		new("max_health", 0x7E09C4, RamType.U16, "Samus max health"),
		new("missiles", 0x7E09C6, RamType.U16, "Samus missiles"),
		new("max_missiles", 0x7E09C8, RamType.U16, "Samus max missiles"),
		new("supers", 0x7E09CA, RamType.U16, "Samus super missiles"),
		new("max_supers", 0x7E09CC, RamType.U16, "Samus max super missiles"),
		new("power_bombs", 0x7E09CE, RamType.U16, "Samus power bombs"),
		new("max_power_bombs", 0x7E09D0, RamType.U16, "Samus max power bombs"),
		new("max_reserve", 0x7E09D4, RamType.U16, "Samus max reserve health"),
		new("reserve", 0x7E09D6, RamType.U16, "Samus reserve health"),
		new("pose", 0x7E0A1C, RamType.U16, "Samus pose"),
		new("samus_x", 0x7E0AF6, RamType.U16, "Samus X position"),
		new("samus_x_sub", 0x7E0AF8, RamType.U16, "Samus X subposition"),
		new("samus_y", 0x7E0AFA, RamType.U16, "Samus Y position"),
		new("samus_y_sub", 0x7E0AFC, RamType.U16, "Samus Y subposition"),
		new("samus_y_speed", 0x7E0B2E, RamType.U16, "Samus Y speed"),
		new("samus_run_speed", 0x7E0B42, RamType.U16, "Samus X extra run speed / shinespark X speed"),
		new("samus_x_speed", 0x7E0B46, RamType.U16, "Samus X base speed (momentum)"),
		new("enemy0_hp", 0x7E0F8C, RamType.U16, "Enemy 0 health"),
		new("enemy1_hp", 0x7E0FCC, RamType.U16, "Enemy 1 health"),
		new("boss_hp", 0x7E0F8C, RamType.U16, "Alias of enemy0_hp (boss health in most boss rooms)"),
		new("mb2_hp", 0x7E0FCC, RamType.U16, "Alias of enemy1_hp (Mother Brain phase 2 health)")
	];

	/// <summary>
	/// Turns a script address into a full WRAM address. Bank-less addresses ($0AF6) are taken as bank $7E.
	/// Returns null when the address is outside WRAM.
	/// </summary>
	public static int? NormalizeAddress(long value)
	{
		if (value is >= 0 and <= 0xFFFF)
		{
			return WramStart + (int)value;
		}
		if (value is >= WramStart and <= WramEnd)
		{
			return (int)value;
		}
		return null;
	}

	public static bool TryParseType(string text, out RamType type)
	{
		switch (text.ToLowerInvariant())
		{
			case "u8":
				type = RamType.U8;
				return true;
			case "u16":
				type = RamType.U16;
				return true;
			case "s8":
				type = RamType.S8;
				return true;
			case "s16":
				type = RamType.S16;
				return true;
			default:
				type = RamType.U16;
				return false;
		}
	}

	public static int Size(RamType type) => type is RamType.U8 or RamType.S8 ? 1 : 2;

	public static string FormatAddress(int address) => $"${address >> 16:X2}:{address & 0xFFFF:X4}";
}
