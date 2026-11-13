using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// That every model really is source-generated, and that making it so changed nothing on the wire
/// (task <c>d24777f5</c>).
/// </summary>
/// <remarks>
/// A <c>JsonSerializerContext</c> is the one optimisation that cannot fail loudly. A type it was
/// not told about does not break: it falls through to the reflecting resolver and works exactly as
/// before, a little slower, for ever. So "we added a context" and "the app stopped reflecting over
/// its models" are different claims, and only a test that enumerates the assembly can tell them
/// apart.
/// </remarks>
public sealed class ModelJsonContextTests
{
    /// <summary>
    /// Every public record in the bindings assembly, found the way the runtime would find it
    /// rather than from a list someone maintains.
    /// </summary>
    /// <remarks>
    /// A record is identified by its compiler-generated clone method — the one member every record
    /// has and no hand-written class does. Asking <c>IsRecord</c> directly is not possible from
    /// reflection; this is what the C# compiler itself emits to mark one.
    /// </remarks>
    private static IEnumerable<Type> PublicRecords() =>
        typeof(TaskRow).Assembly
            .GetExportedTypes()
            .Where(type => type is { IsClass: true, IsGenericTypeDefinition: false })
            .Where(type => type.GetMethod(
                "<Clone>$",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null)
            .OrderBy(type => type.Name, StringComparer.Ordinal);

    /// <summary>
    /// Nothing in this assembly is left to reflection — in either of the two forms it is read in.
    /// </summary>
    /// <remarks>
    /// <c>List&lt;T&gt;</c> as well as <c>T</c>, because <c>ResponseReader.ReadArray&lt;T&gt;</c>
    /// is how a list of rows arrives and an unregistered <c>List&lt;T&gt;</c> sends the whole graph
    /// beneath it back to the reflecting resolver — the element type's own registration never gets
    /// consulted. Registering one and not the other is the shape of mistake that looks finished.
    /// </remarks>
    [Fact]
    public void Every_model_in_the_bindings_is_registered_for_source_generation_task_d24777f5()
    {
        var records = PublicRecords().ToArray();
        Assert.True(records.Length > 100,
            $"only {records.Length} records found, so this test is not looking where it thinks it is");

        var missing = records
            .SelectMany(type => new[] { type, typeof(List<>).MakeGenericType(type) })
            .Where(type => ModelJsonContext.Default.GetTypeInfo(type) is null)
            .Select(type => type.IsGenericType
                ? $"List<{type.GetGenericArguments()[0].Name}>"
                : type.Name)
            .ToArray();

        Assert.True(missing.Length == 0,
            "these shapes are not in ModelJsonContext, so they are still worked out by reflection "
            + "on first use. Add a [JsonSerializable] line for each to "
            + "app/Astrid.Core.Bindings/ModelJsonContext.cs:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>The generated metadata is what the shared options reach for first.</summary>
    /// <remarks>
    /// A context nothing is wired to is dead code that compiles. The order matters as much as the
    /// presence: reflection has an answer for every type, so a chain with it in front would make
    /// the context unreachable while leaving both tests above perfectly green.
    /// </remarks>
    [Fact]
    public void The_shared_options_ask_the_generated_metadata_first_task_d24777f5()
    {
        Assert.Same(ModelJsonContext.Default, CommandJson.Options.TypeInfoResolverChain[0]);
        Assert.Equal(2, CommandJson.Options.TypeInfoResolverChain.Count);
    }

    /// <summary>
    /// And the fallback behind it still answers for what the context cannot name.
    /// </summary>
    /// <remarks>
    /// Setting any resolver turns off the implicit reflection default, so this is the assertion
    /// that the command records — private to <see cref="Commands"/>, unnameable from a context
    /// outside it — did not quietly become a <c>NotSupportedException</c> on the first gesture a
    /// user makes.
    /// </remarks>
    [Fact]
    public void A_type_the_context_cannot_name_still_serialises_task_d24777f5()
    {
        Assert.Contains("\"kind\":\"task\"",
            JsonSerializer.Serialize(Commands.Task("t1"), CommandJson.Options),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every model has the same JSON shape generated as reflection would have discovered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parity assertion, and the reason this change is safe to make in one commit over 111
    /// types. The generator reads its naming and null handling from its own attribute rather than
    /// from the options it is used with, so the two can disagree — and the symptom would be a
    /// field named differently on one screen, which the core answers by ignoring the field it does
    /// not recognise. Silence, not an error.
    /// </para>
    /// <para>
    /// Compared as property names rather than by serialising anything, so it covers all 111
    /// shapes rather than the handful that can be constructed in a test — and so that it never
    /// calls a getter. Several models carry computed properties for the UI
    /// (<c>TaskRow.LeadingIsCopy</c> and its neighbours) that throw on a default instance; they
    /// did so through the reflecting resolver too, because nothing has ever serialised a model in
    /// this direction. That is a pre-existing sharp edge, unchanged here and out of this task's
    /// scope — this test asserts the shapes match, including those properties, not that either
    /// resolver can write one.
    /// </para>
    /// </remarks>
    [Fact]
    public void Each_model_has_the_same_shape_generated_as_reflection_found_task_d24777f5()
    {
        var reflecting = new DefaultJsonTypeInfoResolver();
        var differences = new List<string>();

        foreach (var type in PublicRecords())
        {
            var generated = Names(ModelJsonContext.Default.GetTypeInfo(type));
            var discovered = Names(reflecting.GetTypeInfo(type, CommandJson.Options));

            if (!generated.SequenceEqual(discovered, StringComparer.Ordinal))
            {
                differences.Add(
                    $"{type.Name}: generated [{string.Join(", ", generated)}] "
                    + $"but reflection found [{string.Join(", ", discovered)}]");
            }
        }

        Assert.True(differences.Count == 0,
            "the generated metadata does not describe the same JSON as reflection did, so this "
            + "change is not the no-op it is meant to be:\n  "
            + string.Join("\n  ", differences));

        static IEnumerable<string> Names(JsonTypeInfo? info) =>
            (info?.Properties ?? [])
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
    }

    /// <summary>And a model still reads the camelCase the core sends.</summary>
    [Fact]
    public void A_model_still_reads_camel_case_task_d24777f5()
    {
        var row = JsonSerializer.Deserialize<TaskRow>(
            "{\"id\":\"t1\",\"title\":\"Buy oat milk\",\"showsIdentifier\":true}",
            CommandJson.Options);

        Assert.NotNull(row);
        Assert.Equal("t1", row!.Id);
        Assert.Equal("Buy oat milk", row.Title);
        Assert.True(row.ShowsIdentifier);
        Assert.Null(row.Identifier);
    }

    /// <summary>And a list of them, which is the path a drawn list actually takes.</summary>
    [Fact]
    public void A_list_of_models_reads_through_the_generated_path_task_d24777f5()
    {
        var rows = AstridResponse
            .Parse("{\"ok\":true,\"value\":[{\"id\":\"t1\",\"title\":\"One\"},"
                   + "{\"id\":\"t2\",\"title\":\"Two\"}]}")
            .ReadArray<TaskRow>();

        Assert.Equal(2, rows.Count);
        Assert.Equal("t2", rows[1].Id);
    }
}
