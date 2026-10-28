using System.Text;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff;

internal static class ListDecodersCommand
{
    public static int Run(CliArguments args, TextWriter stdout, Func<DecoderResolver> resolver)
    {
        args.ExpectPositionals(0, "savediff list-decoders [--format text|json] [--out FILE]");
        var json = CliApp.WantsJson(args);
        var output = CliApp.OutputPath(args, []);
        var decoders = resolver().Available;
        if (json)
        {
            CliApp.Emit(output, stdout, DiffJson.Serialize(new { Format = "savetoolkit-decoders/1", Decoders = decoders }) + "\n");
        }
        else
        {
            var width = Math.Max("ID".Length, decoders.Max(d => d.Id.Length));
            var text = new StringBuilder().AppendLine($"{"ID".PadRight(width)}  KIND      PLUGIN");
            foreach (var decoder in decoders)
            {
                text.AppendLine($"{decoder.Id.PadRight(width)}  {decoder.Kind.PadRight(8)}  {decoder.PluginId ?? "—"}");
            }

            CliApp.Emit(output, stdout, text.ToString());
        }

        return CliApp.ExitSame;
    }
}
