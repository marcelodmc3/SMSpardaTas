namespace SMSpardaTas;

public abstract record Expr;

public sealed record NumberExpr(long Value) : Expr;

/// <summary>A WRAM read, either through a named symbol or an explicit u16[$0AF6] reference.</summary>
public sealed record MemoryExpr(int Address, RamType Type, string Display) : Expr;

/// <summary>Op is one of: &amp; == != &lt; &lt;= &gt; &gt;= and or.</summary>
public sealed record BinaryExpr(string Op, Expr Left, Expr Right) : Expr;

public sealed record NotExpr(Expr Operand) : Expr;

public abstract record Statement(int Line);

/// <summary>Hold <see cref="Buttons"/> for <see cref="Frames"/> emulator frames (lag frames included).</summary>
public sealed record InputStatement(int Line, Buttons Buttons, int Frames) : Statement(Line);

public sealed record RepeatStatement(int Line, int Count, IReadOnlyList<Statement> Body) : Statement(Line);

/// <summary>Hold <see cref="Hold"/> until the condition is true. A <see cref="Timeout"/> of 0 waits forever.</summary>
public sealed record WaitUntilStatement(int Line, Expr Condition, string ConditionText, Buttons Hold, int Timeout) : Statement(Line);

public sealed record AssertStatement(int Line, Expr Condition, string ConditionText, string? Message) : Statement(Line);

public sealed record LabelStatement(int Line, string Text) : Statement(Line);

/// <summary>Where playback starts from: a savestate file or a numbered slot.</summary>
public sealed record SavestateDirective(int Line, string? Path, int? Slot);

public sealed class TasScript
{
	public required string SourceName { get; init; }

	public SavestateDirective? Savestate { get; init; }

	public required IReadOnlyList<Statement> Statements { get; init; }

	/// <summary>Built-in symbols plus the script's own @var declarations.</summary>
	public required IReadOnlyDictionary<string, RamSymbol> Symbols { get; init; }
}
