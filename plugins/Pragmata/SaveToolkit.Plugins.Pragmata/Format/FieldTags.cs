namespace SaveToolkit.Plugins.Pragmata.Format;

internal static class FieldTags
{
    public const int Array = -1;
    public const int Enum = 1;
    public const int Boolean = 2;
    public const int Int8 = 3;
    public const int UInt8 = 4;
    public const int Int16 = 5;
    public const int UInt16 = 6;
    public const int Int32 = 7;
    public const int UInt32 = 8;
    public const int Int64 = 9;
    public const int UInt64 = 10;
    public const int Float32 = 11;
    public const int Float64 = 12;
    public const int Char8 = 13;
    public const int Char16 = 14;
    public const int String = 15;
    public const int Struct = 16;
    public const int Class = 17;

    public static string ShortName(int type) => type switch
    {
        Enum => "enum",
        Boolean => "bool",
        Int8 => "i8",
        UInt8 => "u8",
        Int16 => "i16",
        UInt16 => "u16",
        Int32 => "i32",
        UInt32 => "u32",
        Int64 => "i64",
        UInt64 => "u64",
        Float32 => "f32",
        Float64 => "f64",
        Char8 => "char8",
        Char16 => "char16",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a primitive field type."),
    };
}
