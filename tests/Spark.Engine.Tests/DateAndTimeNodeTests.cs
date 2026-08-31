using System;
using System.Linq;
using System.Reflection;
using Spark.Api;
using Spark.Engine;
using Spark.Nodes.Core;

namespace Spark.Engine.Tests;

/// <summary>
/// The date and time nodes — `E4-T13`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The interesting half is not the arithmetic.</b> <see cref="DateAndTime.DaysInMonth"/> is a
/// call to the framework and testing it tests the framework. What is worth asserting is the part
/// that is about Spark: that a clock node is declared impure so the evaluation cache does not serve
/// its first answer forever, that seven outputs really are seven ports, and that a date is
/// formatted the same way on every machine that opens the graph.
/// </para>
/// </remarks>
public sealed class DateAndTimeNodeTests
{
    /// <summary>
    /// <b>A clock node must be declared a side effect, and this is the assertion that matters
    /// most.</b>
    /// </summary>
    /// <remarks>
    /// The evaluation cache is keyed by provenance rather than by value. <c>DateTime.Now</c> has no
    /// inputs, so without the declaration its key never changes: it is computed once and every run
    /// afterwards serves the first answer. A clock stopped at the moment it was placed, with
    /// nothing on screen to say so, is exactly the failure
    /// <see cref="NodeSideEffectAttribute"/> exists to prevent — and its own documentation names
    /// the clock as the case.
    /// </remarks>
    [Theory]
    [InlineData("DateTime.Now")]
    [InlineData("DateTime.Today")]
    public void AClockNodeIsDeclaredImpure(string name) =>
        Assert.True(
            Library.ByName(name).IsSideEffect,
            name + " reads the clock and is not declared a side effect, so it will be cached "
                + "and serve its first answer for the life of the session");

    /// <summary>And nothing else in the set is, because nothing else looks outside the graph.</summary>
    [Fact]
    public void NothingElseIsImpure()
    {
        string[] clocks = ["DateTime.Now", "DateTime.Today"];

        foreach (NodeDefinition definition in Library.Definitions())
        {
            if (!definition.DisplayName.StartsWith("DateTime.", StringComparison.Ordinal)
                && !definition.DisplayName.StartsWith("TimeSpan.", StringComparison.Ordinal))
            {
                continue;
            }

            if (clocks.Contains(definition.DisplayName, StringComparer.Ordinal))
            {
                continue;
            }

            Assert.False(
                definition.IsSideEffect,
                definition.DisplayName + " is declared impure but does not read the clock");
        }
    }

    /// <summary>
    /// <b>Seven <c>out</c> parameters are seven output ports</b>, named as they are written.
    /// </summary>
    [Fact]
    public void ComponentsHasOnePortPerPart()
    {
        NodeDefinition definition = Library.ByName("DateTime.Components");

        Assert.Equal(
            ["year", "month", "day", "hour", "minute", "second", "millisecond"],
            definition.Outputs.Select(port => port.Name));

        Assert.Equal("dateTime", Assert.Single(definition.Inputs).Name);
    }

    /// <summary>The same for a span, which reports parts rather than totals.</summary>
    [Fact]
    public void ASpanReportsPartsAndTotalsSeparately()
    {
        Duration.Components(
            TimeSpan.FromHours(36),
            out int days,
            out int hours,
            out int minutes,
            out int seconds,
            out int milliseconds);

        Assert.Equal(1, days);
        Assert.Equal(12, hours);
        Assert.Equal(0, minutes);
        Assert.Equal(0, seconds);
        Assert.Equal(0, milliseconds);

        // The distinction people get wrong, which is why both exist.
        Assert.Equal(36.0, Duration.TotalHours(TimeSpan.FromHours(36)));
        Assert.Equal(1.5, Duration.TotalDays(TimeSpan.FromHours(36)));
    }

    /// <summary>Building a date and taking it apart gives back what went in.</summary>
    [Fact]
    public void BuildingAndTakingApartRoundTrips()
    {
        DateTime built = DateAndTime.ByDateAndTime(2026, 9, 1, 14, 30, 15, 250);

        DateAndTime.Components(
            built,
            out int year,
            out int month,
            out int day,
            out int hour,
            out int minute,
            out int second,
            out int millisecond);

        Assert.Equal((2026, 9, 1, 14, 30, 15, 250), (year, month, day, hour, minute, second, millisecond));
    }

