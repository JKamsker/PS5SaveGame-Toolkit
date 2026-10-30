using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Many independently bound scalars in a small, completely serialized synthetic document.</summary>
internal sealed class ScalarBatchDocument(int count) : BoundSaveDocument
{
    private readonly long[] _values = Enumerable.Range(0, count).Select(index => (long)index).ToArray();
    public int Writes { get; private set; }
    public int Validations { get; private set; }
    public override string Title => "Synthetic scalar batch";
    public static string Id(int index) => $"scalar/{index}";

    public override ViewNode BuildView() => new SectionNode(Title,
        Enumerable.Range(0, _values.Length).Select(index => (ViewNode)Field(Id(index), Id(index), IntegerSpec.Int64,
            () => FieldValue.Of(_values[index]), value => { _values[index] = value.AsInt64(); Writes++; },
            _ => { Validations++; return null; })).ToArray());

    public override IReadOnlyList<FileOutput> Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            foreach (var value in _values) writer.Write(value);
        return [new("batch.dat", stream.ToArray())];
    }
}
