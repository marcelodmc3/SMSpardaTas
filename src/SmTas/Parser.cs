namespace SmTas;

/// <summary>
/// Parses a .tas script. The language is line based:
/// <code>
/// @savestate "landing_site.State"      ; or: @savestate slot 1
/// @var door_timer $0925 u16            ; name your own RAM variables
/// Right+B 60                           ; hold buttons for N frames (default 1)
/// . 10                                 ; neutral input ("wait 10" also works)
/// repeat 3 {
///     Y 2
///     . 2
/// }
/// wait_until game_state == 8 hold Right timeout 600
/// assert room == $91F8 "should be at the landing site"
/// label "Parlor"
/// </code>
/// </summary>
public sealed class Parser
{
	public const int DefaultTimeout = 3600;

	/// <summary>BizHawk's numbered savestate slots.</summary>
	public const int MinSlot = 1;

	public const int MaxSlot = 9;

	private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
	{
		"repeat", "wait", "wait_until", "assert", "label", "hold", "timeout",
		"and", "or", "not", "slot", "none", "u8", "u16", "s8", "s16"
	};

	private readonly List<Token> _tokens;

	private readonly string[] _lines;

	private readonly string _sourceName;

	private readonly Dictionary<string, RamSymbol> _symbols = new(StringComparer.OrdinalIgnoreCase);

	private int _position;

	private Token? _previous;

	private SavestateDirective? _savestate;

	private bool _seenStatement;

	private Parser(string text, string sourceName)
	{
		_sourceName = sourceName;
		_lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		_tokens = Lexer.Tokenize(text, sourceName);
		foreach (RamSymbol symbol in RamMap.BuiltIn)
		{
			_symbols[symbol.Name] = symbol;
		}
	}

	public static TasScript Parse(string text, string sourceName)
	{
		Parser parser = new(text, sourceName);
		List<Statement> statements = parser.ParseBlock(topLevel: true);
		return new TasScript
		{
			SourceName = sourceName,
			Savestate = parser._savestate,
			Statements = statements,
			Symbols = parser._symbols
		};
	}

	private List<Statement> ParseBlock(bool topLevel)
	{
		List<Statement> statements = [];
		while (true)
		{
			while (Peek().Kind == TokenKind.Newline)
			{
				Advance();
			}
			Token token = Peek();
			if (token.Kind == TokenKind.End)
			{
				if (!topLevel)
				{
					throw Error(token, "missing '}' to close a repeat block");
				}
				return statements;
			}
			if (token.Kind == TokenKind.RightBrace)
			{
				if (topLevel)
				{
					throw Error(token, "'}' without a matching repeat block");
				}
				Advance();
				ExpectEndOfLine();
				return statements;
			}
			Statement? statement = ParseStatement(topLevel);
			if (statement != null)
			{
				statements.Add(statement);
			}
		}
	}

	private Statement? ParseStatement(bool topLevel)
	{
		Token token = Peek();
		if (token.Kind == TokenKind.Directive)
		{
			ParseDirective(topLevel);
			return null;
		}
		_seenStatement = true;
		if (token.Kind == TokenKind.Identifier)
		{
			switch (token.Text.ToLowerInvariant())
			{
				case "repeat":
					return ParseRepeat();
				case "wait":
				{
					Advance();
					int frames = ExpectCount("a frame count after 'wait'", 1);
					ExpectEndOfLine();
					return new InputStatement(token.Line, Buttons.None, frames);
				}
				case "wait_until":
					return ParseWaitUntil();
				case "assert":
					return ParseAssert();
				case "label":
				{
					Advance();
					string text = Expect(TokenKind.String, "a quoted label text").Text;
					ExpectEndOfLine();
					return new LabelStatement(token.Line, text);
				}
			}
		}
		return ParseInput();
	}

	private Statement ParseInput()
	{
		Token first = Peek();
		Buttons buttons = ParseButtons();
		int frames = 1;
		if (Peek().Kind == TokenKind.Number)
		{
			frames = ExpectCount("a frame count", 1);
		}
		ExpectEndOfLine();
		return new InputStatement(first.Line, buttons, frames);
	}

	private Buttons ParseButtons()
	{
		Token first = Peek();
		if (first.Kind == TokenKind.Dot || IsKeyword(first, "none"))
		{
			Advance();
			return Buttons.None;
		}
		Buttons mask = Buttons.None;
		while (true)
		{
			Token token = Peek();
			if (token.Kind != TokenKind.Identifier)
			{
				throw Error(token, $"expected a button name or '.', got {Describe(token)}");
			}
			if (!ButtonNames.TryParse(token.Text, out Buttons button))
			{
				string valid = string.Join(", ", ButtonNames.All);
				throw Error(token, $"unknown button '{token.Text}' (buttons are {valid}; '.' means no buttons)");
			}
			if ((mask & button) != 0)
			{
				throw Error(token, $"button '{button}' is listed twice");
			}
			mask |= button;
			Advance();
			if (Peek().Kind != TokenKind.Plus)
			{
				return mask;
			}
			Advance();
		}
	}

