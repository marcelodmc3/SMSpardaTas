namespace SMSpardaTas;

/// <summary>A game action that Super Metroid lets the player bind to a button.</summary>
/// <param name="Name">Name used in the script's @controller block.</param>
/// <param name="DefaultButton">The game's default binding.</param>
/// <param name="BindingSymbol">RAM variable holding the game's current binding for this action.</param>
public sealed record GameAction(string Name, Buttons DefaultButton, string BindingSymbol);

/// <summary>
/// Maps the buttons a script is written with (the player's layout) to the buttons the game expects
/// with its default controller settings. The d-pad and Start can't be rebound in Super Metroid, so
/// they always map to themselves.
/// </summary>
public sealed class ControllerLayout
{
	public static readonly IReadOnlyList<GameAction> Actions =
	[
		new("shot", Buttons.X, "shot_binding"),
		new("jump", Buttons.A, "jump_binding"),
		new("dash", Buttons.B, "dash_binding"),
		new("item_select", Buttons.Select, "item_select_binding"),
		new("item_cancel", Buttons.Y, "item_cancel_binding"),
		new("angle_up", Buttons.R, "angle_up_binding"),
		new("angle_down", Buttons.L, "angle_down_binding")
	];

	public const Buttons Remappable = Buttons.A | Buttons.B | Buttons.X | Buttons.Y | Buttons.Select | Buttons.L | Buttons.R;

	public static readonly ControllerLayout Default = new(Actions.ToDictionary(a => a, a => a.DefaultButton));

	private readonly Dictionary<Buttons, Buttons> _toGame = [];

	private readonly Dictionary<Buttons, Buttons> _toPlayer = [];

	/// <summary>The player's button for each action. Must be a one-to-one assignment of the remappable buttons.</summary>
	public IReadOnlyDictionary<GameAction, Buttons> Bindings { get; }

	public ControllerLayout(IReadOnlyDictionary<GameAction, Buttons> bindings)
	{
		Bindings = bindings;
		foreach ((GameAction action, Buttons button) in bindings)
		{
			_toGame[button] = action.DefaultButton;
			_toPlayer[action.DefaultButton] = button;
		}
	}

	public static bool TryFindAction(string name, out GameAction action)
	{
		action = Actions.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))!;
		return action != null;
	}

	/// <summary>Translates buttons written in the player's layout into the game's default layout.</summary>
	public Buttons ToGame(Buttons player) => Map(player, _toGame);

	/// <summary>Translates game-layout buttons back into the player's layout, for display.</summary>
	public Buttons ToPlayer(Buttons game) => Map(game, _toPlayer);

	private static Buttons Map(Buttons mask, Dictionary<Buttons, Buttons> map)
	{
		Buttons result = Buttons.None;
		foreach (Buttons button in ButtonNames.All)
		{
			if ((mask & button) != 0)
			{
				result |= map.TryGetValue(button, out Buttons mapped) ? mapped : button;
			}
		}
		return result;
	}
}
