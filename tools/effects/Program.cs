using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Voltage.Editor.Effects;

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "Voltage.Engine", "Voltage.Engine.csproj"))) root = root.Parent;
if (root == null) throw new DirectoryNotFoundException("Run this tool from the VoltageEngine source checkout.");
Directory.SetCurrentDirectory(root.FullName);
var version = XElement.Load("Voltage.Engine/Voltage.Engine.csproj").Descendants("PackageReference")
	.Single(p => (string)p.Attribute("Include") == "MonoGame.Framework.DesktopGL").Attribute("Version").Value;
if (args.Contains("--install-tool"))
{
	Console.WriteLine(EffectToolchain.EnsureTool(version, Console.WriteLine, CancellationToken.None));
	return;
}
const string sources = "Voltage.Editor/DefaultContent/Effects";
const string outputs = "Voltage.Engine/Graphics/Effects/Compiled";
bool verify = args.Contains("--verify");
var compiler = verify ? null : args.FirstOrDefault(a => a.StartsWith("--compiler="))?.Substring(11) ??
	EffectToolchain.EnsureTool(version, Console.WriteLine, CancellationToken.None);
if (compiler != null) compiler = Path.GetFullPath(compiler);
var project = new XElement("Project", new XElement("PropertyGroup", new XElement("BundledEffectsCompilerVersion", version)));
var inputs = new XElement("ItemGroup");
foreach (var source in Directory.GetFiles(sources, "*.fx", SearchOption.AllDirectories).OrderBy(s => s, StringComparer.Ordinal))
{
	var output = Path.Combine(outputs, Path.ChangeExtension(Path.GetRelativePath(sources, source), ".mgfxo"));
	if (!verify)
		EffectToolchain.Compile(compiler, source.Replace('\\', '/'), output, EffectToolchain.Fingerprint(source, version), Console.WriteLine, CancellationToken.None);
	if (!EffectToolchain.IsDesktopGl(File.ReadAllBytes(output))) throw new InvalidDataException(output);
	foreach (var file in new[] { source, output })
	{
		var relative = Path.GetRelativePath(outputs, file).Replace('\\', '/');
		var bytes = File.ReadAllBytes(file);
		var text = file == source ? Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n") : null;
		inputs.Add(new XElement("_BundledEffectInput", new XAttribute("Include", "$(MSBuildThisFileDirectory)" + relative),
			new XElement("ExpectedHash", Convert.ToHexString(SHA256.HashData(text == null ? bytes : Encoding.UTF8.GetBytes(text)))),
			new XElement("WindowsHash", Convert.ToHexString(SHA256.HashData(text == null ? bytes : Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n")))))));
	}
}
project.Add(inputs);
var manifest = Path.Combine(outputs, "manifest.props");
if (verify)
{
	if (!XNode.DeepEquals(XElement.Load(manifest), project)) throw new InvalidDataException("Bundled effects are stale; rebuild with tools/effects/Voltage.Effects.Build.csproj.");
	Console.WriteLine("All 34 bundled DesktopGL effects match their source and compiler manifest.");
}
else
{
	project.Save(manifest);
	foreach (var stamp in Directory.GetFiles(outputs, "*.sha256", SearchOption.AllDirectories)) File.Delete(stamp);
}
