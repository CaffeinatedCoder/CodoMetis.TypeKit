// The generated axes of .claude/probe-axes.md: every public settable property of each options type a
// TypeKit feature reads, by reflection, never from memory. It runs outside the repository, so neither
// global.json nor central package management applies to it:
//
//   `prober replay` (tools/Prober) runs it (copied outside the repository, its #:package versions taken from
//   Directory.Packages.props) whenever a dependency input changes, into the prober's axes-generated.json.
//
// Each property is an axis "<prefix>.<Name>" whose values are "default" and every other value a bool
// or an enum allows (each flag of a [Flags] enum on its own). A property of any other type is listed
// under "needsValues"; probe-axes.md's "hand" map gives its values.
#:sdk Microsoft.NET.Sdk.Web
#:property TargetFramework=net10.0
#:property PublishAot=false
#:package Microsoft.AspNetCore.OpenApi@10.0.12
using System.Reflection;
using System.Text.Json;

var sources = new (string Prefix, object Instance)[]
{
    ("json", new JsonSerializerOptions()),
    ("openapi", new Microsoft.AspNetCore.OpenApi.OpenApiOptions()),
    ("mvc", new Microsoft.AspNetCore.Mvc.MvcOptions()),
};

var axes = new SortedDictionary<string, List<string>>();
var needsValues = new List<string>();
var versions = new SortedDictionary<string, string>();
foreach (var (prefix, instance) in sources)
{
    var type = instance.GetType();
    versions[type.FullName!] = type.Assembly.GetName().Version!.ToString();
    foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name))
    {
        if (p.SetMethod is not { IsPublic: true } && !IsCollection(p.PropertyType)) continue;
        if (p.GetCustomAttribute<ObsoleteAttribute>() is not null) continue;
        var name = $"{prefix}.{p.Name}";
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        var current = p.GetIndexParameters().Length == 0 ? p.GetValue(instance) : null;
        if (t == typeof(bool))
            axes[name] = ["default", (!(bool)(current ?? false)).ToString().ToLowerInvariant()];
        else if (t.IsEnum)
        {
            var flags = t.IsDefined(typeof(FlagsAttribute));
            var values = Enum.GetValues(t).Cast<object>()
                .Where(v => !Equals(v, current) && (!flags || IsSingleFlag(Convert.ToInt64(v))))
                .Select(v => v.ToString()!);
            axes[name] = ["default", .. values];
        }
        else needsValues.Add(name);
    }
}

Console.WriteLine(JsonSerializer.Serialize(new { versions, axes, needsValues }, new JsonSerializerOptions { WriteIndented = true }));

static bool IsSingleFlag(long v) => v != 0 && (v & (v - 1)) == 0;
static bool IsCollection(Type t) => t.IsGenericType && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>));