    /// <summary>
    /// <b>A date that does not exist is refused with the whole date in the message.</b> .NET's own
    /// exception says only that <c>day</c> was out of range, which does not tell somebody that
    /// April has no 31st.
    /// </summary>
    [Fact]
    public void AnImpossibleDateNamesItself()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => DateAndTime.ByDateAndTime(2026, 4, 31));

        Assert.Contains("2026-04-31", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Formatting is invariant.</b> A graph is a document: it is opened on other people's
    /// machines, and a date reading 03/04/2026 in London and 04/03/2026 in New York is a defect
    /// that only surfaces after the file has been shared.
    /// </summary>
    [Fact]
    public void FormattingDoesNotDependOnTheMachine()
    {
        DateTime moment = new(2026, 4, 3, 9, 5, 0, DateTimeKind.Local);

        Assert.Equal("2026-04-03", DateAndTime.Format(moment, "yyyy-MM-dd"));
        Assert.Equal("2026-04-03T09:05:00", DateAndTime.Format(moment, format: ""));
    }

    /// <summary>And reading one back is invariant too, which is the half that can silently differ.</summary>
    [Fact]
    public void ParsingDoesNotDependOnTheMachine()
    {
        DateTime parsed = DateAndTime.FromString("2026-04-03T09:05:00");

        Assert.Equal(new DateTime(2026, 4, 3, 9, 5, 0, DateTimeKind.Unspecified), parsed);
    }

    /// <summary>Text that is not a date is refused rather than guessed at.</summary>
    [Fact]
    public void TextThatIsNotADateIsRefused() =>
        Assert.Throws<FormatException>(() => DateAndTime.FromString("the third of never"));

    /// <summary>
    /// The difference between two dates is signed, because "how long until" and "how long since"
    /// are the same question and a magnitude answers neither.
    /// </summary>
    [Fact]
    public void ADateDifferenceIsSigned()
    {
        DateTime start = new(2026, 1, 1);
        DateTime end = new(2026, 1, 8);

        Assert.Equal(7.0, Duration.ByDateDifference(start, end).TotalDays);
        Assert.Equal(-7.0, Duration.ByDateDifference(end, start).TotalDays);
    }

    /// <summary>
    /// <b>The day of the week is text, not a number.</b> .NET starts its week on Sunday at zero and
    /// ISO 8601 starts on Monday at one, so a node returning 3 would be right under one and wrong
    /// under the other with nothing to say which.
    /// </summary>
    [Fact]
    public void TheDayOfTheWeekIsNamedRatherThanNumbered() =>
        Assert.Equal("Wednesday", DateAndTime.DayOfWeek(new DateTime(2026, 9, 2)));

    /// <summary>Adding a span and taking it away again lands where it started.</summary>
    [Fact]
    public void AddingAndSubtractingASpanCancel()
    {
        DateTime start = new(2026, 9, 1, 12, 0, 0);
        TimeSpan span = Duration.Create(days: 2, hours: 3, minutes: 30);

        Assert.Equal(start, DateAndTime.SubtractTimeSpan(DateAndTime.AddTimeSpan(start, span), span));
    }

    /// <summary>
    /// A span's parts are not range-checked against a clock: ninety minutes is an hour and a half,
    /// which is how somebody types it without doing the arithmetic first.
    /// </summary>
    [Fact]
    public void ASpanAcceptsPartsThatOverflowTheirUnit() =>
        Assert.Equal(1.5, Duration.Create(minutes: 90).TotalHours);

    /// <summary>Every node in the set imported and is reachable by the name it declares.</summary>
    [Theory]
    [InlineData("DateTime.Now")]
    [InlineData("DateTime.Today")]
    [InlineData("DateTime.ByDateAndTime")]
    [InlineData("DateTime.Components")]
    [InlineData("DateTime.FromString")]
    [InlineData("DateTime.Format")]
    [InlineData("DateTime.AddTimeSpan")]
    [InlineData("DateTime.SubtractTimeSpan")]
    [InlineData("DateTime.DayOfWeek")]
    [InlineData("DateTime.DayOfYear")]
    [InlineData("DateTime.DaysInMonth")]
    [InlineData("DateTime.IsLeapYear")]
    [InlineData("TimeSpan.Create")]
    [InlineData("TimeSpan.ByDateDifference")]
    [InlineData("TimeSpan.Components")]
    [InlineData("TimeSpan.TotalDays")]
    [InlineData("TimeSpan.TotalHours")]
    [InlineData("TimeSpan.TotalMinutes")]
    [InlineData("TimeSpan.TotalSeconds")]
    public void EveryNodeImported(string name)
    {
        NodeDefinition definition = Library.ByName(name);

        Assert.Equal(NodeCategories.Input, definition.Category);
        Assert.False(string.IsNullOrWhiteSpace(definition.Description));
    }

    private static NodeLibrary Library { get; } = BuildLibrary();

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(DateAndTime).Assembly));

        return library;
    }
}
