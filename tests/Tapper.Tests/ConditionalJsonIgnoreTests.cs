using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Tapper.Tests;

public class ConditionalJsonIgnoreTests
{
    [Theory]
    [InlineData("", false, false)]
    [InlineData("Always", false, false)]
    [InlineData("Never", true, false)]
    [InlineData("WhenWritingNull", true, true)]
    [InlineData("WhenWritingDefault", true, true)]
    public void Json_ignore_condition_controls_inclusion_and_omission(string condition, bool included, bool optional)
    {
        var attribute = condition.Length == 0 ? "[JsonIgnore]" : $"[JsonIgnore(Condition = JsonIgnoreCondition.{condition})]";
        var type = condition == "WhenWritingNull" ? "string" : "int";
        var mappedType = type == "string" ? "string" : "number";
        var code = Generate($"{attribute} public {type} Value {{ get; set; }}");
        if (included)
            Assert.Contains($"value{(optional ? "?" : "")}: {mappedType};", code);
        else
            Assert.DoesNotContain("value", code);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Always", false)]
    [InlineData("Never", true)]
    [InlineData("WhenWritingNull", true)]
    [InlineData("WhenWritingDefault", true)]
    public void Naming_does_not_override_ignore_regardless_of_attribute_order(string condition, bool included)
    {
        var ignore = condition.Length == 0 ? "[JsonIgnore]" : $"[JsonIgnore(Condition = JsonIgnoreCondition.{condition})]";
        const string rename = "[JsonPropertyName(\"renamed\")]";
        var first = Generate($"{ignore} {rename} public string? Value {{ get; set; }}");
        var second = Generate($"{rename} {ignore} public string? Value {{ get; set; }}");
        Assert.Equal(first, second);
        var optional = condition == "Never" ? "" : "?";
        Assert.Equal(included, first.Contains($"renamed{optional}: (string | null);", StringComparison.Ordinal));
        Assert.DoesNotContain("value", first);
    }

    [Theory]
    [InlineData("string?", "string", "{ get; set; }")]
    [InlineData("System.Guid?", "string", "{ get; set; }")]
    [InlineData("int?", "number", "{ get; set; }")]
    [InlineData("int?", "number", ";")]
    public void Never_nullable_member_is_required_and_preserves_null(string type, string mappedType, string suffix)
    {
        var code = Generate($"[JsonIgnore(Condition = JsonIgnoreCondition.Never)] public {type} Value {suffix}");
        Assert.Contains($"value: ({mappedType} | null);", code);
        Assert.DoesNotContain("value?:", code);
    }

    [Fact]
    public void Nullable_union_arm_is_optional_and_unconditional_secret_is_excluded()
    {
        var code = Generate("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? StringValue { get; set; } [JsonIgnore] public string PasswordHash { get; set; } = \"\";");
        Assert.Contains("stringValue?: (string | null);", code);
        Assert.DoesNotContain("passwordHash", code);
    }

    [Fact]
    public void Conditionally_omitted_nullable_value_preserves_null_for_serialization()
    {
        var code = Generate("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public System.Guid? GuidValue { get; set; } [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int? NumberValue { get; set; }");
        Assert.Contains("guidValue?: (string | null);", code);
        Assert.Contains("numberValue?: (number | null);", code);
    }

    private static string Generate(string members)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ConditionalIgnoreFixture",
            new[] { CSharpSyntaxTree.ParseText($"#nullable enable\nusing System.Text.Json.Serialization; public class Fixture {{ {members} }}") },
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var options = new TranspilationOptions(compilation, SerializerOption.Json, NamingStyle.CamelCase,
            EnumStyle.Value, NewLineOption.Lf, 4, false, true);
        var writer = new CodeWriter();
        new TypeScriptCodeGenerator(compilation, options).AddType(compilation.GetTypeByMetadataName("Fixture")!, ref writer);
        return writer.ToString();
    }
}