	private Statement ParseRepeat()
	{
		Token keyword = Advance();
		int count = ExpectCount("a repeat count", 1);
		Expect(TokenKind.LeftBrace, "'{' after the repeat count");
		ExpectEndOfLine();
		List<Statement> body = ParseBlock(topLevel: false);
		return new RepeatStatement(keyword.Line, count, body);
	}

	private Statement ParseWaitUntil()
	{
		Token keyword = Advance();
		Token conditionStart = Peek();
		Expr condition = ParseOr();
		string conditionText = TextBetween(conditionStart, _previous!);
		Buttons hold = Buttons.None;
		int timeout = DefaultTimeout;
		while (Peek().Kind == TokenKind.Identifier)
		{
			Token option = Peek();
			if (IsKeyword(option, "hold"))
			{
				Advance();
				hold = ParseButtons();
			}
			else if (IsKeyword(option, "timeout"))
			{
				Advance();
				timeout = ExpectCount("a timeout in frames (0 waits forever)", 0);
			}
			else
			{
				throw Error(option, $"expected 'hold', 'timeout' or the end of the line, got '{option.Text}'");
			}
		}
		ExpectEndOfLine();
		return new WaitUntilStatement(keyword.Line, condition, conditionText, hold, timeout);
	}

	private Statement ParseAssert()
	{
		Token keyword = Advance();
		Token conditionStart = Peek();
		Expr condition = ParseOr();
		string conditionText = TextBetween(conditionStart, _previous!);
		string? message = null;
		if (Peek().Kind == TokenKind.String)
		{
			message = Advance().Text;
		}
		ExpectEndOfLine();
		return new AssertStatement(keyword.Line, condition, conditionText, message);
	}

	private void ParseDirective(bool topLevel)
	{
		Token directive = Advance();
		if (!topLevel)
		{
			throw Error(directive, $"@{directive.Text} is not allowed inside a repeat block");
		}
		switch (directive.Text.ToLowerInvariant())
		{
			case "savestate":
				ParseSavestate(directive);
				break;
			case "var":
				ParseVar();
				break;
			default:
				throw Error(directive, $"unknown directive '@{directive.Text}' (known: @savestate, @var)");
		}
		ExpectEndOfLine();
	}

	private void ParseSavestate(Token directive)
	{
		if (_savestate != null)
		{
			throw Error(directive, "@savestate can only be used once");
		}
		if (_seenStatement)
		{
			throw Error(directive, "@savestate must come before any input");
		}
		Token token = Peek();
		if (token.Kind == TokenKind.String)
		{
			Advance();
			if (token.Text.Length == 0)
			{
				throw Error(token, "the savestate path is empty");
			}
			_savestate = new SavestateDirective(directive.Line, token.Text, null);
		}
		else if (IsKeyword(token, "slot"))
		{
			Advance();
			Token slotToken = Peek();
			int slot = ExpectCount("a slot number", MinSlot);
			if (slot > MaxSlot)
			{
				throw Error(slotToken, $"BizHawk savestate slots are {MinSlot}-{MaxSlot}");
			}
			_savestate = new SavestateDirective(directive.Line, null, slot);
		}
		else
		{
			throw Error(token, "expected a quoted savestate path or 'slot N'");
		}
	}

	private void ParseVar()
	{
		Token name = Expect(TokenKind.Identifier, "a variable name");
		if (Keywords.Contains(name.Text) || ButtonNames.TryParse(name.Text, out _))
		{
			throw Error(name, $"'{name.Text}' is reserved and can't be used as a variable name");
		}
		if (_symbols.ContainsKey(name.Text))
		{
			throw Error(name, $"variable '{name.Text}' is already defined");
		}
		Token addressToken = Expect(TokenKind.Number, "a WRAM address like $0AF6 or $7F:1234");
		int address = ExpectWramAddress(addressToken);
		RamType type = RamType.U16;
		if (Peek().Kind == TokenKind.Identifier)
		{
			Token typeToken = Advance();
			if (!RamMap.TryParseType(typeToken.Text, out type))
			{
				throw Error(typeToken, $"unknown type '{typeToken.Text}' (use u8, u16, s8 or s16)");
			}
		}
		CheckFits(addressToken, address, type);
		_symbols[name.Text] = new RamSymbol(name.Text, address, type, "Declared in script");
	}

	private Expr ParseOr()
	{
		Expr left = ParseAnd();
		while (IsKeyword(Peek(), "or"))
		{
			Advance();
			left = new BinaryExpr("or", left, ParseAnd());
		}
		return left;
	}

