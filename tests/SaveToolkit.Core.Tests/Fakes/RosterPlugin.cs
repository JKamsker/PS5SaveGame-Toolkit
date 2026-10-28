using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>
/// Reference plugin for structural edits: a variable-length list of party members with add, remove
/// and move-to-front actions, all undoable through whole-document snapshots.
/// Layout: "RSTR" magic, u16 next id, u16 count, count × (u16 id, u16 level, 12-byte name), opaque trailer.
/// </summary>
public sealed class RosterPlugin : IGamePlugin
{
    public const int MaxMembers = 8;
    public const string FailingName = "!fail";

    private static readonly byte[] Magic = "RSTR"u8.ToArray();

    public PluginInfo Info { get; } = new("test.roster", "Roster test format", "1.0.0")
    {
        Games = [new GameTitle("PPSA99998", "Roster Game")],
    };

    /// <summary>Test hook: the most recently opened document (test fakes may keep state; real plugins may not).</summary>
    public RosterDocument? LastOpened { get; private set; }

    public ProbeResult Probe(ISaveFile file) =>
        file.ReadHeader(4).AsSpan().SequenceEqual(Magic) ? ProbeResult.Match(ProbeScore.Title, "Roster save") : ProbeResult.None;

    public ISaveDocument Open(ISaveFile file) => LastOpened = new RosterDocument(file.RelativePath, file.ReadAllBytes());

