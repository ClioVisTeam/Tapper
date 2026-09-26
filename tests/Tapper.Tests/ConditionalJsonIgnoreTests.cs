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
    [InlineData("Always", false)]
    [InlineData("Never", true)]
    [InlineData("WhenWritingNull", true)]
    [InlineData("WhenWritingDefault", true)]
    public void Naming_does_not_override_ignore_regardless_of_attribute_order(string condition, bool included)
    {
        var ignore = $"[JsonIgnore(Condition = JsonIgnoreCondition.{condition})]";
        const string rename = "[JsonPropertyName(\"renamed\")]";
        var first = Generate($"{ignore} {rename} public string? Value {{ get; set; }}");
        var second = Generate($"{rename} {ignore} public string? Value {{ get; set; }}");
        Assert.Equal(first, second);
        Assert.Equal(included, first.Contains("renamed?: string;", StringComparison.Ordinal));
        Assert.DoesNotContain("value", first);
    }

    [Fact]
    public void Nullable_union_arm_is_optional_and_unconditional_secret_is_excluded()
    {
        var code = Generate("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? StringValue { get; set; } [JsonIgnore] public string PasswordHash { get; set; } = \"\";");
        Assert.Contains("stringValue?: string;", code);
        Assert.DoesNotContain("passwordHash", code);
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
