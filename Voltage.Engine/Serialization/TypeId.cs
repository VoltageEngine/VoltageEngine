namespace Voltage.Serialization;

/// <summary>
/// An identifier for a serializable type.
/// Derived from the fully-qualified CLR name at source-generation time.
/// </summary>
public readonly record struct TypeId(string Value)
{
	public override string ToString() => Value;

	public static implicit operator string(TypeId id) => id.Value;
	public static explicit operator TypeId(string s) => new(s);
}