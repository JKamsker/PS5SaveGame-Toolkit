using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed class ScalarBindings(byte[] bytes, Func<string, string, FieldSpec, Func<FieldValue>, Action<FieldValue>?, Func<FieldValue, string?>?, FieldNode> field)
{
    private readonly Dictionary<string, int> _offsets = new(StringComparer.Ordinal);

    public bool HasConflictingAliases(IReadOnlyList<ValueEdit> edits) => edits.Where(edit => _offsets.ContainsKey(edit.ValueId))
        .GroupBy(edit => _offsets[edit.ValueId]).Any(group => group.Select(edit => edit.Value.AsInteger()).Distinct().Skip(1).Any());

    public FieldNode Bind(ScalarField scalar)
    {
        _offsets[scalar.Id] = scalar.Offset;
        FieldValue Read() => scalar.Byte ? FieldValue.Of((long)bytes[scalar.Offset]) :
            scalar.Signed ? FieldValue.Of((long)BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(scalar.Offset))) :
            FieldValue.Of((long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(scalar.Offset)));
        var warning = scalar.Warning;
        if (scalar.Signed && Read().AsInteger() < 0)
        {
            warning = "Negative value retained as stored; it may be a sentinel or an unsupported mapping.";
        }

        Action<FieldValue>? set = scalar.Editable ? value =>
        {
            if (scalar.Byte)
            {
                bytes[scalar.Offset] = checked((byte)value.AsUInt32());
            }
            else if (scalar.Signed)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(scalar.Offset), checked(value.AsInt32()));
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(scalar.Offset), checked(value.AsUInt32()));
            }
        } : null;
        Func<FieldValue, string?>? validate = scalar.Editable ? value =>
        {
            var integer = value.AsInteger();
            return scalar.Validate?.Invoke(integer) ?? (integer < scalar.Spec.Min || integer > scalar.Spec.Max
                ? $"Value must be between {scalar.Spec.Min} and {scalar.Spec.Max}."
                : null);
        } : null;
        return field(scalar.Id, scalar.Label, scalar.Spec, Read, set, validate) with
        {
            Confidence = scalar.Confidence,
            Description = $"Absolute offset 0x{scalar.Offset:X}; { (scalar.Byte ? "u8" : scalar.Signed ? "i32 LE" : "u32 LE") }. {warning}",
            Warning = warning,
        };
    }

}