    public static byte[] Create(params (ushort Id, ushort Level, string Name)[] members)
    {
        var bytes = new byte[8 + (members.Length * 16) + 3];
        Magic.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)(members.Length == 0 ? 1 : members.Max(m => m.Id) + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)members.Length);
        for (var i = 0; i < members.Length; i++)
        {
            var at = 8 + (i * 16);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), members[i].Id);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 2), members[i].Level);
            Encoding.UTF8.GetBytes(members[i].Name).CopyTo(bytes, at + 4);
        }

        // Opaque trailer the plugin does not understand and must preserve.
        bytes[^3] = 0xDE;
        bytes[^2] = 0xAD;
        bytes[^1] = 0x01;
        return bytes;
    }

    /// <summary>Mutable model with stable record ids (the stored member id), parsed from and written back to bytes.</summary>
    public sealed class RosterDocument : BoundSaveDocument
    {
        private readonly string _path;
        private List<Member> _members = [];
        private ushort _nextId;
        private byte[] _trailer = [];

        public RosterDocument(string path, byte[] bytes)
        {
            _path = path;
            Load(bytes);
        }

        public override string Title => $"Roster ({_members.Count})";

        public IReadOnlyList<ushort> MemberIds => [.. _members.Select(m => m.Id)];

        public override ViewNode BuildView()
        {
            var rows = _members.Select(m => new TableRow($"m{m.Id}", new Dictionary<string, ViewNode>
            {
                ["id"] = new TextNode(m.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ["name"] = Field($"member/{m.Id}/name", "Name", new TextSpec { MaxLength = 12 }, () => FieldValue.Of(m.Name), v => m.Name = Fit(v.AsString())),
            })).ToList();

            List<ViewNode> children =
            [
                new TableNode([new TableColumn("id", "Id"), new TableColumn("name", "Name")], rows) { Title = "Members" },
                .. _members.Select(m => SectionNode.Lazy($"Details: {m.Name}", $"details/{m.Id}")),
                new ActionNode("add", "Add member") { Parameters = [new FieldNode("name", "Name", new TextSpec { MaxLength = 12 }, FieldValue.Of("new"))] },
                new ActionNode("clear-all", "Remove everyone (not undoable)"),
            ];
            if (_members.Count > 0)
            {
                // A typed reference: the parameter's options are record keys, rebuilt with the view.
                var target = new FieldNode("member", "Member", MemberSpec(), FieldValue.Of(_members[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                children.Add(new ActionNode("remove", "Remove member") { Parameters = [target] });
                children.Add(new ActionNode("move-first", "Move to front") { Parameters = [target] });
            }

            return new SectionNode("Roster", children);
        }

        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
        {
            var member = lazyId.StartsWith("details/", StringComparison.Ordinal) ? Find(lazyId["details/".Length..]) : null;
            return member is null
                ? base.ExpandNode(lazyId)
                : [Field($"member/{member.Id}/level", "Level", IntegerSpec.UInt16 with { Min = 1, Max = 99 }, () => FieldValue.Of((long)member.Level), v => member.Level = (ushort)v.AsInt32())];
        }

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) => actionId switch
        {
            "add" => Structural(() => Add(parameters["name"].AsString()), "Member added."),
            "remove" => Structural(() => _members.Remove(Require(parameters["member"])), "Member removed."),
            "move-first" => Structural(() => MoveFirst(Require(parameters["member"])), "Member moved."),
            "clear-all" => ClearAll(),
            _ => base.InvokeAction(actionId, parameters),
        };

        public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput(_path, ToBytes())];

        protected override DocumentMemento CaptureSnapshot()
        {
            var bytes = ToBytes();
            return DocumentMemento.Snapshot(bytes, bytes.Length);
        }

        protected override void LoadSnapshot(DocumentMemento snapshot) => Load(((SnapshotMemento<byte[]>)snapshot).State);

        private void Add(string name)
        {
            if (_members.Count >= MaxMembers)
            {
                throw new InvalidOperationException($"The roster is full ({MaxMembers} members).");
            }

            _members.Add(new Member(_nextId++, 1, Fit(name)));
            if (name == FailingName)
            {
                // Simulates a precondition discovered after mutating; Structural() must roll back.
                throw new InvalidOperationException("Simulated failure after mutation.");
            }
        }

        private void MoveFirst(Member member)
        {
            _members.Remove(member);
            _members.Insert(0, member);
        }

        private ActionOutcome ClearAll()
        {
            _members.Clear();
            return new ActionOutcome { Undoable = false, StructureChanged = true, Message = "Roster cleared." };
        }

        private EnumSpec MemberSpec() =>
            new([.. _members.Select(m => new EnumOption(m.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), m.Name))]);

        private Member Require(FieldValue reference) =>
            Find(reference.AsString()) ?? throw new InvalidOperationException($"Member {reference} no longer exists.");

        private Member? Find(string id) => _members.FirstOrDefault(m => m.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) == id);

        private void Load(byte[] bytes)
        {
            if (bytes.Length < 8 || !bytes.AsSpan(0, 4).SequenceEqual(Magic))
            {
                throw new SaveFormatException("Not a roster save.");
            }

            var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6));
            if (count > MaxMembers || bytes.Length < 8 + (count * 16))
            {
                throw new SaveFormatException("Roster count is out of range.");
            }

            var members = new List<Member>(count);
            for (var i = 0; i < count; i++)
            {
                var at = 8 + (i * 16);
                members.Add(new Member(
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at)),
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 2)),
                    Encoding.UTF8.GetString(bytes.AsSpan(at + 4, 12)).TrimEnd('\0')));
            }

            _members = members;
            _nextId = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
            _trailer = bytes[(8 + (count * 16))..];
        }

        private byte[] ToBytes()
        {
            var bytes = new byte[8 + (_members.Count * 16) + _trailer.Length];
            Magic.CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), _nextId);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)_members.Count);
            for (var i = 0; i < _members.Count; i++)
            {
                var at = 8 + (i * 16);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), _members[i].Id);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 2), _members[i].Level);
                Encoding.UTF8.GetBytes(_members[i].Name).CopyTo(bytes, at + 4);
            }

            _trailer.CopyTo(bytes, bytes.Length - _trailer.Length);
            return bytes;
        }

        private static string Fit(string name) =>
            Encoding.UTF8.GetByteCount(name) <= 12 ? name : throw new ArgumentException("Name is longer than 12 bytes in UTF-8.");

        private sealed class Member(ushort id, ushort level, string name)
        {
            public ushort Id { get; } = id;
            public ushort Level { get; set; } = level;
            public string Name { get; set; } = name;
        }
    }
}
