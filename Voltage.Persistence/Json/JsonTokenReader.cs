using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Voltage.Persistence
{
	/// <summary>
	/// Lightweight forward-only JSON reader for AOT-safe deserialization.
	/// Used by hand-written and source-generated deserializers to read JSON
	/// without any reflection. Handles nested objects/arrays via skip methods.
	/// </summary>
	public sealed class JsonTokenReader : IDisposable
	{
		private readonly StringReader _reader;
		private const string WhiteSpace = " \t\n\r";

		public JsonTokenReader(string json)
		{
			_reader = new StringReader(json);
		}

		public void Dispose() => _reader.Dispose();

		#region Core reading

		private char Peek()
		{
			var p = _reader.Peek();
			return p == -1 ? '\0' : (char)p;
		}

		private char Read() => (char)_reader.Read();

		private void ConsumeWhiteSpace()
		{
			while (WhiteSpace.IndexOf(Peek()) != -1)
			{
				_reader.Read();
				if (_reader.Peek() == -1) break;
			}
		}

		/// <summary>
		/// Expects and consumes the opening '{' of a JSON object.
		/// Returns false if the next non-whitespace char is not '{'.
		/// </summary>
		public bool BeginObject()
		{
			ConsumeWhiteSpace();
			if (Peek() == '{') { Read(); return true; }
			return false;
		}

		/// <summary>
		/// Reads the next key in the current JSON object.
		/// Returns false when '}' is reached (object ended).
		/// Consumes the trailing ':' after the key.
		/// </summary>
		public bool ReadNextKey(out string key)
		{
			key = null;
			ConsumeWhiteSpace();

			var c = Peek();
			if (c == '}') { Read(); return false; }
			if (c == ',') { Read(); ConsumeWhiteSpace(); c = Peek(); }
			if (c == '}') { Read(); return false; }

			if (c != '"') return false;

			key = ReadStringValue();

			// consume ':'
			ConsumeWhiteSpace();
			if (Peek() == ':') Read();

			return key != null;
		}

		#endregion

		#region Typed readers

		public string ReadString()
		{
			ConsumeWhiteSpace();
			if (Peek() == 'n') { ReadWord(); return null; }
			return ReadStringValue();
		}

		public int ReadInt()
		{
			ConsumeWhiteSpace();
			var word = ReadWord();
			return int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
		}

		public float ReadFloat()
		{
			ConsumeWhiteSpace();
			var word = ReadWord();
			return float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
		}

		public double ReadDouble()
		{
			ConsumeWhiteSpace();
			var word = ReadWord();
			return double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0d;
		}

		public long ReadLong()
		{
			ConsumeWhiteSpace();
			var word = ReadWord();
			return long.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0L;
		}

		public bool ReadBool()
		{
			ConsumeWhiteSpace();
			var word = ReadWord();
			return word == "true";
		}

		/// <summary>
		/// Reads a Guid value. Handles three JSON representations:
		/// 1. String: "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
		/// 2. Null:   null
		/// 3. Object: { } or { "A": ..., "B": ..., ... } (Voltage.Persistence.JsonEncoder
		///    serializes structs as objects by default)
		/// </summary>
		public Guid ReadGuid()
		{
			ConsumeWhiteSpace();
			var c = Peek();

			// null
			if (c == 'n') { ReadWord(); return Guid.Empty; }

			// String representation: "xxxxxxxx-..."
			if (c == '"')
			{
				var str = ReadStringValue();
				return str != null && Guid.TryParse(str, out var g) ? g : Guid.Empty;
			}

			// Object representation: { } or { "A": ..., ... }
			// JsonEncoder serializes Guid as a struct with internal fields.
			// We can't reconstruct it from those fields, so skip the object
			// and return Guid.Empty.
			if (c == '{')
			{
				SkipObject();
				return Guid.Empty;
			}

			// Unknown token  skip it
			ReadWord();
			return Guid.Empty;
		}

		/// <summary>
		/// Reads a nullable Guid value. Same object/string/null handling as ReadGuid.
		/// </summary>
		public Guid? ReadNullableGuid()
		{
			ConsumeWhiteSpace();
			var c = Peek();

			// null
			if (c == 'n') { ReadWord(); return null; }

			// String representation
			if (c == '"')
			{
				var str = ReadStringValue();
				return str != null && Guid.TryParse(str, out var g) ? g : null;
			}

			// Object representation  Guid serialized as struct fields
			if (c == '{')
			{
				SkipObject();
				return null;
			}

			ReadWord();
			return null;
		}

		public T ReadEnum<T>() where T : struct, Enum
		{
			ConsumeWhiteSpace();
			// enums can be serialized as strings or ints
			if (Peek() == '"')
			{
				var str = ReadStringValue();
				return Enum.TryParse<T>(str, out var e) ? e : default;
			}
			else
			{
				var word = ReadWord();
				if (int.TryParse(word, out var intVal))
					return (T)Enum.ToObject(typeof(T), intVal);
				return default;
			}
		}

		public DateTime ReadDateTime()
		{
			ConsumeWhiteSpace();
			var c = Peek();

			// null
			if (c == 'n') { ReadWord(); return default; }

			// String: "2026-02-27T20:09:42.4746066Z"
			if (c == '"')
			{
				var str = ReadStringValue();
				if (str != null && DateTime.TryParse(str, CultureInfo.InvariantCulture,
					DateTimeStyles.RoundtripKind, out var dt))
					return dt;
				return default;
			}

			// Object: { "Ticks": ..., ... }  JsonEncoder serializes DateTime as struct
			if (c == '{')
			{
				long ticks = 0;
				Read(); // consume '{'
				while (true)
				{
					ConsumeWhiteSpace();
					var p = Peek();
					if (p == '}') { Read(); break; }
					if (p == ',') { Read(); continue; }
					if (p != '"') { ReadWord(); continue; }

					var key = ReadStringValue();
					ConsumeWhiteSpace();
					if (Peek() == ':') Read();

					if (key == "Ticks")
						ticks = ReadLong();
					else
						SkipValue();
				}
				return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : default;
			}

			// Number: epoch time
			var word = ReadWord();
			if (long.TryParse(word, out var epoch))
			{
				return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
			}

			return default;
		}

		#endregion

		#region Composite readers

		/// <summary>
		/// Reads a JSON object using a delegate deserializer.
		/// Returns default(T) if null.
		/// </summary>
		public T ReadObject<T>(Func<JsonTokenReader, T> deserializer)
		{
			ConsumeWhiteSpace();
			if (Peek() == 'n') { ReadWord(); return default; }
			return deserializer(this);
		}

		/// <summary>
		/// Reads a JSON array, deserializing each element with the given reader func.
		/// </summary>
		public List<T> ReadList<T>(Func<JsonTokenReader, T> elementReader)
		{
			ConsumeWhiteSpace();
			if (Peek() == 'n') { ReadWord(); return null; }
			if (Peek() != '[') return null;
			Read(); // consume '['

			var list = new List<T>();
			ConsumeWhiteSpace();
			if (Peek() == ']') { Read(); return list; }

			while (true)
			{
				ConsumeWhiteSpace();
				list.Add(elementReader(this));
				ConsumeWhiteSpace();
				if (Peek() == ',') { Read(); continue; }
				if (Peek() == ']') { Read(); break; }
				break; // malformed
			}

			return list;
		}

		/// <summary>
		/// Reads a JSON array into a freshly-allocated T[] array, deserializing each
		/// element with the given reader func. Returns <c>null</c> if the JSON value
		/// is <c>null</c> or not an array.
		/// </summary>
		public T[] ReadArray<T>(Func<JsonTokenReader, T> elementReader)
		{
			ConsumeWhiteSpace();
			if (Peek() == 'n') { ReadWord(); return null; }
			if (Peek() != '[') return null;
			Read(); // consume '['

			ConsumeWhiteSpace();
			if (Peek() == ']') { Read(); return System.Array.Empty<T>(); }

			// We don't know the length up-front, so collect into a List<T> then ToArray.
			// Mirrors the pattern in ReadList<T>() — same performance characteristics.
			var list = new List<T>();
			while (true)
			{
				ConsumeWhiteSpace();
				list.Add(elementReader(this));
				ConsumeWhiteSpace();
				if (Peek() == ',') { Read(); continue; }
				if (Peek() == ']') { Read(); break; }
				break; // malformed
			}

			return list.ToArray();
		}

		/// <summary>
		/// Reads a JSON object as a Dictionary&lt;string, TValue&gt;.
		/// </summary>
		public Dictionary<string, TValue> ReadStringDictionary<TValue>(Func<JsonTokenReader, TValue> valueReader)
		{
			ConsumeWhiteSpace();
			if (Peek() == 'n') { ReadWord(); return null; }
			if (Peek() != '{') return null;
			Read(); // consume '{'

			var dict = new Dictionary<string, TValue>();

			ConsumeWhiteSpace();
			if (Peek() == '}') { Read(); return dict; }

			while (true)
			{
				ConsumeWhiteSpace();
				if (Peek() == '}') { Read(); break; }
				if (Peek() == ',') { Read(); ConsumeWhiteSpace(); }

				var key = ReadStringValue();
				ConsumeWhiteSpace();
				if (Peek() == ':') Read();

				dict[key] = valueReader(this);

				ConsumeWhiteSpace();
				if (Peek() == '}') { Read(); break; }
			}

			return dict;
		}

		/// <summary>
		/// Skips the next JSON value (string, number, bool, null, object, or array).
		/// Use when encountering an unknown key.
		/// </summary>
		public void SkipValue()
		{
			ConsumeWhiteSpace();
			var c = Peek();
			switch (c)
			{
				case '"':
					ReadStringValue();
					break;
				case '{':
					SkipObject();
					break;
				case '[':
					SkipArray();
					break;
				default:
					ReadWord();
					break;
			}
		}

		#endregion

		#region Internal helpers

		private string ReadStringValue()
		{
			if (Peek() != '"') return null;
			Read(); // consume opening "

			var sb = new System.Text.StringBuilder();
			while (true)
			{
				if (_reader.Peek() == -1) break;
				var c = Read();
				if (c == '"') break;
				if (c == '\\')
				{
					if (_reader.Peek() == -1) break;
					var esc = Read();
					switch (esc)
					{
						case '"': sb.Append('"'); break;
						case '\\': sb.Append('\\'); break;
						case '/': sb.Append('/'); break;
						case 'b': sb.Append('\b'); break;
						case 'f': sb.Append('\f'); break;
						case 'n': sb.Append('\n'); break;
						case 'r': sb.Append('\r'); break;
						case 't': sb.Append('\t'); break;
						case 'u':
							var hex = new char[4];
							for (int i = 0; i < 4; i++) hex[i] = Read();
							sb.Append((char)Convert.ToInt32(new string(hex), 16));
							break;
						default: sb.Append(esc); break;
					}
				}
				else
				{
					sb.Append(c);
				}
			}
			return sb.ToString();
		}

		private string ReadWord()
		{
			var sb = new System.Text.StringBuilder();
			const string wordBreak = " \t\n\r{}[],:\"";
			while (_reader.Peek() != -1 && wordBreak.IndexOf(Peek()) == -1)
				sb.Append(Read());
			return sb.ToString();
		}

		private void SkipObject()
		{
			Read(); // consume '{'
			int depth = 1;
			while (depth > 0 && _reader.Peek() != -1)
			{
				var c = Read();
				if (c == '{') depth++;
				else if (c == '}') depth--;
				else if (c == '"') SkipStringContents();
			}
		}

		private void SkipArray()
		{
			Read(); // consume '['
			int depth = 1;
			while (depth > 0 && _reader.Peek() != -1)
			{
				var c = Read();
				if (c == '[') depth++;
				else if (c == ']') depth--;
				else if (c == '"') SkipStringContents();
			}
		}

		private void SkipStringContents()
		{
			while (_reader.Peek() != -1)
			{
				var c = Read();
				if (c == '"') return;
				if (c == '\\' && _reader.Peek() != -1) Read(); // skip escaped char
			}
		}

		#endregion
	}
}