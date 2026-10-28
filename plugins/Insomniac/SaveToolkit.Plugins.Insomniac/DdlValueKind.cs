namespace SaveToolkit.Plugins.Insomniac;

internal enum DdlValueKind : byte
{
    UInt8 = 0,
    UInt16 = 1,
    UInt32 = 2,
    UInt64 = 3,
    Int8 = 4,
    Int16 = 5,
    Int32 = 6,
    Int64 = 7,
    Float = 8,
    Double = 9,
    String = 10,
    Enum = 11,
    Bitfield = 12,
    Object = 13,
    Bool = 15,
    File = 16,
    Identifier = 17,
    Json = 18,
    Null = 19,
    Asset = 20,
}
