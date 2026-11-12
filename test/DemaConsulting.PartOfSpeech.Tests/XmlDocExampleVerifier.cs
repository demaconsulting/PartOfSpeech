using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     Compiles every <c>&lt;example&gt;&lt;code&gt;</c> block that the package publishes in its XML documentation
///     file, so that a documented example cannot describe an API the code does not have.
/// </summary>
/// <remarks>
///     <para>
///     The documented examples are the assembly instructions that a coding agent follows to build an application
///     against PartOfSpeech, and an example that does not compile is worse than no example at all: it is a
///     confident, plausible-looking instruction that fails only once the reader has acted on it. The examples are
///     therefore verified against the real compiled API rather than reviewed by eye.
///     </para>
///     <para>
///     The <em>shipped XML documentation file</em> is the subject, not the source files. That is what a consumer's
///     IDE shows, what the packaged API reference is generated from, and what therefore has to be true. Reading it
///     back from the build output also proves that the package actually emits documentation at all.
///     </para>
///     <para>
///     A snippet is written for a reader, not for a compiler, so there is no single correct way to wrap it. Four
///     candidate wrappings are tried (a whole file, a whole executable file, a class body and a method body) and
///     the snippet passes if any of them compiles. When none does, the diagnostics of the <em>best</em> candidate
///     (the one with the fewest errors) are reported, because those describe the real mistake rather than the
///     consequences of a wrapping that never fitted.
///     </para>
///     <para>
///     Convention: a snippet may use a variable named <c>tagger</c> of type <see cref="Tagger"/> without declaring
///     it, because every page that shows tagging has the construction of a tagger on its own example (a weights
///     file that cannot ship with the documentation). The method-body wrapping declares that variable as a parameter.
///     This verification only compiles the examples; running them against real models is covered by the tagger
///     tests.
///     </para>
///     <para>
///     This file is modeled on the verifier of the sibling WordSense repository and is a copy in this repository's
///     test project rather than shared through a project reference.
///     </para>
/// </remarks>
internal static class XmlDocExampleVerifier
{
    /// <summary>
    ///     The usings that every snippet is compiled with, so that examples show composition rather than boilerplate
    ///     that a reader would have to mentally discard.
    /// </summary>
    private static readonly string[] CommonUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Threading",
        "System.Threading.Tasks"
    ];

    /// <summary>
    ///     Compiles every documented example in a package's XML documentation file and fails the calling test if any
    ///     of them does not compile.
    /// </summary>
    /// <remarks>
    ///     Finding no examples is itself a failure. A missing or empty documentation file would otherwise let this
    ///     verification pass while proving nothing, which is exactly the silent false confidence that it exists to
    ///     prevent.
    /// </remarks>
    /// <param name="xmlDocumentationFileName">
    ///     The file name (not path) of the package's XML documentation file as it appears in the test output folder,
    ///     for example <c>DemaConsulting.PartOfSpeech.xml</c>.
    /// </param>
    /// <param name="additionalUsings">
    ///     The package namespaces that a snippet may use without writing a using directive, for example the
    ///     namespace of the type being documented.
    /// </param>
    public static void VerifyExamples(string xmlDocumentationFileName, params string[] additionalUsings)
    {
        var documentationPath = Path.Combine(AppContext.BaseDirectory, xmlDocumentationFileName);
        Assert.True(
            File.Exists(documentationPath),
            $"The XML documentation file '{xmlDocumentationFileName}' was not found in the test output "
            + $"folder '{AppContext.BaseDirectory}'. The package must build with "
            + "GenerateDocumentationFile enabled for its examples to be verifiable.");

        var examples = ExtractExamples(documentationPath);
        Assert.True(
            examples.Count > 0,
            $"No <example><code> blocks were found in '{xmlDocumentationFileName}'. The package is "
            + "expected to document its consumer-facing API with compilable examples.");

        var references = BuildReferences();
        var prologue = BuildPrologue(additionalUsings);

        var failures = examples
            .Select(example => TryCompile(example, prologue, references))
            .Where(failure => failure is not null)
            .ToList();

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {examples.Count} documented example(s) in "
            + $"'{xmlDocumentationFileName}' do not compile against the real API:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    ///     Compiles one example that is not part of the XML documentation file, for example a code block of the user
    ///     guide, with the same wrappings and references as the documented examples, and fails the calling test if it
    ///     does not compile.
    /// </summary>
    /// <param name="source">The name of the document that holds the snippet, for the failure message.</param>
    /// <param name="snippet">The snippet as written in the document.</param>
    /// <param name="additionalUsings">The package namespaces that the snippet may use without a using directive.</param>
    public static void VerifySnippet(string source, string snippet, params string[] additionalUsings)
    {
        Assert.False(string.IsNullOrWhiteSpace(snippet), $"No example was found in '{source}'.");

        var failure = TryCompile(Dedent(snippet), BuildPrologue(additionalUsings), BuildReferences());
        Assert.True(failure is null, $"The example in '{source}' does not compile against the real API:{failure}");
    }

    /// <summary>
    ///     Reads every <c>&lt;example&gt;&lt;code&gt;</c> block out of an XML documentation file.
    /// </summary>
    /// <remarks>
    ///     Only <c>&lt;code&gt;</c> content is taken. Prose inside an <c>&lt;example&gt;</c> explains the snippet and
    ///     is not itself code, so compiling it would report failures that say nothing about the API.
    /// </remarks>
    /// <param name="documentationPath">The full path of the XML documentation file.</param>
    /// <returns>The body of each documented example, with its comment indentation removed, in document order.</returns>
    private static List<string> ExtractExamples(string documentationPath)
    {
        var document = XDocument.Load(documentationPath);

        return document
            .Descendants("example")
            .Elements("code")
            .Select(code => Dedent(code.Value))
            .Where(snippet => !string.IsNullOrWhiteSpace(snippet))
            .ToList();
    }

    /// <summary>
    ///     Removes the common leading indentation that an XML documentation comment adds to every line of a snippet.
    /// </summary>
    /// <remarks>
    ///     The indentation is an artifact of the comment the snippet was written in, not of the snippet. Leaving it
    ///     in place compiles correctly but makes every reported diagnostic column misleading.
    /// </remarks>
    /// <param name="code">The raw text of a <c>&lt;code&gt;</c> element.</param>
    /// <returns>The snippet with its common indentation and surrounding blank lines removed.</returns>
    private static string Dedent(string code)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');

        var indent = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        var stripped = lines.Select(line =>
            string.IsNullOrWhiteSpace(line) ? string.Empty : line.Substring(indent));

        return string.Join(Environment.NewLine, stripped).Trim();
    }

    /// <summary>
    ///     Builds the using directives that every snippet is compiled with.
    /// </summary>
    /// <param name="additionalUsings">The package namespaces to add to the common set.</param>
    /// <returns>The prologue text, ending with a line break.</returns>
    private static string BuildPrologue(IEnumerable<string> additionalUsings)
    {
        var builder = new StringBuilder();

        foreach (var name in CommonUsings.Concat(additionalUsings).Distinct(StringComparer.Ordinal))
        {
            builder.AppendLine($"using {name};");
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Collects the metadata references that a snippet is compiled against.
    /// </summary>
    /// <remarks>
    ///     The test process's own trusted-platform assembly list is used, because it already contains exactly the
    ///     framework and package assemblies that the subject package was built against. References are deduplicated
    ///     by simple name: the list can legitimately contain two files that provide one assembly, which the compiler
    ///     reports as a duplicate-reference error that has nothing to do with the snippet. A host that does not
    ///     publish that list (.NET Framework) still has every assembly it loaded beside the test binary, plus the
    ///     core library of the running framework, which is an equivalent source.
    /// </remarks>
    /// <returns>The deduplicated metadata references.</returns>
    private static List<MetadataReference> BuildReferences()
    {
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)
            ?.Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrEmpty(path))
            .ToList();

        if (paths is null || paths.Count == 0)
        {
            // The package targets netstandard2.0, whose facade lives beside the core library of the framework
            // together with System.Core (LINQ) and the other framework assemblies a snippet may use.
            var runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            paths = Directory.GetFiles(runtimeDirectory, "*.dll")
                .Where(path => IsFrameworkAssembly(Path.GetFileNameWithoutExtension(path)) && IsManaged(path))
                .ToList();
            paths.AddRange(Directory.GetFiles(AppContext.BaseDirectory, "*.dll"));
        }

        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths.Where(path => seen.Add(SimpleName(path))))
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }

        return references;
    }

    /// <summary>
    ///     Determines whether a file name in the runtime directory of .NET Framework is a framework assembly that a
    ///     snippet may reference.
    /// </summary>
    /// <param name="name">The file name without extension.</param>
    /// <returns><see langword="true"/> for the core library, the facade and the <c>System</c> assemblies.</returns>
    private static bool IsFrameworkAssembly(string name) =>
        name == "mscorlib" || name == "netstandard" || name.StartsWith("System", StringComparison.Ordinal);

    /// <summary>
    ///     Determines whether a file is a managed assembly.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <returns><see langword="true"/> when the file can be read as a managed assembly.</returns>
    private static bool IsManaged(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException
                                              or IOException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Determines the assembly simple name that a file provides, used to deduplicate references.
    /// </summary>
    /// <remarks>
    ///     The file name is used when the file cannot be read as a managed assembly (a native library beside the test
    ///     binary, for instance) so that such a file is still counted once and never crashes the collection.
    /// </remarks>
    /// <param name="path">The full path of a candidate reference.</param>
    /// <returns>The assembly simple name, or the file name when it cannot be determined.</returns>
    private static string SimpleName(string path)
    {
        try
        {
            return AssemblyName.GetAssemblyName(path).Name ?? Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException
                                              or IOException or ArgumentException)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
    }

    /// <summary>
    ///     Compiles one snippet, trying each candidate wrapping in turn.
    /// </summary>
    /// <param name="snippet">The snippet body, with its comment indentation already removed.</param>
    /// <param name="prologue">The using directives to compile it with.</param>
    /// <param name="references">The metadata references to compile it against.</param>
    /// <returns>
    ///     <see langword="null"/> when some candidate compiles; otherwise a report that names the snippet and the
    ///     errors of the best candidate.
    /// </returns>
    private static string? TryCompile(string snippet, string prologue, List<MetadataReference> references)
    {
        // Three shapes a reader may reasonably have written: a whole file, a member declaration, or a sequence of
        // statements. The method body is declared async so that a snippet may await without the wrapping itself
        // being the reason it fails, and it receives the tagger that the examples of the tagging pages assume.
        string[] candidates =
        [
            prologue + snippet,
            prologue + snippet,
            prologue + "internal class __Wrap\n{\n" + snippet + "\n}\n",
            prologue + "internal class __Wrap\n{\n    private async Task __M(Tagger tagger)\n    {\n"
                + snippet + "\n    }\n}\n"
        ];

        List<Diagnostic>? best = null;

        for (var index = 0; index < candidates.Length; index++)
        {
            // The second candidate is the whole file again, compiled as an executable, because a file of top-level
            // statements is only valid in one.
            var kind = index == 1 ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary;
            var errors = CompileErrors(candidates[index], references, kind);
            if (errors.Count == 0)
            {
                return null;
            }

            // Keep the closest fit, because the diagnostics of a wrapping that never suited the snippet describe
            // the wrapping rather than the defect.
            if (best is null || errors.Count < best.Count)
            {
                best = errors;
            }
        }

        var reported = string.Join(
            Environment.NewLine,
            best!.Select(error => "    " + error.GetMessage(CultureInfo.InvariantCulture)));

        return Environment.NewLine
               + "--- example ---" + Environment.NewLine
               + snippet + Environment.NewLine
               + "--- errors ---" + Environment.NewLine
               + reported;
    }

    /// <summary>
    ///     Compiles one candidate and returns its errors.
    /// </summary>
    /// <remarks>
    ///     Only errors are collected. Warnings such as an unused local are normal in an illustrative snippet and say
    ///     nothing about whether the API it shows is real.
    /// </remarks>
    /// <param name="source">The complete candidate source text.</param>
    /// <param name="references">The metadata references to compile against.</param>
    /// <param name="kind">The kind of assembly to produce.</param>
    /// <returns>The error diagnostics, empty when the candidate compiles.</returns>
    private static List<Diagnostic> CompileErrors(string source, List<MetadataReference> references, OutputKind kind)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));

        var compilation = CSharpCompilation.Create(
            "__XmlDocExampleVerification",
            [tree],
            references,
            new CSharpCompilationOptions(
                kind,
                nullableContextOptions: NullableContextOptions.Enable));

        return compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
    }
}
