using System.Security.Cryptography;
using System.Text;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

/// <summary>Reproducible hypotheses, not a package-integrity validator.</summary>
internal sealed class DigestExperiments(byte[] bytes)
{
    private readonly Dictionary<string, string> _results = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, string> Run()
    {
        var package = LspkPackage.Read(bytes);
        var entries = package.Entries;
        var expanded = entries.Select(package.Extract).ToArray();
        var stored = entries.Select(e => bytes.AsSpan(e.Offset, e.StoredSize).ToArray()).ToArray();
        int[][] orders = [Enumerable.Range(0, entries.Count).ToArray(),
            Enumerable.Range(0, entries.Count).OrderBy(i => entries[i].Offset).ToArray(),
            Enumerable.Range(0, entries.Count).OrderBy(i => entries[i].Name, StringComparer.Ordinal).ToArray(),
            Enumerable.Range(0, entries.Count).OrderBy(i => entries[i].Name, StringComparer.OrdinalIgnoreCase).ToArray()];
        string[] orderNames = ["list", "offset", "ordinal", "ignore-case"];
        foreach (var (payloads, kind) in new[] { (expanded, "expanded"), (stored, "stored") })
        {
            for (var order = 0; order < orders.Length; order++)
            {
                foreach (var reverse in new[] { false, true })
                {
                    var indices = reverse ? orders[order].Reverse().ToArray() : orders[order];
                    var prefix = $"{kind}/{orderNames[order]}/{(reverse ? "reverse" : "forward")}";
                    Add(prefix, indices.Select(i => payloads[i]));
                    Add(prefix + "/input+1", indices.Select(i => payloads[i].Select(b => unchecked((byte)(b + 1))).ToArray()));
                    foreach (var nameStyle in new[] { "utf8", "nul", "lower-nul", "backslash-nul" })
                    {
                        Add(prefix + "/names-" + nameStyle, indices.SelectMany(i => new[]
                        {
                            Encoding.UTF8.GetBytes(Name(entries[i].Name, nameStyle)), payloads[i],
                        }));
                    }

                    foreach (var increment in new[] { false, true })
                    {
                        var hashes = indices.Select(i => Hash([payloads[i]])).ToArray();
                        if (increment)
                        {
                            hashes = hashes.Select(h => h.Select(b => unchecked((byte)(b + 1))).ToArray()).ToArray();
                        }

                        Add(prefix + $"/entry-md5/{increment}", hashes);
                        Add(prefix + $"/entry-md5-hex/{increment}", hashes.Select(h => Encoding.ASCII.GetBytes(Convert.ToHexString(h).ToLowerInvariant())));
                    }
                }
            }

            // All nonempty subsets in file-list order: omitted metadata/thumbnail hypotheses.
            for (var subset = 1; subset < 1 << entries.Count; subset++)
            {
                Add($"{kind}/subset/{subset:X}", Enumerable.Range(0, entries.Count)
                    .Where(i => (subset & (1 << i)) != 0).Select(i => payloads[i]));
            }
        }

        var listOffset = BinaryData.Size(BinaryData.U64(bytes, LspkLayout.FileListOffset));
        var listSize = BinaryData.Size(BinaryData.U32(bytes, LspkLayout.FileListSize));
        var list = bytes.AsSpan(listOffset, listSize).ToArray();
        Add("file-list/preamble", [list]);
        Add("file-list/compressed", [list[LspkLayout.FileListPreambleSize..]]);
        Add("file-list/expanded", [LarianCompression.Decode(list.AsSpan(LspkLayout.FileListPreambleSize),
            entries.Count * LspkLayout.Entry18Size, (byte)LarianCompressionMethod.Lz4)]);
        Add("payload-area/padding", [bytes[LspkLayout.Header16Size..listOffset]]);
        var zeroed = bytes.ToArray();
        zeroed.AsSpan(LspkLayout.Digest, LspkLayout.DigestSize).Clear();
        Add("header/zeroed", [zeroed[..LspkLayout.Header16Size]]);
        Add("whole/zeroed", [zeroed]);
        Add("whole/without-digest", [bytes[..LspkLayout.Digest], bytes[(LspkLayout.Digest + LspkLayout.DigestSize)..]]);
        Add("whole/original", [bytes]);
        return _results;
    }

    private void Add(string name, IEnumerable<byte[]> segments)
    {
        var hash = Hash(segments);
        _results.Add(name, Convert.ToHexString(hash));
        _results.Add(name + "/digest+1", Convert.ToHexString(hash.Select(b => unchecked((byte)(b + 1))).ToArray()));
    }

    private static byte[] Hash(IEnumerable<byte[]> segments)
    {
        // MD5 is a legacy format hypothesis, never a security primitive here.
#pragma warning disable CA5351
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
#pragma warning restore CA5351
        foreach (var segment in segments)
        {
            hash.AppendData(segment);
        }

        return hash.GetHashAndReset();
    }

    private static string Name(string name, string style) => style switch
    {
        "nul" => name + '\0',
        "lower-nul" => name.ToLowerInvariant() + '\0',
        "backslash-nul" => name.Replace('/', '\\') + '\0',
        _ => name,
    };
}
