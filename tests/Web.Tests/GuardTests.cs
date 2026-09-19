using System.Reflection;

namespace Snail.Toolkit.Web.Tests;

/// <summary>
/// Holds the core to the rules it is documented by.
/// </summary>
/// <remarks>
/// An invariant that lives only in a <c>&lt;remarks&gt;</c> is a wish. Three defects found in the audit of
/// 2026-09-13 were code breaking a rule written on the same screen: the block detector kept the generic word its
/// own paragraph explained it excluded, the content budget announced a length its own summary said it must
/// never misstate, and the registration methods were documented as idempotent while binding their settings
/// again on every call. Each is now covered by a test beside the behaviour it belongs to; these are the rules
/// with no single behaviour to hang a test on.
/// </remarks>
public class GuardTests
{
    private static readonly Assembly Core = typeof(Link).Assembly;

    /// <summary>The core knows about neither Playwright nor an HTTP client package.</summary>
    /// <remarks>
    /// The first of the dependency rules, and the one that decides the shape of the whole toolkit: a consumer
    /// that only wants links must never pay for a browser driver. <see cref="Identity.Proxied"/> is the stated
    /// exception and costs nothing, because <see cref="System.Net.Http.SocketsHttpHandler"/> ships with the
    /// framework and adds no package reference — which is exactly what this asserts.
    /// </remarks>
    [Theory]
    [InlineData("Microsoft.Playwright")]
    [InlineData("Microsoft.Extensions.Http")]
    public void Core_PackagesItWouldMakeEveryConsumerCarry_AreNotReferenced(string package)
        => Assert.DoesNotContain(
            Core.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith(package, StringComparison.Ordinal) is true);

    /// <summary>No type is named after the pattern it implements.</summary>
    /// <remarks>
    /// A name says what the thing is, never what pattern it implements. The suffixes below are the ones that
    /// get bolted on to make a name look like a type, and the toolkit was written without a single one of them;
    /// this is what keeps it that way when the next class is added in a hurry.
    /// </remarks>
    [Theory]
    [InlineData("Service")]
    [InlineData("Manager")]
    [InlineData("Helper")]
    [InlineData("Processor")]
    [InlineData("Data")]
    public void Core_TypeNamedAfterAPattern_DoesNotExist(string suffix)
        => Assert.DoesNotContain(
            Core.GetExportedTypes(),
            type => type.Name.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>
    /// The door to the parser is open to a consumer, and it carries the ceiling with it.
    /// </summary>
    /// <remarks>
    /// The thirteenth item on the consumer's list, and a guard rather than a behaviour test because the
    /// behaviour — markup too deep is refused rather than parsed — is already held by
    /// <see cref="HostilePageTextTests"/>. What was missing is that anyone outside this assembly could ask for
    /// it: the bot reads pages this library never fetched, ten of its probes parse them with a parser of their
    /// own, and every one of those parses pays the quadratic cost this type exists to bound. Extraction was
    /// public and a document was not, so a consumer with an XPath of its own had nowhere to go.
    /// </remarks>
    [Fact]
    public void Core_DoorToTheParser_IsOpenWithItsCeiling()
    {
        var door = Core.GetExportedTypes().SingleOrDefault(type => type.Name == "Markup");

        Assert.NotNull(door);
        Assert.NotNull(door.GetMethod("Read", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(door.GetField("MostNesting", BindingFlags.Public | BindingFlags.Static));
    }

    /// <summary>Every method that answers later is named for it and can be cancelled.</summary>
    /// <remarks>
    /// A method returning a task without a token is one a caller cannot get out of, and the crawl budget, the
    /// politeness gate and the retry pipeline all depend on cancellation reaching the bottom. Pure fan-out over
    /// a sequence is the one shape excused: <see cref="Streaming.Fanout"/> hands its token to the work it runs
    /// rather than taking one for itself.
    /// </remarks>
    [Fact]
    public void Core_MethodThatAnswersLater_IsNamedForItAndTakesAToken()
    {
        var offenders = Core.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(IsDeferred)
            .Where(method => !method.Name.EndsWith("Async", StringComparison.Ordinal)
                             || method.GetParameters().All(parameter => parameter.ParameterType != typeof(CancellationToken)))
            .Select(method => $"{method.DeclaringType?.Name}.{method.Name}")
            .ToArray();

        Assert.Empty(offenders);
    }

    private static bool IsDeferred(MethodInfo method)
        => method.ReturnType == typeof(Task)
           || method.ReturnType == typeof(ValueTask)
           || (method.ReturnType.IsGenericType
               && (method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                   || method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)));
}
