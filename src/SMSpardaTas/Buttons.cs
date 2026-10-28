namespace SMSpardaTas;

/// <summary>
/// SNES controller buttons. The bit layout matches Super Metroid's controller 1 input
/// variable $8B (a copy of the SNES $4218 register), so a mask can be compared directly
/// against the game's own input variables.
/// </summary>
[Flags]
public enum Buttons : ushort
{
	None = 0,
	R = 0x0010,
	L = 0x0020,
	X = 0x0040,
	A = 0x0080,
	Right = 0x0100,
	Left = 0x0200,
	Down = 0x0400,
	Up = 0x0800,
	Start = 0x1000,
	Select = 0x2000,
	Y = 0x4000,
	B = 0x8000
}

public static class ButtonNames
{
	/// <summary>Display order, also used when decoding a mask in the generated Lua.</summary>
	public static readonly IReadOnlyList<Buttons> All =
	[
		Buttons.Up, Buttons.Down, Buttons.Left, Buttons.Right,
		Buttons.Select, Buttons.Start,
		Buttons.Y, Buttons.B, Buttons.X, Buttons.A,
		Buttons.L, Buttons.R
	];

	public static bool TryParse(string name, out Buttons button)
	{
		foreach (Buttons candidate in All)
		{
			if (string.Equals(candidate.ToString(), name, StringComparison.OrdinalIgnoreCase))
			{
				button = candidate;
				return true;
			}
		}
		button = Buttons.None;
		return false;
	}

	public static string Format(Buttons mask)
	{
		if (mask == Buttons.None)
		{
			return ".";
		}
		return string.Join("+", All.Where(b => (mask & b) != 0));
	}
}
