using System.Security.Cryptography;
using System.Text;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>Compact view keys retain complete source identity while fitting F9 navigation bounds.</summary>
internal static class RecordIdentity
{
    internal static string Compact(string kind, string identity) =>
        kind + "/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
}
