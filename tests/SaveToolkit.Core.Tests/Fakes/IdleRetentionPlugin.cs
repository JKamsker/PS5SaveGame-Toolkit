using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Signals operation boundaries so idle-cleanup races need no sleeps or private samples.</summary>
internal sealed class IdleRetentionPlugin : IGamePlugin
{
    private int _disposals;
    public PluginInfo Info { get; } = new("test.retention", "Idle retention", "1.0.0");
    public Action? OnOpen { get; set; }
    public Action? BeforeEdit { get; set; }
    public Action<string>? BeforeDispose { get; set; }
    public int Disposals => Volatile.Read(ref _disposals);
    public ProbeResult Probe(ISaveFile file) => ProbeResult.None;
    public ISaveDocument Open(ISaveFile file)
    {
        OnOpen?.Invoke();
        return new RetentionDocument(this, file.RelativePath, file.ReadAllBytes()[0]);
    }

    private sealed class RetentionDocument(IdleRetentionPlugin owner, string path, byte initial) : BoundSaveDocument
    {
        private byte _value = initial;
        public override string Title => "Idle retention";
        public override ViewNode BuildView() => Field("value", "Value", IntegerSpec.UInt8,
            () => FieldValue.Of(_value), value =>
            {
                owner.BeforeEdit?.Invoke();
                _value = (byte)value.AsInt32();
            });
        public override IReadOnlyList<FileOutput> Serialize() =>
            throw new InvalidOperationException("Idle retention must not serialize documents.");
        protected override void Dispose(bool disposing)
        {
            Interlocked.Increment(ref owner._disposals);
            owner.BeforeDispose?.Invoke(path);
            base.Dispose(disposing);
        }
    }
}
