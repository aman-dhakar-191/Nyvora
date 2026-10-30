using System.Reflection;
using System.Text;
using Nyvora.Mcp;

// stdout carries the protocol and nothing else; everything human-readable goes to stderr.
var allowWrite = false;
var allowDelete = false;
string? dataDir = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--allow-write":
            allowWrite = true;
            break;
        case "--allow-delete":
            allowDelete = true;
            break;
        case "--data-dir" when i + 1 < args.Length:
            dataDir = args[++i];
            break;
        case "--help" or "-h" or "/?":
            Console.Error.WriteLine(
                "nyvora-mcp: MCP server (stdio) for the Nyvora task manager.\n" +
                "  --allow-write    let agents add, edit, complete and tag tasks (default: read-only)\n" +
                "  --allow-delete   also let agents delete tasks (implies --allow-write)\n" +
                "  --data-dir PATH  use another data folder (default: %APPDATA%\\Nyvora)");
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]} (try --help)");
            return 2;
    }
}

if (dataDir is null)
{
    Nyvora.Core.Storage.DataFolders.MigrateFromLocal(); // the app may not have run since the data moved
    dataDir = Nyvora.Core.Storage.DataFolders.Active();
}
var version = (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0").Split('+')[0];

var tools = new NyvoraTools(dataDir, allowWrite, allowDelete);
Console.Error.WriteLine($"nyvora-mcp {version} ready ({(allowDelete ? "read, write, delete" : allowWrite ? "read, write" : "read-only")}), data: {dataDir}");

var utf8 = new UTF8Encoding(false);
using var input = new StreamReader(Console.OpenStandardInput(), utf8);
using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true, NewLine = "\n" };
new McpServer(tools, version).Run(input, output);
return 0;
