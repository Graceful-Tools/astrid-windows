using System.Reflection;
using System.Text.Json;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// What the models promise a caller, independent of how they are serialised (task
/// <c>2ba2c53e</c>, formerly <c>d24777f5</c>).
/// </summary>
public sealed class ModelContractTests
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
    /// A property the payload leaves out keeps the value its initializer gave it, across every
    /// model in the assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The models lean on this everywhere. <c>AccountSettings.DueOffsetChoices</c> and its three
    /// siblings are <c>= []</c> so a caller can count them without first asking whether they are
    /// there, and <c>TaskSettingsViewModel.ReplaceChoices</c> does exactly that; dozens of
    /// <c>string</c> properties are <c>= string.Empty</c> for the same reason. The core omits a
    /// field whenever it has nothing to say — a settings answer that is not the settings page
    /// carries no choice lists at all — so absence is the ordinary case, not an edge.
    /// </para>
    /// <para>
    /// <strong>This test exists because of what it caught.</strong> Task <c>d24777f5</c> added a
    /// <c>JsonSerializerContext</c> over these records, and System.Text.Json's source generator
    /// turns every <c>{ get; init; }</c> property into a synthetic constructor parameter and
    /// builds the record in a single object initializer — <c>ObjectCreator = null</c>. The
    /// parameterless constructor, which is where an initializer runs, is then never called, so
    /// every property the JSON omitted arrived as <c>default</c>. 47 tests failed with a
    /// <c>NullReferenceException</c> on the settings pages. Neither <c>[JsonConstructor]</c> on an
    /// explicit parameterless constructor nor <c>JsonSourceGenerationMode.Metadata</c> changes it;
    /// only turning all 493 <c>init</c> accessors into <c>set</c> does, because the generator can
    /// then assign properties after construction.
    /// </para>
    /// <para>
    /// The context was dropped rather than the models made mutable, and the decision is task
    /// <c>2ba2c53e</c>'s completion report. So this test passes today against the reflecting
    /// resolver, which honours initializers, and goes red the moment a resolver that does not is
    /// put in front of it. That is its whole job: the failure it guards against is silent, costs
    /// nothing at build time, and shows up as a null on whichever screen opens first.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_property_the_payload_omits_keeps_its_initializer_task_2ba2c53e()
    {
        var lost = new List<string>();
        var examined = 0;

        foreach (var type in PublicRecords())
        {
            // Only models the runtime can make on its own, and only from an empty object: this is
            // a question about absence, so the JSON says nothing at all.
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            object fresh;
            object? read;
            try
            {
                fresh = Activator.CreateInstance(type)!;
                read = JsonSerializer.Deserialize("{}", type, CommandJson.Options);
            }
            catch (Exception)
            {
                // A model that cannot be made empty is not what this test is about.
                continue;
            }

            if (read is null)
            {
                continue;
            }

            examined++;

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                // Only what deserialisation can actually assign. A computed property has no
                // setter, is not part of the contract, and several of them throw when read off an
                // empty model — which is the state this test deliberately creates.
                if (!property.CanRead || property.SetMethod is null
                    || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (property.GetValue(fresh) is null)
                {
                    continue;
                }

                if (property.GetValue(read) is null)
                {
                    lost.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        Assert.True(examined > 100,
            $"only {examined} models were read, so this test is not looking where it thinks it is");

        Assert.True(lost.Count == 0,
            "deserialising {} dropped an initialised property, so a payload that omits one now "
            + "reads as null where it used to read as the initializer — see this test's remarks "
            + "for the source-generator shape that causes it: " + string.Join(", ", lost));
    }
}