	private Expr ParseAnd()
	{
		Expr left = ParseNot();
		while (IsKeyword(Peek(), "and"))
		{
			Advance();
			left = new BinaryExpr("and", left, ParseNot());
		}
		return left;
	}

	private Expr ParseNot()
	{
		Token token = Peek();
		if (IsKeyword(token, "not"))
		{
			Advance();
			return new NotExpr(ParseNot());
		}
		if (token.Kind == TokenKind.LeftParen)
		{
			Advance();
			Expr inner = ParseOr();
			Expect(TokenKind.RightParen, "')'");
			return inner;
		}
		return ParseComparison();
	}

	private Expr ParseComparison()
	{
		Expr left = ParseOperand();
		Token op = Peek();
		if (op.Kind != TokenKind.Compare)
		{
			throw Error(op, $"expected a comparison (==, !=, <, <=, >, >=), got {Describe(op)}");
		}
		Advance();
		Expr right = ParseOperand();
		return new BinaryExpr(op.Text, left, right);
	}

	private Expr ParseOperand()
	{
		Expr left = ParsePrimary();
		while (Peek().Kind == TokenKind.Ampersand)
		{
			Advance();
			left = new BinaryExpr("&", left, ParsePrimary());
		}
		return left;
	}

	private Expr ParsePrimary()
	{
		Token token = Peek();
		if (token.Kind == TokenKind.Minus)
		{
			Advance();
			Token number = Expect(TokenKind.Number, "a number after '-'");
			return new NumberExpr(-number.Value);
		}
		if (token.Kind == TokenKind.Number)
		{
			Advance();
			return new NumberExpr(token.Value);
		}
		if (token.Kind == TokenKind.Identifier)
		{
			Advance();
			if (RamMap.TryParseType(token.Text, out RamType type) && Peek().Kind == TokenKind.LeftBracket)
			{
				Advance();
				Token addressToken = Expect(TokenKind.Number, "a WRAM address");
				int address = ExpectWramAddress(addressToken);
				CheckFits(addressToken, address, type);
				Expect(TokenKind.RightBracket, "']'");
				string display = $"{type.ToString().ToLowerInvariant()}[{RamMap.FormatAddress(address)}]";
				return new MemoryExpr(address, type, display);
			}
			if (_symbols.TryGetValue(token.Text, out RamSymbol? symbol))
			{
				return new MemoryExpr(symbol.Address, symbol.Type, symbol.Name);
			}
			throw Error(token, $"unknown variable '{token.Text}' (declare it with @var, or use u16[$addr])");
		}
		throw Error(token, $"expected a number or a variable, got {Describe(token)}");
	}

	private int ExpectCount(string what, int minimum)
	{
		Token token = Expect(TokenKind.Number, what);
		if (token.Value < minimum)
		{
			throw Error(token, $"expected {what} of at least {minimum}");
		}
		return (int)token.Value;
	}

	private int ExpectWramAddress(Token token)
	{
		return RamMap.NormalizeAddress(token.Value)
			?? throw Error(token, $"address {token.Text} is outside WRAM ($7E:0000-$7F:FFFF)");
	}

	private void CheckFits(Token token, int address, RamType type)
	{
		if (address + RamMap.Size(type) - 1 > RamMap.WramEnd)
		{
			throw Error(token, $"a {type} at {token.Text} runs past the end of WRAM");
		}
	}

	private void ExpectEndOfLine()
	{
		Token token = Peek();
		if (token.Kind == TokenKind.Newline)
		{
			Advance();
		}
		else if (token.Kind != TokenKind.End)
		{
			throw Error(token, $"unexpected {Describe(token)}, expected the end of the line");
		}
	}

	private Token Expect(TokenKind kind, string what)
	{
		Token token = Peek();
		if (token.Kind != kind)
		{
			throw Error(token, $"expected {what}, got {Describe(token)}");
		}
		return Advance();
	}

	private Token Peek() => _tokens[_position];

	private Token Advance()
	{
		Token token = _tokens[_position];
		if (token.Kind != TokenKind.End)
		{
			_position++;
		}
		_previous = token;
		return token;
	}

	private static bool IsKeyword(Token token, string keyword)
	{
		return token.Kind == TokenKind.Identifier && string.Equals(token.Text, keyword, StringComparison.OrdinalIgnoreCase);
	}

	private string TextBetween(Token first, Token last)
	{
		string line = _lines[first.Line - 1];
		return line[(first.Column - 1)..(last.EndColumn - 1)];
	}

	private static string Describe(Token token)
	{
		return token.Kind switch
		{
			TokenKind.Newline => "the end of the line",
			TokenKind.End => "the end of the file",
			TokenKind.String => $"\"{token.Text}\"",
			_ => $"'{token.Text}'"
		};
	}

	private ScriptException Error(Token token, string message)
	{
		return new ScriptException(_sourceName, token.Line, token.Column, message);
	}
}
