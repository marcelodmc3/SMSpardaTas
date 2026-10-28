using System.Globalization;
using System.Text;

namespace SmTas;

public enum TokenKind
{
	Identifier,
	Number,
	String,
	Directive,
	Plus,
	Minus,
	Dot,
	Ampersand,
	Compare,
	LeftBrace,
	RightBrace,
	LeftParen,
	RightParen,
	LeftBracket,
	RightBracket,
	Newline,
	End
}

/// <summary>Column is 1-based; EndColumn is the column just past the token.</summary>
public sealed record Token(TokenKind Kind, string Text, int Line, int Column, int EndColumn, long Value = 0);

public sealed class ScriptException(string source, int line, int column, string message)
	: Exception($"{source}({line},{column}): error: {message}")
{
	public string SourceName { get; } = source;

	public int Line { get; } = line;

	public int Column { get; } = column;

	public string Detail { get; } = message;
}

public static class Lexer
{
	public static List<Token> Tokenize(string text, string sourceName)
	{
		List<Token> tokens = [];
		string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
		{
			TokenizeLine(lines[lineIndex], lineIndex + 1, sourceName, tokens);
			tokens.Add(new Token(TokenKind.Newline, "\n", lineIndex + 1, lines[lineIndex].Length + 1, lines[lineIndex].Length + 1));
		}
		tokens.Add(new Token(TokenKind.End, "", lines.Length, 1, 1));
		return tokens;
	}

	private static void TokenizeLine(string line, int lineNumber, string sourceName, List<Token> tokens)
	{
		int i = 0;
		while (i < line.Length)
		{
			char c = line[i];
			int start = i;
			if (char.IsWhiteSpace(c))
			{
				i++;
				continue;
			}
			if (c is '#' or ';')
			{
				return;
			}
			if (char.IsLetter(c) || c == '_')
			{
				while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
				{
					i++;
				}
				tokens.Add(new Token(TokenKind.Identifier, line[start..i], lineNumber, start + 1, i + 1));
				continue;
			}
			if (c == '@')
			{
				i++;
				while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
				{
					i++;
				}
				if (i == start + 1)
				{
					throw new ScriptException(sourceName, lineNumber, start + 1, "expected a directive name after '@'");
				}
				tokens.Add(new Token(TokenKind.Directive, line[(start + 1)..i], lineNumber, start + 1, i + 1));
				continue;
			}
			if (char.IsDigit(c) || c == '$')
			{
				i = ReadNumber(line, i, lineNumber, sourceName, out long value);
				tokens.Add(new Token(TokenKind.Number, line[start..i], lineNumber, start + 1, i + 1, value));
				continue;
			}
			if (c == '"')
			{
				i = ReadString(line, i, lineNumber, sourceName, out string value);
				tokens.Add(new Token(TokenKind.String, value, lineNumber, start + 1, i + 1));
				continue;
			}
			if (c is '=' or '!' or '<' or '>')
			{
				string op = i + 1 < line.Length && line[i + 1] == '=' ? line.Substring(i, 2) : c.ToString();
				if (op is "=" or "!")
				{
					string hint = op == "=" ? " (use '==' to compare)" : "";
					throw new ScriptException(sourceName, lineNumber, start + 1, $"unexpected '{op}'{hint}");
				}
				i += op.Length;
				tokens.Add(new Token(TokenKind.Compare, op, lineNumber, start + 1, i + 1));
				continue;
			}
			TokenKind? kind = c switch
			{
				'+' => TokenKind.Plus,
				'-' => TokenKind.Minus,
				'.' => TokenKind.Dot,
				'&' => TokenKind.Ampersand,
				'{' => TokenKind.LeftBrace,
				'}' => TokenKind.RightBrace,
				'(' => TokenKind.LeftParen,
				')' => TokenKind.RightParen,
				'[' => TokenKind.LeftBracket,
				']' => TokenKind.RightBracket,
				_ => null
			};
			if (kind == null)
			{
				throw new ScriptException(sourceName, lineNumber, start + 1, $"unexpected character '{c}'");
			}
			i++;
			tokens.Add(new Token(kind.Value, c.ToString(), lineNumber, start + 1, i + 1));
		}
	}

	/// <summary>Decimal (60), hex (0x91F8 or $91F8) and bank:address ($7F:1234) numbers.</summary>
	private static int ReadNumber(string line, int i, int lineNumber, string sourceName, out long value)
	{
		int start = i;
		bool hex;
		if (line[i] == '$')
		{
			hex = true;
			i++;
		}
		else if (line[i] == '0' && i + 1 < line.Length && (line[i + 1] is 'x' or 'X'))
		{
			hex = true;
			i += 2;
		}
		else
		{
			hex = false;
		}
		int digitsStart = i;
		while (i < line.Length && (hex ? Uri.IsHexDigit(line[i]) : char.IsDigit(line[i])))
		{
			i++;
		}
		if (i == digitsStart)
		{
			throw new ScriptException(sourceName, lineNumber, start + 1, "expected digits");
		}
		value = Parse(line[digitsStart..i], hex, lineNumber, start, sourceName);
		if (hex && i < line.Length && line[i] == ':')
		{
			int bankEnd = i;
			i++;
			int addressStart = i;
			while (i < line.Length && Uri.IsHexDigit(line[i]))
			{
				i++;
			}
			if (i == addressStart || bankEnd - digitsStart > 2 || i - addressStart > 4)
			{
				throw new ScriptException(sourceName, lineNumber, start + 1, "expected a bank:address like $7E:0AF6");
			}
			value = (value << 16) | Parse(line[addressStart..i], true, lineNumber, start, sourceName);
		}
		if (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
		{
			throw new ScriptException(sourceName, lineNumber, start + 1, $"invalid number '{line[start..(i + 1)]}'");
		}
		return i;
	}

	private static long Parse(string digits, bool hex, int lineNumber, int start, string sourceName)
	{
		NumberStyles style = hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None;
		if (!long.TryParse(digits, style, CultureInfo.InvariantCulture, out long value) || value > int.MaxValue)
		{
			throw new ScriptException(sourceName, lineNumber, start + 1, "number is too large");
		}
		return value;
	}

	private static int ReadString(string line, int i, int lineNumber, string sourceName, out string value)
	{
		int start = i;
		StringBuilder sb = new();
		i++;
		while (i < line.Length && line[i] != '"')
		{
			if (line[i] == '\\' && i + 1 < line.Length)
			{
				i++;
			}
			sb.Append(line[i]);
			i++;
		}
		if (i >= line.Length)
		{
			throw new ScriptException(sourceName, lineNumber, start + 1, "unterminated string");
		}
		value = sb.ToString();
		return i + 1;
	}
}
