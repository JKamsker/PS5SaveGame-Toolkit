using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy16;

// Fixed-width patches only. Bounds are conservative corpus ranges, never claimed gameplay caps.
internal sealed class FaithWritableField(byte[] bytes, int offset, int width, int min, int max, IReadOnlyList<string>? categories = null)
{
    private readonly int _original = Read(bytes, offset, width);
    public int Offset => offset;
    public int Width => width;
    public int Minimum => Math.Min(min, _original);
    public int Maximum => Math.Max(max, _original);

    public FieldSpec Spec(FieldSpec spec) => spec is IntegerSpec number
        ? number with { Min = Minimum, Max = Maximum } : spec;

    public FieldValue Get(bool reference) => reference
        ? FieldValue.Of(FaithCatalog.Key(Read(bytes, offset, width)))
        : FieldValue.Of(Read(bytes, offset, width));

    public string? Validate(FieldValue value, string? catalog, FaithCatalog names)
    {
        if (!TryNumber(value, catalog is not null, out var number)) return "Expected an exact signed 32-bit value.";
        if (number != _original && (number < min || number > max))
            return $"Use the conservative observed range {min}..{max}; the original value can also be restored. These are not proven game limits.";
        if (catalog is not null && number != _original && number != 0 && names.Find(catalog, number) is null)
            return "Choose a named catalog ID, zero, or restore this field's original raw ID; unknown replacements are not supported.";
        if (catalog is not null && categories is not null && number != _original && number != 0 &&
            !categories.Contains(names.Find(catalog, number)?.Category, StringComparer.Ordinal))
            return "This field accepts only these reference categories: " + string.Join(", ", categories) + ". Slot eligibility is still unverified.";
        return null;
    }

    public void Set(FieldValue value, bool reference) => Write(bytes, value, reference);

    public void Write(byte[] target, FieldValue value, bool reference)
    {
        if (!TryNumber(value, reference, out var number)) throw new ArgumentException("Invalid fixed-width value.", nameof(value));
        if (width == sizeof(byte)) target[offset] = checked((byte)number);
        else BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset), number);
    }

    private static bool TryNumber(FieldValue value, bool reference, out int number)
    {
        number = 0;
        if (reference) return value.Kind == FieldValueKind.Text && int.TryParse(value.AsString(), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out number) && FaithCatalog.Key(number) == value.AsString();
        if (value.Kind != FieldValueKind.Integer || value.AsInteger() < int.MinValue || value.AsInteger() > int.MaxValue) return false;
        number = value.AsInt32();
        return true;
    }

    private static int Read(byte[] source, int position, int size) => size == sizeof(byte)
        ? source[position] : BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(position));
}
